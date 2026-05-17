using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Handler MediatR pour la commande <see cref="UploadDocumentCommand"/>.
/// Persiste le document et son <see cref="OutboxMessage"/> dans la même transaction (Outbox Pattern),
/// garantissant qu'aucun événement ne sera perdu même en cas de défaillance réseau après le commit.
/// </summary>
public sealed class UploadDocumentHandler(
    IDocumentRepository documentRepository, IUnitOfWork unitOfWork) : IRequestHandler<UploadDocumentCommand, UploadDocumentResponse>
{

    /// <summary>
    /// Exécute la création du document et enregistre l'événement dans l'Outbox de façon atomique.
    /// L'<c>OutboxWorker</c> (BackgroundService) se chargera de publier l'événement via MediatR.
    /// </summary>
    /// <param name="request">Commande contenant les données du fichier uploadé.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Réponse contenant l'identifiant et le statut initial du document.</returns>
    public async Task<UploadDocumentResponse> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = Domain.Entities.Document.Create(
          request.FileName,
          request.ContentType,
          request.FileSizeBytes,
          request.UserId,
          request.DocumentMetadata,
          request.ChunkingStrategy);

        await documentRepository.AddAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UploadDocumentResponse
        {
            DocumentId = document.Id,
            FileName = document.FileName,
            Status = IngestionStatus.Completed, // TODO: mettre pending quand je metterais un background service pour traiter les documents 
            CreatedAt = document.CreatedAt
        };
    }
}
