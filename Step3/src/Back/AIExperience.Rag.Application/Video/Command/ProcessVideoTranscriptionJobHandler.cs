using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.Video;
using AIExperience.Rag.Domain.Models;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AIExperience.Rag.Application.Video.Command;

/// <summary>
/// Handler pour <see cref="ProcessVideoTranscriptionJobCommand"/>, invoqué par le worker
/// d'ingestion en arrière-plan. Pipeline : extraction audio (si vidéo) → transcription Whisper →
/// nettoyage LLM (optionnel) → indexation RAG (optionnelle) → transcription persistée pour
/// consultation après coup.
/// </summary>
public sealed class ProcessVideoTranscriptionJobHandler(
    IVideoProcessorService videoProcessor,
    ITranscriptionService transcriptionService,
    IChatClient chatClient,
    Domain.Interfaces.Services.IIngestionService ingestionService,
    IDocumentRepository documentRepository,
    DocumentIngestionStatusUpdater statusUpdater,
    IIngestionProgressReporter progressReporter,
    ILogger<ProcessVideoTranscriptionJobHandler> logger)
    : IRequestHandler<ProcessVideoTranscriptionJobCommand, ProcessVideoTranscriptionJobResponse>
{
    /// <summary>Extensions considérées comme de l'audio pur — pas besoin d'extraction FFmpeg.</summary>
    private static readonly string[] AudioExtensions =
        [".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    public async Task<ProcessVideoTranscriptionJobResponse> Handle(ProcessVideoTranscriptionJobCommand request, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document introuvable après création : {request.DocumentId}");

        var filePath = document.FileReference
            ?? throw new InvalidOperationException($"Document sans fichier de travail référencé : {document.Id}");

        await statusUpdater.MarkProcessingAsync(document, cancellationToken);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var isAudioOnly = AudioExtensions.Contains(extension);
        var audioPath = filePath;

        try
        {
            if (!isAudioOnly)
            {
                logger.LogInformation("Extraction audio depuis la vidéo : {File}", filePath);
                await progressReporter.EnterStageAsync(document.Id, IngestionStage.ExtractingAudio, cancellationToken);
                var tempAudioPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
                audioPath = await videoProcessor.ExtractAudioAsync(filePath, tempAudioPath, cancellationToken);
            }

            logger.LogInformation("Transcription en cours (langue : {Lang})...", document.Metadata.Language);
            await progressReporter.EnterStageAsync(document.Id, IngestionStage.Transcribing, cancellationToken);
            var totalDuration = await videoProcessor.TryGetMediaDurationAsync(audioPath, cancellationToken) ?? TimeSpan.Zero;
            var result = await transcriptionService.TranscribeAsync(audioPath, document.Metadata.Language,
                onSegment: seg =>
                {
                    int? pct = totalDuration > TimeSpan.Zero
                        ? Math.Clamp((int)(100.0 * seg.End.TotalSeconds / totalDuration.TotalSeconds), 0, 99)
                        : null;
                    _ = progressReporter.ReportAsync(document.Id, IngestionStage.Transcribing, pct, new IngestionProgressCounters());
                },
                cancellationToken);
            logger.LogInformation("Transcription terminée : {Segments} segments, durée {Duration}",
                result.Segments.Count, result.Duration);

            string? cleanedText = null;
            if (document.CleanTranscriptionWithLlm)
            {
                logger.LogInformation("Nettoyage de la transcription via LLM local...");
                cleanedText = await CleanTranscriptionAsync(result.FullText, cancellationToken);
            }

            if (document.IndexInRag)
                await ingestionService.IngestFromSegmentsAsync(result.Segments, document.Id, document.Metadata, cancellationToken);

            document.SetTranscription(result.FullText, cleanedText);
            await statusUpdater.MarkCompletedAsync(document, cancellationToken);
            await progressReporter.ClearAsync(document.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Annulation explicite (arrêt du worker, timeout) : pas un échec métier, on laisse
            // l'exception se propager sans marquer le document en erreur — il sera repris au
            // prochain tour de sonde puisque le message outbox correspondant reste non traité.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Échec de la transcription du document {DocumentId} ({FilePath})", document.Id, filePath);
            await statusUpdater.MarkFailedAsync(document,
                "La transcription a échoué. Consultez les journaux serveur pour le détail.", cancellationToken);
        }
        finally
        {
            // Le fichier audio intermédiaire extrait par FFmpeg n'est qu'un artefact de travail
            // interne à ce traitement — il peut être supprimé quelle que soit l'issue.
            if (!isAudioOnly && File.Exists(audioPath))
                File.Delete(audioPath);
        }

        // Le fichier vidéo/audio d'origine, lui, n'a plus lieu d'être une fois le document arrivé
        // dans un état final (succès ou échec) — une annulation, elle, propage l'exception plus
        // haut avant d'atteindre cette ligne, laissant le fichier intact pour une reprise.
        if (File.Exists(filePath))
            File.Delete(filePath);

        return new ProcessVideoTranscriptionJobResponse
        {
            DocumentId = document.Id,
            Status = document.Status,
            ErrorMessage = document.ErrorMessage
        };
    }

    /// <summary>
    /// Seuil de caractères au-delà duquel la transcription est tronquée avant envoi au LLM.
    /// Correspond à ~3 000 tokens pour un modèle local 4 096 tokens — laisse de la place au prompt et à la réponse.
    /// </summary>
    private const int MaxTranscriptionCharsForLlm = 8_000;

    /// <summary>Regex qui supprime les préfixes de timestamps Whisper : "[00:00:00 → 00:00:09] ".</summary>
    private static readonly Regex TimestampPrefix =
        new(@"\[\d{2}:\d{2}:\d{2} → \d{2}:\d{2}:\d{2}\] ?", RegexOptions.Compiled);

    /// <summary>
    /// Nettoie la transcription brute via le LLM local : supprime les hésitations, structure en
    /// paragraphes, corrige la ponctuation. Les timestamps sont retirés avant envoi pour réduire
    /// la consommation de tokens.
    /// </summary>
    private async Task<string> CleanTranscriptionAsync(string rawText, CancellationToken cancellationToken)
    {
        var textWithoutTimestamps = TimestampPrefix.Replace(rawText, string.Empty).Trim();

        var textToSend = textWithoutTimestamps.Length > MaxTranscriptionCharsForLlm
            ? textWithoutTimestamps[..MaxTranscriptionCharsForLlm]
            : textWithoutTimestamps;

        if (textWithoutTimestamps.Length > MaxTranscriptionCharsForLlm)
            logger.LogWarning("Transcription tronquée pour le nettoyage LLM : {Original} → {Truncated} caractères.",
                textWithoutTimestamps.Length, MaxTranscriptionCharsForLlm);

        var prompt = $"""
            Tu es un assistant spécialisé dans le nettoyage de transcriptions audio.
            Voici une transcription brute issue d'un enregistrement.

            Consignes :
            - Supprime les hésitations (euh, hum, ben, alors euh...)
            - Corrige la ponctuation et la grammaire
            - Structure le texte en paragraphes thématiques
            - Ajoute des titres de sections si des sujets distincts sont abordés
            - Conserve TOUT le contenu informatif — ne résume pas, ne supprime aucune information
            - Garde le vocabulaire technique métier tel quel

            Transcription brute :
            {textToSend}
            """;

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return response.Text;
    }
}
