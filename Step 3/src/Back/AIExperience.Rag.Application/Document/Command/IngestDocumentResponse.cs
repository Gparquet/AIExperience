using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>Résultat de l'ingestion : statut final du document (Completed/Failed) et message d'erreur générique éventuel.</summary>
public sealed record IngestDocumentResponse
{
    public Guid DocumentId { get; init; }
    public IngestionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}
