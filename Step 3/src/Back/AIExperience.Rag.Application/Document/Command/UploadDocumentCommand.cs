using AIExperience.Rag.Domain.Enums;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

public sealed record UploadDocumentCommand : IRequest<UploadDocumentResponse>
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string UserId { get; init; }
    public required DocumentMetadata DocumentMetadata { get; init; }
    public ChunkingStrategy ChunkingStrategy { get; init; } = ChunkingStrategy.Recursive;
}
