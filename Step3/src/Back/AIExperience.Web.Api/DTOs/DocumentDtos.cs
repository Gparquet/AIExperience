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

/// <summary>Requête de pré-vérification de doublon (nom de fichier + hash calculé côté navigateur).</summary>
public record CheckDuplicateRequest(string FileName, string ContentHash);

/// <summary>Informations minimales sur un document existant, utilisées pour informer l'utilisateur d'un doublon.</summary>
public record ExistingDocumentInfo(Guid Id, string FileName, DateTimeOffset CreatedAt);

/// <summary>
/// Réponse de la pré-vérification de doublon. MatchType est null si IsDuplicate est false, sinon il contient
/// la représentation textuelle de <see cref="DocumentMatchType"/> ("ExactDuplicate" ou "SameNameDifferentContent").
/// </summary>
public record CheckDuplicateResponse(bool IsDuplicate, ExistingDocumentInfo? ExistingDocument, string? MatchType);

/// <summary>
/// Corps de la réponse 409 Conflict renvoyée lorsque l'upload est bloqué par un doublon ou une nouvelle version non confirmée.
/// MatchType contient la représentation textuelle de <see cref="DocumentMatchType"/> ("ExactDuplicate" ou "SameNameDifferentContent").
/// </summary>
public record DuplicateDocumentResponse(ExistingDocumentInfo ExistingDocument, string MatchType);
