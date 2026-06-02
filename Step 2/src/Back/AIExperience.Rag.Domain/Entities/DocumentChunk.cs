using System.ComponentModel.DataAnnotations.Schema;

namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente un fragment (chunk) d'un document, issu du découpage lors de l'ingestion.
/// Chaque chunk est vectorisé et stocké dans pgvector pour la recherche sémantique.
/// </summary>
public class DocumentChunk
{
    /// <summary>Identifiant unique du chunk.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Identifiant du document parent.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Contenu textuel brut du chunk.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Position ordinale du chunk dans le document (0-based).</summary>
    public int ChunkIndex { get; private set; }

    /// <summary>Numéro de page du document source où se trouve ce chunk.</summary>
    public int? PageNumber { get; private set; }

    /// <summary>Titre de la section ou du chapitre contenant ce chunk.</summary>
    public string? SectionTitle { get; private set; }

    /// <summary>Nombre de dimensions du vecteur d'embedding (ex: 3072 pour text-embedding-3-large).</summary>
    public int EmbeddingDimensions { get; private set; }

    /// <summary>Date et heure de création du chunk (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation vers le document parent.</summary>
    public Document Document { get; private set; } = null!;

    private DocumentChunk() { }

    /// <summary>
    /// Crée un nouveau chunk de document.
    /// </summary>
    /// <param name="documentId">Identifiant du document parent.</param>
    /// <param name="content">Contenu textuel du chunk.</param>
    /// <param name="chunkIndex">Position ordinale dans le document.</param>
    /// <param name="embeddingDimensions">Nombre de dimensions du vecteur d'embedding.</param>
    /// <param name="pageNumber">Numéro de page source (optionnel).</param>
    /// <param name="sectionTitle">Titre de la section source (optionnel).</param>
    public static DocumentChunk Create(
        Guid documentId,
        string content,
        int chunkIndex,
        int embeddingDimensions,
        int? pageNumber = null,
        string? sectionTitle = null)
    {
        return new DocumentChunk
        {
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex,
            EmbeddingDimensions = embeddingDimensions,
            PageNumber = pageNumber,
            SectionTitle = sectionTitle
        };
    }
}
