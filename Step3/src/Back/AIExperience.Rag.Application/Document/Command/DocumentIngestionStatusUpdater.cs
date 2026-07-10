using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Regroupe en un seul endroit la séquence "changer le statut d'un document → le persister →
/// prévenir le front" — utilisée aussi bien par le pipeline d'ingestion de documents que par celui
/// de transcription vidéo, pour que les deux gardent toujours la base et la notification temps
/// réel synchronisées de la même façon.
/// </summary>
public sealed class DocumentIngestionStatusUpdater(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    IIngestionNotifier notifier)
{
    /// <summary>Marque le document comme en cours de traitement.</summary>
    public Task MarkProcessingAsync(Domain.Entities.Document document, CancellationToken ct = default)
    {
        document.MarkAsProcessing();
        return PersistAndNotifyAsync(document, ct);
    }

    /// <summary>Marque le document comme traité avec succès.</summary>
    public Task MarkCompletedAsync(Domain.Entities.Document document, CancellationToken ct = default)
    {
        document.MarkAsCompleted();
        return PersistAndNotifyAsync(document, ct);
    }

    /// <summary>Marque le document en échec, avec un message d'erreur destiné à l'utilisateur.</summary>
    public Task MarkFailedAsync(Domain.Entities.Document document, string errorMessage, CancellationToken ct = default)
    {
        document.MarkAsFailed(errorMessage);
        return PersistAndNotifyAsync(document, ct);
    }

    private async Task PersistAndNotifyAsync(Domain.Entities.Document document, CancellationToken ct)
    {
        await documentRepository.UpdateAsync(document, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // La notification est un aparté best-effort : un client déconnecté ou une erreur de
        // transport ne doit jamais remettre en cause le fait que le statut est déjà en base.
        await notifier.NotifyStatusChangedAsync(document.Id, document.Status.ToString(), document.ErrorMessage, ct);
    }
}
