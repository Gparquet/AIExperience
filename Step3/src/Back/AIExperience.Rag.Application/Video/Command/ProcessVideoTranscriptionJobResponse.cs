using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Application.Video.Command;

/// <summary>Résultat du traitement : statut final du document (Completed/Failed) et message d'erreur générique éventuel.</summary>
public sealed record ProcessVideoTranscriptionJobResponse
{
    public Guid DocumentId { get; init; }
    public IngestionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}
