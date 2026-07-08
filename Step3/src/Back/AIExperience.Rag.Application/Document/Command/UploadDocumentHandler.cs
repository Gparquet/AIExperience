using AIExperience.Rag.Application.Document.Exceptions;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Handler MediatR pour la commande <see cref="UploadDocumentCommand"/>.
/// Compare le fichier uploadé au document le plus récent portant le même nom : bloque la création
/// si aucun remplacement n'a été confirmé (doublon exact ou nouvelle version détectée), ou supprime
/// l'ancien document et crée le nouveau de façon atomique (une seule transaction) si confirmé.
/// </summary>
public sealed class UploadDocumentHandler(
    IDocumentRepository documentRepository, IUnitOfWork unitOfWork, IFileHashService fileHashService) : IRequestHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    /// <summary>
    /// Calcule le hash du fichier puis exécute la détection de doublon/nouvelle version et la création
    /// (ou le remplacement) du document.
    /// </summary>
    /// <param name="request">Commande contenant les données du fichier uploadé, son chemin et un éventuel ReplaceDocumentId.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Réponse contenant l'identifiant et le statut initial du document.</returns>
    /// <exception cref="DuplicateDocumentException">Un document de même nom existe déjà et n'a pas été confirmé comme remplaçable.</exception>
    public async Task<UploadDocumentResponse> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var contentHash = await fileHashService.ComputeSha256Async(request.FilePath, cancellationToken);

        var latestSameName = await documentRepository.GetLatestByFileNameAsync(
            request.UserId, request.FileName, cancellationToken);

        if (latestSameName is not null && latestSameName.Id != request.ReplaceDocumentId)
        {
            var matchType = latestSameName.ContentHash == contentHash
                ? DocumentMatchType.ExactDuplicate
                : DocumentMatchType.SameNameDifferentContent;

            throw new DuplicateDocumentException(
                latestSameName.Id, latestSameName.FileName, latestSameName.CreatedAt, matchType);
        }

        if (latestSameName is not null)
        {
            // Remplacement confirmé : suppression de l'ancien document avant création du nouveau,
            // le tout validé par un seul SaveChangesAsync ci-dessous (une seule transaction atomique).
            await documentRepository.DeleteAsync(latestSameName.Id, cancellationToken);
        }

        var document = Domain.Entities.Document.Create(
          request.FileName,
          request.ContentType,
          request.FileSizeBytes,
          request.UserId,
          request.DocumentMetadata,
          request.ChunkingStrategy,
          contentHash);

        await documentRepository.AddAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UploadDocumentResponse
        {
            DocumentId = document.Id,
            FileName = document.FileName,
            Status = document.Status,
            CreatedAt = document.CreatedAt
        };
    }
}
