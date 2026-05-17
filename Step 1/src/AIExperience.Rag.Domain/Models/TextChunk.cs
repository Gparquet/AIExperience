namespace AIExperience.Rag.Domain.Models;

// <summary>
/// Représente un fragment de texte extrait lors du découpage (chunking) d'un document.
/// </summary>
public sealed record TextChunk
{
    /// <summary>Contenu textuel du chunk.</summary>
    public required string Content { get; init; }

    /// <summary>Numéro de page source (si disponible).</summary>
    public int? PageNumber { get; init; }

    /// <summary>Titre de la section source (si disponible).</summary>
    public string? SectionTitle { get; init; }
}
