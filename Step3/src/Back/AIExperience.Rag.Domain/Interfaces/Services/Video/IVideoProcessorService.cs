namespace AIExperience.Rag.Domain.Interfaces.Services.Video
{
    /// <summary>
    /// Extrait la piste audio d'un fichier vidéo.
    /// Analogie : c'est le technicien qui retire la bande-son d'un DVD
    /// pour qu'on puisse la transcrire séparément.
    /// </summary>
    public interface IVideoProcessorService
    {
        /// <summary>
        /// Extrait l'audio d'une vidéo et le convertit en WAV 16kHz mono
        /// (format attendu par Whisper).
        /// </summary>
        /// <param name="videoPath">Chemin vers le fichier vidéo source</param>
        /// <param name="outputAudioPath">Chemin de sortie pour le fichier WAV</param>
        /// <param name="cancellationToken">Token d'annulation</param>
        /// <returns>Le chemin du fichier audio extrait</returns>
        Task<string> ExtractAudioAsync(
            string videoPath,
            string outputAudioPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Vérifie si le format vidéo est supporté.
        /// </summary>
        bool IsSupported(string filePath);

        /// <summary>
        /// Sonde la durée totale d'un média (vidéo ou audio). Utilisée pour convertir l'avancement
        /// de transcription en pourcentage. Best-effort : renvoie <c>null</c> si la durée ne peut
        /// pas être déterminée (fichier illisible, sondage en échec) — l'avancement devient alors
        /// indéterminé côté UI plutôt que de faire échouer le traitement.
        /// </summary>
        Task<TimeSpan?> TryGetMediaDurationAsync(string path, CancellationToken cancellationToken = default);
    }
}
