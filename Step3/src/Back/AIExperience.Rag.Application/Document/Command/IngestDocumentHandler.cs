using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Handler MediatR pour <see cref="IngestDocumentCommand"/>, invoqué par le worker d'ingestion en
/// arrière-plan. Exécute le pipeline d'ingestion sur un document déjà créé, en passant par le
/// statut "en cours de traitement" avant de s'y attaquer — pour qu'un client qui interroge le
/// document pendant ce temps voie un état cohérent plutôt qu'un statut figé sur "en attente". Un
/// échec technique ne doit jamais fuiter de détail interne au client : seul un message générique
/// est stocké/exposé, le détail complet (stack trace incluse) est loggé côté serveur.
/// </summary>
public sealed class IngestDocumentHandler(
    IIngestionService ingestionService,
    IDocumentRepository documentRepository,
    DocumentIngestionStatusUpdater statusUpdater,
    IIngestionProgressReporter progressReporter,
    ILogger<IngestDocumentHandler> logger) : IRequestHandler<IngestDocumentCommand, IngestDocumentResponse>
{
    /// <summary>Extensions vidéo/audio routées vers le pipeline segments plutôt que vers l'extraction texte générique.</summary>
    private static readonly string[] VideoOrAudioExtensions =
        [".mp4", ".mkv", ".webm", ".avi", ".mov", ".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    public async Task<IngestDocumentResponse> Handle(IngestDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document introuvable après création : {request.DocumentId}");

        var filePath = document.FileReference
            ?? throw new InvalidOperationException($"Document sans fichier de travail référencé : {document.Id}");

        await statusUpdater.MarkProcessingAsync(document, cancellationToken);

        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (VideoOrAudioExtensions.Contains(extension))
            {
                // Un seul pipeline vidéo/audio, identique quel que soit le point d'upload d'origine.
                await ingestionService.IngestVideoOrAudioAsync(
                    filePath, document.Id, document.Metadata, document.Metadata.Language, cancellationToken);
            }
            else
            {
                await ingestionService.IngestAsync(filePath, document.Id, document.Metadata, ct: cancellationToken);
            }

            await statusUpdater.MarkCompletedAsync(document, cancellationToken);
            await progressReporter.ClearAsync(document.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Annulation explicite (arrêt du worker, timeout) : ce n'est pas un échec d'ingestion,
            // on laisse l'exception se propager sans marquer le document en erreur ni supprimer le
            // fichier de travail — il sera repris au prochain tour de sonde puisque le message
            // outbox correspondant reste non traité.
            throw;
        }
        catch (Exception ex)
        {
            // Trace technique complète (stack trace) côté serveur uniquement, pour le diagnostic.
            logger.LogError(ex, "Échec de l'ingestion du document {DocumentId} ({FilePath})",
                document.Id, filePath);

            // Message générique exposé via l'API : évite de fuiter des détails internes
            // (chemins de fichiers temporaires, message brut Npgsql, etc.) au client.
            await statusUpdater.MarkFailedAsync(document,
                "L'ingestion du document a échoué. Consultez les journaux serveur pour le détail.", cancellationToken);
        }

        // Le fichier de travail n'a plus lieu d'être une fois le document arrivé dans un état
        // final (succès ou échec) — il ne sert qu'à alimenter une tentative d'ingestion.
        if (File.Exists(filePath))
            File.Delete(filePath);

        return new IngestDocumentResponse
        {
            DocumentId = document.Id,
            Status = document.Status,
            ErrorMessage = document.ErrorMessage
        };
    }
}
