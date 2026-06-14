namespace AIExperience.Rag.Application.Video.Command
{
    public sealed record TranscribeVideoResponse
    {
        /// <summary>Transcription brute avec timestamps</summary>
        public required string RawTranscription { get; init; }

        /// <summary>Transcription nettoyée par le LLM (si demandé)</summary>
        public string? CleanedTranscription { get; init; }

        /// <summary>Durée totale de la vidéo/audio</summary>
        public required TimeSpan Duration { get; init; }

        /// <summary>Nombre de segments transcrits</summary>
        public required int SegmentCount { get; init; }

        /// <summary>ID du document créé dans le RAG (si auto-ingest activé)</summary>
        public Guid? DocumentId { get; init; }

        /// <summary>Temps de traitement total</summary>
        public required TimeSpan ProcessingTime { get; init; }
    }
}
