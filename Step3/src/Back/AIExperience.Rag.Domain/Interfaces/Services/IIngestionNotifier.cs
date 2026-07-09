namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Pousse les changements de statut d'ingestion vers les clients connectés (front-end), pour que
/// l'utilisateur soit prévenu sans avoir à recharger la page. Best-effort par nature : un échec de
/// notification ne doit jamais faire échouer le traitement d'ingestion lui-même — le statut en
/// base reste la source de vérité, la notification n'est qu'un accélérateur d'affichage.
/// </summary>
public interface IIngestionNotifier
{
    /// <summary>Signale qu'un document vient de changer de statut.</summary>
    /// <param name="documentId">Identifiant du document concerné.</param>
    /// <param name="status">Nouveau statut (ex. "Processing", "Completed", "Failed").</param>
    /// <param name="errorMessage">Message d'erreur si le traitement a échoué, sinon <c>null</c>.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default);
}
