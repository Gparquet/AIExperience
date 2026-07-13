using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Application.Video.Command;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Web.Api.DTOs;
using AIExperience.Web.Api.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AIExperience.Web.Api.Controllers;

/// <summary>
/// Endpoint dédié à la transcription de fichiers vidéo et audio.
/// Accepte un fichier multipart, met la transcription Whisper en file (traitée en arrière-plan)
/// et indexe optionnellement le résultat dans le pipeline RAG.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VideoController(
    ICommandDispatcher dispatcher,
    IDocumentRepository documentRepository,
    IngestionSignal ingestionSignal,
    IOptions<IngestionOptions> ingestionOptions,
    IContentTypeResolver contentTypeResolver) : ControllerBase
{
    /// <summary>
    /// Reçoit le fichier, le persiste dans le répertoire de travail et met la transcription en
    /// file — le traitement (extraction audio, Whisper, nettoyage LLM optionnel, indexation
    /// optionnelle, potentiellement long sur une vidéo de plusieurs dizaines de minutes) se déroule
    /// hors requête. Une fois le document "Completed", le texte est récupérable via
    /// <see cref="GetTranscription"/>.
    /// </summary>
    /// <param name="file">Fichier vidéo (.mp4, .mkv, .webm, .avi, .mov) ou audio (.wav, .mp3, .m4a).</param>
    /// <param name="language">Code langue ISO pour la transcription (défaut : "fr").</param>
    /// <param name="cleanWithLlm">Nettoyer la transcription brute via le LLM local (défaut : false). Non utilisé pour l'ingestion RAG — enrichit uniquement la transcription consultable après coup.</param>
    /// <param name="autoIngest">Indexer automatiquement dans le RAG (défaut : true).</param>
    /// <param name="title">Titre du document dans le RAG (défaut : nom du fichier).</param>
    [HttpPost("transcribe")]
    [DisableRequestSizeLimit]                                          // Désactive la limite Kestrel de 30 MB
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]      // Autorise les fichiers vidéo volumineux
    public async Task<ActionResult<DocumentResponse>> Transcribe(
        IFormFile file,
        [FromQuery] string language = "fr",
        [FromQuery] bool cleanWithLlm = false,
        [FromQuery] bool autoIngest = true,
        [FromQuery] string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Fichier manquant ou vide.");

        var documentId = Guid.NewGuid();
        var filePath = await WorkFileStore.SaveAsync(file, documentId, ingestionOptions.Value.WorkDirectory, cancellationToken);
        var contentType = contentTypeResolver.Resolve(file.FileName);

        var createResponse = await dispatcher.SendAsync(new CreateVideoTranscriptionJobCommand
        {
            Id = documentId,
            FileName = file.FileName,
            ContentType = contentType,
            FileSizeBytes = file.Length,
            FileReference = filePath,
            Language = language,
            CleanWithLlm = cleanWithLlm,
            IndexInRag = autoIngest,
            Title = title
        }, cancellationToken);

        // Réveille le worker immédiatement plutôt que de le laisser attendre le prochain tour de sonde.
        ingestionSignal.Pulse();

        // Le handler a déjà persisté le document et renvoyé tout ce qu'il faut pour la réponse :
        // pas besoin de requêter à nouveau le repository, ContentType/FileSizeBytes sont déjà connus
        // côté contrôleur (calculés juste au-dessus) et ErrorMessage est forcément nul à la création.
        return Accepted(new DocumentResponse(
            createResponse.DocumentId, createResponse.FileName, contentType, file.Length,
            createResponse.Status.ToString(), createResponse.CreatedAt, ErrorMessage: null));
    }

    /// <summary>
    /// Récupère la transcription d'un document vidéo/audio une fois le traitement terminé.
    /// Renvoie des champs vides tant que le document n'est pas encore "Completed".
    /// </summary>
    [HttpGet("{id:guid}/transcription")]
    public async Task<ActionResult<VideoTranscriptionResponse>> GetTranscription(Guid id)
    {
        var document = await documentRepository.GetByIdAsync(id);
        if (document is null) return NotFound();

        return Ok(new VideoTranscriptionResponse(document.RawTranscription, document.CleanedTranscription));
    }
}
