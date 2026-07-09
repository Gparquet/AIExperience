using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Application.Video.Command;

/// <summary>Résultat de la création du job : identifiant et statut initial (toujours "Pending" à ce stade).</summary>
public sealed record CreateVideoTranscriptionJobResponse
{
    public Guid DocumentId { get; init; }
    public IngestionStatus Status { get; init; }
    public string FileName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
