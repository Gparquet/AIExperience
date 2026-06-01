using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Web.Api.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace AIExperience.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(
    ISender sender,
    IIngestionService ingestionService,
    IDocumentRepository documentRepository) : ControllerBase
{
    private const string DefaultUserId = "1ea95468-3f27-4a6d-8fb3-25fdd1530023";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentResponse>>> GetAll()
    {
        var (documents, _) = await documentRepository.GetByUserIdAsync(DefaultUserId, page: 1, pageSize: 100);
        return Ok(documents.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentResponse>> GetById(Guid id)
    {
        var doc = await documentRepository.GetByIdAsync(id);
        return doc is null ? NotFound() : Ok(ToResponse(doc));
    }

    [HttpPost]
    public async Task<ActionResult<DocumentResponse>> Upload(
        IFormFile file,
        [FromQuery] ChunkingStrategy strategy = ChunkingStrategy.Recursive)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Fichier manquant.");

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(file.FileName));

        await using (var stream = System.IO.File.Create(tempPath))
            await file.CopyToAsync(stream);

        try
        {
            var uploadResponse = await sender.Send(new UploadDocumentCommand
            {
                FileName = file.FileName,
                ContentType = GetContentType(file.FileName),
                FileSizeBytes = file.Length,
                UserId = DefaultUserId,
                DocumentMetadata = new DocumentMetadata { Title = file.FileName },
                ChunkingStrategy = strategy
            });

            var doc = await documentRepository.GetByIdAsync(uploadResponse.DocumentId);
            if (doc is null) return StatusCode(500, "Document introuvable après création.");

            try
            {
                await ingestionService.IngestAsync(tempPath, uploadResponse.DocumentId,
                    new DocumentMetadata { Title = file.FileName });
                doc.MarkAsCompleted();
            }
            catch (Exception ex)
            {
                doc.MarkAsFailed(ex.Message);
            }

            await documentRepository.UpdateAsync(doc);
            return CreatedAtAction(nameof(GetById), new { id = doc.Id }, ToResponse(doc));
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var doc = await documentRepository.GetByIdAsync(id);
        if (doc is null) return NotFound();
        await documentRepository.DeleteAsync(id);
        return NoContent();
    }

    private static DocumentResponse ToResponse(AIExperience.Rag.Domain.Entities.Document d) =>
        new(d.Id, d.FileName, d.ContentType, d.FileSizeBytes, d.Status.ToString(), d.CreatedAt);

    private static string GetContentType(string fileName)
    {
        var provider = new FileExtensionContentTypeProvider();
        return provider.TryGetContentType(fileName, out var ct) ? ct : "application/octet-stream";
    }
}
