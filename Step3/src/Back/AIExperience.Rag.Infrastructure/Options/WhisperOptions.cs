namespace AIExperience.Rag.Infrastructure.Options;

/// <summary>
/// Options de configuration pour le service de transcription Whisper.net.
/// Lié à la section "Whisper" de appsettings.json.
/// </summary>
public sealed class WhisperOptions
{
    /// <summary>Nom de la section dans appsettings.json.</summary>
    public const string SectionName = "Whisper";

    /// <summary>Chemin absolu vers le fichier modèle Whisper (.bin), ex: C:\Models\Whisper\ggml-medium.bin</summary>
    public required string ModelPath { get; set; }

    /// <summary>Nombre de threads CPU alloués à la transcription. Défaut : 4.</summary>
    public int Threads { get; set; } = 4;
}
