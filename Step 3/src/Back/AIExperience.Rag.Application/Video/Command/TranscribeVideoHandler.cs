using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.Video;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AIExperience.Rag.Application.Video.Command;

/// <summary>
/// Handler pour <see cref="TranscribeVideoCommand"/>.
/// Pipeline : extraction audio → transcription Whisper → nettoyage LLM (optionnel) → ingestion RAG (optionnel).
/// </summary>
public sealed class TranscribeVideoHandler
    : IRequestHandler<TranscribeVideoCommand, TranscribeVideoResponse>
{
    private readonly IVideoProcessorService _videoProcessor;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IChatClient _chatClient;
    private readonly IIngestionService _ingestionService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TranscribeVideoHandler> _logger;

    /// <summary>Extensions considérées comme de l'audio pur — pas besoin d'extraction FFmpeg.</summary>
    private static readonly string[] AudioExtensions =
        [".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    /// <summary>UserId fixe en développement — doit correspondre au DefaultUserId de DocumentsController.</summary>
    private const string DevUserId = "1ea95468-3f27-4a6d-8fb3-25fdd1530023";

    public TranscribeVideoHandler(
        IVideoProcessorService videoProcessor,
        ITranscriptionService transcriptionService,
        IChatClient chatClient,
        IIngestionService ingestionService,
        IDocumentRepository documentRepository,
        IUnitOfWork unitOfWork,
        ILogger<TranscribeVideoHandler> logger)
    {
        _videoProcessor = videoProcessor;
        _transcriptionService = transcriptionService;
        _chatClient = chatClient;
        _ingestionService = ingestionService;
        _documentRepository = documentRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TranscribeVideoResponse> Handle(
        TranscribeVideoCommand request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var extension = Path.GetExtension(request.FilePath).ToLowerInvariant();
        var isAudioOnly = AudioExtensions.Contains(extension);
        string audioPath;

        // ── ÉTAPE 1 : Extraction audio (uniquement pour les fichiers vidéo) ──
        if (isAudioOnly)
        {
            _logger.LogInformation("Fichier audio détecté, pas d'extraction nécessaire : {File}", request.FilePath);
            audioPath = request.FilePath;
        }
        else
        {
            _logger.LogInformation("Extraction audio depuis la vidéo : {File}", request.FilePath);
            var tempAudioPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
            audioPath = await _videoProcessor.ExtractAudioAsync(request.FilePath, tempAudioPath, cancellationToken);
        }

        try
        {
            // ── ÉTAPE 2 : Transcription via Whisper ──
            _logger.LogInformation("Transcription en cours (langue: {Lang})...", request.Language);
            var result = await _transcriptionService.TranscribeAsync(audioPath, request.Language, cancellationToken);
            _logger.LogInformation(
                "Transcription terminée : {Segments} segments, durée {Duration}",
                result.Segments.Count, result.Duration);

            // ── ÉTAPE 3 : Nettoyage via LLM local (optionnel) ──
            string? cleanedText = null;
            if (request.CleanWithLlm)
            {
                _logger.LogInformation("Nettoyage de la transcription via LLM local...");
                cleanedText = await CleanTranscriptionAsync(result.FullText, cancellationToken);
            }

            // ── ÉTAPE 4 : Injection dans le pipeline RAG (optionnel) ──
            Guid? documentId = null;
            if (request.AutoIngest)
            {
                var textToIngest = cleanedText ?? result.FullText;
                var title = request.Title ?? Path.GetFileNameWithoutExtension(request.FilePath);

                _logger.LogInformation("Création du document et injection dans le pipeline RAG : {Title}", title);

                // Créer l'entité Document en base de données
                var metadata = DocumentMetadata.Create(
                    title: title,
                    language: result.Language,
                    tags: [isAudioOnly ? "audio" : "video"]);

                var document = global::AIExperience.Rag.Domain.Entities.Document.Create(
                    fileName: Path.GetFileName(request.FilePath),
                    contentType: isAudioOnly ? "audio/mpeg" : "video/mp4",
                    fileSizeBytes: new FileInfo(request.FilePath).Length,
                    userId: DevUserId,
                    metadata: metadata);

                document.MarkAsProcessing();
                await _documentRepository.AddAsync(document, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Ingérer le texte transcrit (sans re-parsing de fichier)
                await _ingestionService.IngestTextAsync(textToIngest, document.Id, metadata, cancellationToken);

                document.MarkAsCompleted();
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                documentId = document.Id;
            }

            stopwatch.Stop();

            return new TranscribeVideoResponse
            {
                RawTranscription = result.FullText,
                CleanedTranscription = cleanedText,
                Duration = result.Duration,
                SegmentCount = result.Segments.Count,
                DocumentId = documentId,
                ProcessingTime = stopwatch.Elapsed
            };
        }
        finally
        {
            // Supprimer le fichier audio temporaire généré par FFmpeg
            if (!isAudioOnly && File.Exists(audioPath))
                File.Delete(audioPath);
        }
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
    /// Nettoie la transcription brute via le LLM local :
    /// supprime les hésitations, structure en paragraphes, corrige la ponctuation.
    /// Les timestamps sont retirés avant envoi pour réduire la consommation de tokens.
    /// </summary>
    private async Task<string> CleanTranscriptionAsync(string rawText, CancellationToken cancellationToken)
    {
        // Supprimer les préfixes "[HH:MM:SS → HH:MM:SS]" — inutiles pour le nettoyage, consomment ~8 tokens chacun
        var textWithoutTimestamps = TimestampPrefix.Replace(rawText, string.Empty).Trim();

        // Tronquer si le texte dépasse la context window du modèle local
        var textToSend = textWithoutTimestamps.Length > MaxTranscriptionCharsForLlm
            ? textWithoutTimestamps[..MaxTranscriptionCharsForLlm]
            : textWithoutTimestamps;

        if (textWithoutTimestamps.Length > MaxTranscriptionCharsForLlm)
            _logger.LogWarning(
                "Transcription tronquée pour le nettoyage LLM : {Original} → {Truncated} caractères.",
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

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return response.Text;
    }
}
