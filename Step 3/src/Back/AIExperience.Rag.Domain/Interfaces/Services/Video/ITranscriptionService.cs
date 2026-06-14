using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Domain.Interfaces.Services.Video
{
    /// <summary>
    /// Transcrit un fichier audio en texte.
    /// Analogie : c'est le sténographe qui écoute un enregistrement
    /// et tape tout ce qui se dit, mot pour mot.
    /// </summary>
    public interface ITranscriptionService
    {
        /// <summary>
        /// Transcrit un fichier audio en segments de texte avec timestamps.
        /// </summary>
        /// <param name="audioPath">Chemin vers le fichier audio WAV 16kHz mono</param>
        /// <param name="language">Code langue ISO (ex: "fr" pour français)</param>
        /// <param name="cancellationToken">Token d'annulation</param>
        /// <returns>Liste de segments transcrits avec leurs timestamps</returns>
        Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            string language = "fr",
            CancellationToken cancellationToken = default);
    }
}
