using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Handler MediatR pour <see cref="IngestDocumentCommand"/>. Exécute le pipeline d'ingestion sur un
/// document déjà créé, puis persiste son statut final (Completed ou Failed). Un échec technique ne
/// doit jamais fuiter de détail interne au client : seul un message générique est stocké/exposé,
/// le détail complet (stack trace incluse) est loggé côté serveur.
/// </summary>
public sealed class IngestDocumentHandler(
    IIngestionService ingestionService,
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ILogger<IngestDocumentHandler> logger) : IRequestHandler<IngestDocumentCommand, IngestDocumentResponse>
{
    public async Task<IngestDocumentResponse> Handle(IngestDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document introuvable après création : {request.DocumentId}");

        try
        {
            await ingestionService.IngestAsync(request.FilePath, request.DocumentId, request.DocumentMetadata, ct: cancellationToken);
            document.MarkAsCompleted();
        }
        catch (OperationCanceledException)
        {
            // Annulation explicite (client déconnecté, timeout) : ce n'est pas un échec d'ingestion,
            // on laisse l'exception se propager au lieu de marquer le document en erreur.
            throw;
        }
        catch (Exception ex)
        {
            // Trace technique complète (stack trace) côté serveur uniquement, pour le diagnostic.
            logger.LogError(ex, "Échec de l'ingestion du document {DocumentId} ({FilePath})",
                request.DocumentId, request.FilePath);

            // Message générique exposé via l'API : évite de fuiter des détails internes
            // (chemins de fichiers temporaires, message brut Npgsql, etc.) au client.
            document.MarkAsFailed("L'ingestion du document a échoué. Consultez les journaux serveur pour le détail.");
        }

        await documentRepository.UpdateAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new IngestDocumentResponse
        {
            DocumentId = document.Id,
            Status = document.Status,
            ErrorMessage = document.ErrorMessage
        };
    }
}
