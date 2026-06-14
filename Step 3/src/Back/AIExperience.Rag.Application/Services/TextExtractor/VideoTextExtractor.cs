using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.Video;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur de texte pour les fichiers vidéo et audio.
/// S'intègre dans le <see cref="ICompositeTextExtractor"/> comme les autres extracteurs (PDF, HTML) :
/// quand un fichier vidéo est uploadé via POST /api/documents, cet extracteur est sélectionné automatiquement.
/// Pipeline interne : extraction audio FFmpeg (si vidéo) → transcription Whisper → texte brut.
/// </summary>
public sealed class VideoTextExtractor : ITextExtractor
{
    private readonly IVideoProcessorService _videoProcessor;
    private readonly ITranscriptionService _transcriptionService;

    private static readonly string[] SupportedVideoExtensions =
        [".mp4", ".mkv", ".webm", ".avi", ".mov"];

    private static readonly string[] SupportedAudioExtensions =
        [".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    public VideoTextExtractor(
        IVideoProcessorService videoProcessor,
        ITranscriptionService transcriptionService)
    {
        _videoProcessor = videoProcessor;
        _transcriptionService = transcriptionService;
    }

    /// <inheritdoc/>
    public bool CanHandle(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedVideoExtensions.Contains(ext) || SupportedAudioExtensions.Contains(ext);
    }

    /// <inheritdoc/>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var isVideo = SupportedVideoExtensions.Contains(ext);
        string audioPath;

        if (isVideo)
        {
            // Extraire la piste audio dans un fichier WAV temporaire
            var tempAudio = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
            audioPath = await _videoProcessor.ExtractAudioAsync(filePath, tempAudio, cancellationToken);
        }
        else
        {
            // Fichier audio pur — utilisation directe sans extraction
            audioPath = filePath;
        }

        try
        {
            var result = await _transcriptionService.TranscribeAsync(audioPath, "fr", cancellationToken);
            return result.FullText;
        }
        finally
        {
            // Supprimer le WAV temporaire uniquement si on l'a créé
            if (isVideo && File.Exists(audioPath))
                File.Delete(audioPath);
        }
    }
}
