using AIExperience.Rag.Domain.Interfaces.Services.Video;
using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Infrastructure.AI.Video;

/// <summary>
/// Extrait la piste audio d'un fichier vidéo via FFmpeg (100% local, aucun appel réseau).
/// Le fichier audio produit est un WAV 16 kHz mono — format exact requis par Whisper.
/// </summary>
public sealed class FFmpegVideoProcessorService : IVideoProcessorService
{
    private readonly ILogger<FFmpegVideoProcessorService> _logger;

    private static readonly string[] SupportedVideoExtensions =
        [".mp4", ".mkv", ".webm", ".avi", ".mov"];

    public FFmpegVideoProcessorService(ILogger<FFmpegVideoProcessorService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> ExtractAudioAsync(
        string videoPath,
        string outputAudioPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Fichier vidéo introuvable.", videoPath);

        _logger.LogInformation("Extraction audio : {Video} → {Audio}", videoPath, outputAudioPath);

        // FFMpegCore convertit la vidéo en WAV 16 kHz mono (format requis par Whisper)
        // Le codec PCM par défaut de FFmpeg pour .wav est pcm_s16le — pas besoin de le spécifier explicitement
        await FFMpegArguments
            .FromFileInput(videoPath)
            .OutputToFile(outputAudioPath, overwrite: true, options => options
                .WithAudioSamplingRate(16000)        // 16 kHz requis par Whisper
                .ForceFormat("wav")
                .WithCustomArgument("-ac 1")         // Mono requis par Whisper
                .WithCustomArgument("-vn"))          // Pas de piste vidéo dans la sortie
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();

        _logger.LogInformation("Audio extrait avec succès : {Audio}", outputAudioPath);
        return outputAudioPath;
    }

    /// <inheritdoc/>
    public bool IsSupported(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedVideoExtensions.Contains(ext);
    }
}
