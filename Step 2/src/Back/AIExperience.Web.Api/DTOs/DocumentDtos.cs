using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Web.Api.DTOs;

public record DocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Status,
    DateTimeOffset CreatedAt);

public record UploadDocumentRequest(
    ChunkingStrategy ChunkingStrategy = ChunkingStrategy.Recursive);
