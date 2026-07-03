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

/// <summary>Résultat de la ré-ingestion (R-15) : nombre de chunks ré-embeddés avec le préfixe nomic correct.</summary>
public record ReembedCorpusResponse(int ChunksReembedded, long DurationMs);
