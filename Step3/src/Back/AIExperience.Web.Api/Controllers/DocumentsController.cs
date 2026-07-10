using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Document.Exceptions;
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Web.Api.DTOs;
using AIExperience.Web.Api.Helpers;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace AIExperience.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(
    ISender sender,
    IDocumentRepository documentRepository,
    IngestionSignal ingestionSignal,
    IOptions<IngestionOptions> ingestionOptions) : ControllerBase
{
    private const string DefaultUserId = "1ea95468-3f27-4a6d-8fb3-25fdd1530023";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentResponse>>> GetAll()
    {
        // GetAllAsync retourne tous les documents sans filtre utilisateur (auth non implémentée)
        var documents = await documentRepository.GetAllAsync();
        return Ok(documents.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentResponse>> GetById(Guid id)
    {
        var doc = await documentRepository.GetByIdAsync(id);
        return doc is null ? NotFound() : Ok(ToResponse(doc));
    }

    /// <summary>
    /// Pré-vérification de doublon (nom + hash calculé côté navigateur), appelée par le front avant
    /// d'envoyer le fichier complet. Purement informatif — l'autorité finale reste <see cref="Upload"/>.
    /// </summary>
    [HttpPost("check-duplicate")]
    public async Task<ActionResult<CheckDuplicateResponse>> CheckDuplicate([FromBody] CheckDuplicateRequest request)
    {
        // Recherche le document le plus récent portant le même nom pour cet utilisateur
        var existing = await documentRepository.GetLatestByFileNameAsync(DefaultUserId, request.FileName);
        if (existing is null)
            return Ok(new CheckDuplicateResponse(false, null, null));

        // Compare les hashes pour distinguer un doublon exact d'un simple conflit de nom
        var matchType = existing.ContentHash == request.ContentHash
            ? DocumentMatchType.ExactDuplicate
            : DocumentMatchType.SameNameDifferentContent;

        // Conversion explicite en string (même convention que ToResponse pour IngestionStatus) : sans elle,
        // System.Text.Json sérialise l'enum en entier brut faute de JsonStringEnumConverter global.
        return Ok(new CheckDuplicateResponse(
            true, new ExistingDocumentInfo(existing.Id, existing.FileName, existing.CreatedAt), matchType.ToString()));
    }

    /// <summary>
    /// Reçoit le fichier, le persiste dans le répertoire de travail et met l'ingestion en file —
    /// le traitement lui-même (parsing/chunking/embedding, potentiellement long) se déroule hors
    /// requête, dans le worker d'ingestion. La réponse arrive dès que le document est créé, avec
    /// un statut encore "Pending" ; le front est notifié de la suite via SignalR ou le polling.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DocumentResponse>> Upload(
        IFormFile file,
        [FromQuery] ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        [FromQuery] Guid? replaceDocumentId = null,
        [FromQuery] string language = "fr",
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Fichier manquant.");

        var documentId = Guid.NewGuid();
        var filePath = await WorkFileStore.SaveAsync(file, documentId, ingestionOptions.Value.WorkDirectory, cancellationToken);
        var contentType = GetContentType(file.FileName);

        UploadDocumentResponse uploadResponse;
        try
        {
            uploadResponse = await sender.Send(new UploadDocumentCommand
            {
                Id = documentId,
                FileName = file.FileName,
                ContentType = contentType,
                FileSizeBytes = file.Length,
                UserId = DefaultUserId,
                DocumentMetadata = new DocumentMetadata { Title = file.FileName, Language = language },
                ChunkingStrategy = strategy,
                FilePath = filePath,
                ReplaceDocumentId = replaceDocumentId
            }, cancellationToken);
        }
        catch (DuplicateDocumentException ex)
        {
            // Le document n'a finalement pas été créé : le fichier de travail fraîchement écrit
            // n'a plus de raison d'exister.
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            // Propage le type de correspondance (doublon exact ou simple conflit de nom) au front-end,
            // converti en string pour éviter la sérialisation en entier brut (cf. CheckDuplicate ci-dessus)
            return Conflict(new DuplicateDocumentResponse(
                new ExistingDocumentInfo(ex.ExistingDocumentId, ex.ExistingFileName, ex.ExistingCreatedAt),
                ex.MatchType.ToString()));
        }

        // Réveille le worker immédiatement plutôt que de le laisser attendre le prochain tour de sonde.
        ingestionSignal.Pulse();

        // Le handler a déjà persisté le document et renvoyé tout ce qu'il faut pour la réponse :
        // pas besoin de requêter à nouveau le repository, ContentType/FileSizeBytes sont déjà connus
        // côté contrôleur (calculés juste au-dessus) et ErrorMessage est forcément nul à la création.
        return AcceptedAtAction(nameof(GetById), new { id = uploadResponse.DocumentId }, new DocumentResponse(
            uploadResponse.DocumentId, uploadResponse.FileName, contentType, file.Length,
            uploadResponse.Status.ToString(), uploadResponse.CreatedAt, ErrorMessage: null));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await sender.Send(new DeleteDocumentCommand { DocumentId = id });
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// À appeler une fois après activation de <c>AI.EmbeddingTaskPrefixes</c> : les vecteurs stockés sans préfixe sont incompatibles
    /// avec des questions désormais préfixées "search_query: ". N'extrait/ne re-chunk rien —
    /// seul l'embedding de chaque chunk existant est recalculé.
    /// </summary>
    [HttpPost("reembed-corpus")]
    public async Task<ActionResult<ReembedCorpusResponse>> ReembedCorpus(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReembedCorpusCommand(), cancellationToken);
        return Ok(new ReembedCorpusResponse(result.ChunksReembedded, result.DurationMs));
    }

    private static DocumentResponse ToResponse(AIExperience.Rag.Domain.Entities.Document d) =>
        new(d.Id, d.FileName, d.ContentType, d.FileSizeBytes, d.Status.ToString(), d.CreatedAt, d.ErrorMessage,
            d.IngestionProgress is null
                ? null
                : new IngestionProgressResponse(
                    d.IngestionProgress.Stage.ToString(),
                    d.IngestionProgress.Percent,
                    new IngestionProgressCountersResponse(
                        d.IngestionProgress.Counters.BatchIndex,
                        d.IngestionProgress.Counters.BatchCount,
                        d.IngestionProgress.Counters.ChunksDone,
                        d.IngestionProgress.Counters.ChunksTotal,
                        d.IngestionProgress.Counters.SegmentsDone,
                        d.IngestionProgress.Counters.SegmentsTotal),
                    d.IngestionProgress.UpdatedAt));

    private static string GetContentType(string fileName)
    {
        var provider = new FileExtensionContentTypeProvider();
        return provider.TryGetContentType(fileName, out var ct) ? ct : "application/octet-stream";
    }
}
