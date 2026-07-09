using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Commande orchestrant l'ingestion (parsing → chunking → embedding → stockage pgvector) d'un
/// document déjà créé en base via <see cref="UploadDocumentCommand"/>. Ne transporte que
/// l'identifiant : tout le reste (chemin du fichier, métadonnées, langue) est relu depuis la
/// ligne <c>Document</c> au moment de l'exécution — ce qui permet de rejouer exactement le même
/// traitement, que la commande soit envoyée juste après l'upload ou reprise après un redémarrage.
/// </summary>
public sealed record IngestDocumentCommand : IRequest<IngestDocumentResponse>
{
    /// <summary>Identifiant du document déjà créé en base de données.</summary>
    public required Guid DocumentId { get; init; }
}
