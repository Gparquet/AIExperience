using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Service d'ingestion de documents dans le pipeline RAG.
/// Orchestre le parsing, le chunking et la vectorisation d'un document
/// pour le rendre interrogeable via pgvector.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Ingère un document : parsing → chunking → embedding → stockage dans pgvector.
    /// </summary>
    /// <param name="filePath">Flux du fichier à ingérer.</param>
    /// <param name="documentId">Identifiant du document en base de données.</param>
    /// <param name="metadata">Métadonnées du document (titre, auteur, nb pages...).</param>
    /// <param name="strategy">Stratégie de chunking à appliquer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task IngestAsync(
        string filePath,
        Guid documentId,
        DocumentMetadata metadata,
        ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        CancellationToken ct = default);

    /// <summary>
    /// Supprime tous les chunks et vecteurs associés à un document de pgvector.
    /// </summary>
    /// <param name="documentId">Identifiant du document à supprimer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task DeleteAsync(Guid documentId, CancellationToken ct = default);
}
