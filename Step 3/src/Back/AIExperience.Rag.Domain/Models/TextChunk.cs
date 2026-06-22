namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Représente un fragment de texte extrait lors du découpage (chunking) d'un document.
/// </summary>
public sealed record TextChunk
{
    /// <summary>Contenu textuel du chunk.</summary>
    public required string Content { get; init; }

    /// <summary>Numéro de page source (si disponible, pour les PDF).</summary>
    public int? PageNumber { get; init; }

    /// <summary>Titre de la section source (si disponible).</summary>
    public string? SectionTitle { get; init; }

    /// <summary>Début du chunk dans la vidéo source (null pour les documents non-vidéo).</summary>
    public TimeSpan? StartTime { get; init; }

    /// <summary>Fin du chunk dans la vidéo source (null pour les documents non-vidéo).</summary>
    public TimeSpan? EndTime { get; init; }
}
