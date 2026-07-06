using AIExperience.Rag.Domain.Enums;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Commande orchestrant l'ingestion (parsing → chunking → embedding → stockage pgvector) d'un document
/// déjà créé en base via <see cref="UploadDocumentCommand"/>, ainsi que la mise à jour de son statut final.
/// </summary>
public sealed record IngestDocumentCommand : IRequest<IngestDocumentResponse>
{
    /// <summary>Identifiant du document déjà créé en base de données.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Chemin du fichier temporaire à ingérer.</summary>
    public required string FilePath { get; init; }

    /// <summary>Métadonnées du document, transmises au pipeline d'ingestion.</summary>
    public required DocumentMetadata DocumentMetadata { get; init; }
}
