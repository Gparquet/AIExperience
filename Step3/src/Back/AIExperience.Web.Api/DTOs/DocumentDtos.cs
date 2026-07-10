using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Web.Api.DTOs;

public record DocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Status,
    DateTimeOffset CreatedAt,
    string? ErrorMessage = null,
    IngestionProgressResponse? IngestionProgress = null);

/// <summary>Compteurs de détail de l'avancement d'ingestion (voir IngestionProgressCounters).</summary>
public record IngestionProgressCountersResponse(
    int? BatchIndex, int? BatchCount, int? ChunksDone, int? ChunksTotal, int? SegmentsDone, int? SegmentsTotal);

/// <summary>Avancement fin d'ingestion exposé au front (étape textuelle + pourcentage + compteurs).</summary>
public record IngestionProgressResponse(
    string Stage, int? Percent, IngestionProgressCountersResponse Counters, DateTimeOffset UpdatedAt);

/// <summary>Transcription d'une vidéo/audio, consultée après coup une fois le document "Completed".</summary>
public record VideoTranscriptionResponse(string? RawTranscription, string? CleanedTranscription);

public record UploadDocumentRequest(
    ChunkingStrategy ChunkingStrategy = ChunkingStrategy.Recursive);

/// <summary>
/// Résultat d'une ré-ingestion complète du corpus. Le modèle d'embedding nomic exige un préfixe
/// ("search_document: " / "search_query: ") devant le texte pour produire des vecteurs corrects ;
/// cette réponse indique combien de chunks ont été recalculés avec ce préfixe et le temps que ça a pris.
/// </summary>
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
