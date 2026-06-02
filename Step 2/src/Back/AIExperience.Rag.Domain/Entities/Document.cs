using AIExperience.Rag.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente un document uploadé par un utilisateur dans le système RAG.
/// Contient les métadonnées du fichier ainsi que son statut d'ingestion.
/// </summary>
public class Document
{
    /// <summary>Identifiant unique du document.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Nom du fichier original (ex: GBCP_2024.pdf).</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Type MIME du fichier (ex: application/pdf).</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>Taille du fichier en octets.</summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>Identifiant de l'utilisateur propriétaire du document.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Statut actuel du pipeline d'ingestion.</summary>
    public IngestionStatus Status { get; private set; } = IngestionStatus.Pending;

    /// <summary>Stratégie de chunking utilisée lors de l'ingestion.</summary>
    public ChunkingStrategy ChunkingStrategy { get; private set; } = ChunkingStrategy.Recursive;

    /// <summary>Métadonnées enrichies du document (titre, auteur, nb pages...).</summary>
    public DocumentMetadata Metadata { get; private set; } = DocumentMetadata.Empty;

    /// <summary>Référence au fichier physique dans le stockage (chemin ou clé blob).</summary>
    public string? FileReference { get; private set; }

    /// <summary>Message d'erreur en cas d'échec de l'ingestion.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Date et heure de création du document (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Date et heure de la dernière mise à jour (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Collection des chunks vectorisés générés lors de l'ingestion.</summary>
    public ICollection<DocumentChunk> Chunks { get; private set; } = [];

    private Document() { }

    /// <summary>
    /// Crée une nouvelle instance de <see cref="Document"/>.
    /// </summary>
    /// <param name="fileName">Nom du fichier original.</param>
    /// <param name="contentType">Type MIME du fichier.</param>
    /// <param name="fileSizeBytes">Taille du fichier en octets.</param>
    /// <param name="userId">Identifiant de l'utilisateur propriétaire.</param>
    /// <param name="metadata">Métadonnées enrichies du document.</param>
    /// <param name="chunkingStrategy">Stratégie de découpage à appliquer lors de l'ingestion.</param>
    public static Document Create(
        string fileName,
        string contentType,
        long fileSizeBytes,
        string userId,
        DocumentMetadata metadata,
        ChunkingStrategy chunkingStrategy = ChunkingStrategy.Recursive)
    {
        return new Document
        {
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            UserId = userId,
            Metadata = metadata,
            ChunkingStrategy = chunkingStrategy
        };
    }

    /// <summary>
    /// Définit la référence de stockage physique du fichier.
    /// </summary>
    /// <param name="fileReference">Chemin ou clé blob du fichier stocké.</param>
    public void SetFileReference(string fileReference)
    {
        FileReference = fileReference;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Passe le statut du document à <see cref="IngestionStatus.Processing"/>.
    /// </summary>
    public void MarkAsProcessing()
    {
        Status = IngestionStatus.Processing;
        ErrorMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Passe le statut du document à <see cref="IngestionStatus.Completed"/>.
    /// </summary>
    public void MarkAsCompleted()
    {
        Status = IngestionStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Passe le statut du document à <see cref="IngestionStatus.Failed"/> et enregistre le message d'erreur.
    /// </summary>
    /// <param name="errorMessage">Description de l'erreur survenue.</param>
    public void MarkAsFailed(string errorMessage)
    {
        Status = IngestionStatus.Failed;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Réinitialise le document à <see cref="IngestionStatus.Pending"/> pour une nouvelle tentative d'ingestion.
    /// </summary>
    public void ResetToPending()
    {
        Status = IngestionStatus.Pending;
        ErrorMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

}