using AIExperience.Rag.Domain.Interfaces.Services.Video;
using AIExperience.Rag.Domain.Models.Video;
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;

namespace AIExperience.Rag.Infrastructure.AI.Transcription;

/// <summary>
/// Transcrit un fichier audio en texte via Whisper.net (modèle local, 100% offline).
/// Le modèle est chargé une seule fois au premier appel et réutilisé (Singleton).
/// Analogie : un sténographe local installé sur votre machine.
/// </summary>
public sealed class WhisperTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly WhisperOptions _options;

    /// <summary>Processeur Whisper partagé, initialisé une seule fois (lazy + thread-safe).</summary>
    private WhisperProcessor? _processor;
    private WhisperFactory? _factory;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public WhisperTranscriptionService(
        IOptions<WhisperOptions> options,
        ILogger<WhisperTranscriptionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string language = "fr",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("Fichier audio introuvable.", audioPath);

        var processor = await GetOrCreateProcessorAsync(language);

        _logger.LogInformation("Début de la transcription : {File}", audioPath);

        var segments = new List<TranscriptionSegment>();
        var fullText = new System.Text.StringBuilder();

        await using var fileStream = File.OpenRead(audioPath);

        await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
        {
            var ts = new TranscriptionSegment
            {
                Start = segment.Start,
                End = segment.End,
                Text = segment.Text.Trim()
            };

            segments.Add(ts);

            // Format avec timestamps : [00:00:05 → 00:00:12] Voici le contenu...
            fullText.AppendLine($"[{segment.Start:hh\\:mm\\:ss} → {segment.End:hh\\:mm\\:ss}] {segment.Text.Trim()}");
        }

        var duration = segments.LastOrDefault()?.End ?? TimeSpan.Zero;

        _logger.LogInformation(
            "Transcription terminée : {Count} segments, durée totale {Duration}",
            segments.Count, duration);

        return new TranscriptionResult
        {
            FullText = fullText.ToString(),
            Segments = segments.AsReadOnly(),
            Duration = duration,
            Language = language
        };
    }

    /// <summary>
    /// Initialise le processeur Whisper lors du premier appel (chargement du modèle .bin).
    /// Thread-safe via SemaphoreSlim — le modèle est chargé une seule fois.
    /// </summary>
    private async Task<WhisperProcessor> GetOrCreateProcessorAsync(string language)
    {
        if (_processor is not null)
            return _processor;

        await _initLock.WaitAsync();
        try
        {
            if (_processor is not null)
                return _processor;

            _logger.LogInformation("Chargement du modèle Whisper : {Model}", _options.ModelPath);

            if (!File.Exists(_options.ModelPath))
                throw new FileNotFoundException(
                    $"Modèle Whisper introuvable à '{_options.ModelPath}'. " +
                    "Téléchargez ggml-medium.bin depuis Hugging Face (ggerganov/whisper.cpp).",
                    _options.ModelPath);

            _factory = WhisperFactory.FromPath(_options.ModelPath);

            // WithTranslate() active la traduction (non souhaité ici — on transcrit sans traduire)
            _processor = _factory.CreateBuilder()
                .WithLanguage(language)
                .WithThreads(_options.Threads)
                .Build();

            _logger.LogInformation("Modèle Whisper chargé avec succès.");
            return _processor;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _initLock.Dispose();
    }
}
