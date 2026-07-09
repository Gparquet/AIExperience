namespace AIExperience.Rag.Application.Jobs;

/// <summary>
/// Contenu JSON porté par un message outbox d'ingestion — les deux types d'événement
/// (<see cref="IngestionEventTypes.DocumentIngestionRequested"/> et
/// <see cref="IngestionEventTypes.VideoTranscriptionRequested"/>) partagent la même forme :
/// tout ce dont le job a besoin est déjà sur la ligne <c>Document</c> désignée par cet identifiant.
/// </summary>
public sealed record IngestionJobPayload
{
    public required Guid DocumentId { get; init; }
}
