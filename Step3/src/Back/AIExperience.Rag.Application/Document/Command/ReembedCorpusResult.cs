namespace AIExperience.Rag.Application.Document.Command;

/// <summary>Résultat de la ré-ingestion : nombre de chunks ré-embeddés et durée de l'opération.</summary>
public sealed record ReembedCorpusResult
{
    public int ChunksReembedded { get; init; }
    public long DurationMs { get; init; }
}
