using AIExperience.Rag.Domain.Interfaces.Services.Video;

namespace AIExperience.Rag.Domain.Models.Video
{
    /// <summary>
    /// Résultat complet d'une transcription.
    /// </summary>
    public sealed record TranscriptionResult
    {
        /// <summary>Texte complet de la transcription</summary>
        public required string FullText { get; init; }

        /// <summary>Segments individuels avec timestamps</summary>
        public required IReadOnlyList<TranscriptionSegment> Segments { get; init; }

        /// <summary>Durée totale de l'audio</summary>
        public required TimeSpan Duration { get; init; }

        /// <summary>Langue détectée ou spécifiée</summary>
        public required string Language { get; init; }
    }
}
