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

    /// <summary>Nombre de dimensions du vecteur d'embedding.</summary>
    public int EmbeddingDimensions { get; private set; }

    /// <summary>Date et heure de création du chunk (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Position de début dans la vidéo source (null pour les documents non-vidéo).
    /// Persisté en base comme <c>start_time_seconds</c> (double precision).
    /// </summary>
    public TimeSpan? StartTime { get; private set; }

    /// <summary>
    /// Position de fin dans la vidéo source (null pour les documents non-vidéo).
    /// Persisté en base comme <c>end_time_seconds</c> (double precision).
    /// </summary>
    public TimeSpan? EndTime { get; private set; }

    /// <summary>
    /// Nom du document source — non persisté, renseigné à la volée depuis le JOIN documents.
    /// </summary>
    [NotMapped]
    public string? DocumentName { get; private set; }

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
    /// <param name="pageNumber">Numéro de page source (optionnel, PDF).</param>
    /// <param name="sectionTitle">Titre de la section source (optionnel).</param>
    /// <param name="documentName">Nom du document source, issu du JOIN SQL (optionnel, non persisté).</param>
    /// <param name="startTime">Début du chunk dans la vidéo (optionnel, vidéo uniquement).</param>
    /// <param name="endTime">Fin du chunk dans la vidéo (optionnel, vidéo uniquement).</param>
    public static DocumentChunk Create(
        Guid documentId,
        string content,
        int chunkIndex,
        int embeddingDimensions,
        int? pageNumber = null,
        string? sectionTitle = null,
        string? documentName = null,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        return new DocumentChunk
        {
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex,
            EmbeddingDimensions = embeddingDimensions,
            PageNumber = pageNumber,
            SectionTitle = sectionTitle,
            DocumentName = documentName,
            StartTime = startTime,
            EndTime = endTime
        };
    }
}
