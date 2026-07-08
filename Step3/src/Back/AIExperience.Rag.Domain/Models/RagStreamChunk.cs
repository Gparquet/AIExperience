namespace AIExperience.Rag.Domain.Models;

public sealed record RagStreamChunk
{
    public string? Token { get; init; }
    public bool IsDone { get; init; }
    public RagResponse? FinalResponse { get; init; }
}
