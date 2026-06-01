using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Application.Document.Command;

public sealed record UploadDocumentResponse
{
    public Guid DocumentId { get; init; }
    public IngestionStatus Status { get; init; }
    public string FileName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
