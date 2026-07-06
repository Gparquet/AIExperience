using AIExperience.Rag.Application.Video;
using AIExperience.Rag.Application.Video.Command;
using AIExperience.Web.Api.Helpers;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AIExperience.Web.Api.Controllers;

/// <summary>
/// Endpoint dédié à la transcription de fichiers vidéo et audio.
/// Accepte un fichier multipart, transcrit via Whisper local et indexe dans le pipeline RAG.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VideoController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Transcrit un fichier vidéo ou audio et l'injecte optionnellement dans le RAG.
    /// </summary>
    /// <param name="file">Fichier vidéo (.mp4, .mkv, .webm, .avi, .mov) ou audio (.wav, .mp3, .m4a).</param>
    /// <param name="language">Code langue ISO pour la transcription (défaut : "fr").</param>
    /// <param name="cleanWithLlm">Nettoyer la transcription brute via le LLM local (défaut : false). Non utilisé pour l'ingestion RAG — enrichit uniquement la réponse affichée.</param>
    /// <param name="autoIngest">Injecter automatiquement dans le pipeline RAG (défaut : true).</param>
    /// <param name="title">Titre du document dans le RAG (défaut : nom du fichier).</param>
    [HttpPost("transcribe")]
    [DisableRequestSizeLimit]                                          // Désactive la limite Kestrel de 30 MB
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]      // Autorise les fichiers vidéo volumineux
    public async Task<ActionResult<TranscribeVideoResponse>> Transcribe(
        IFormFile file,
        [FromQuery] string language = "fr",
        [FromQuery] bool cleanWithLlm = false,
        [FromQuery] bool autoIngest = true,
        [FromQuery] string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Fichier manquant ou vide.");

        // Sauvegarde le fichier temporairement (supprimé automatiquement à la fin du bloc) pour que le handler puisse y accéder
        await using var tempFile = await TempUploadedFile.CreateAsync(file, cancellationToken);

        var response = await sender.Send(new TranscribeVideoCommand
        {
            FilePath = tempFile.Path,
            Language = language,
            CleanWithLlm = cleanWithLlm,
            AutoIngest = autoIngest,
            Title = title ?? Path.GetFileNameWithoutExtension(file.FileName)
        }, cancellationToken);

        return Ok(response);
    }
}
