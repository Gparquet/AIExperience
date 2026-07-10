using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Signale l'avancement fin du pipeline d'ingestion. Deux responsabilités combinées derrière une
/// seule abstraction pour que le pipeline n'ait qu'un interlocuteur : persister l'avancement (pour
/// qu'un rechargement de page le retrouve) et le pousser en temps réel au front. Best-effort comme
/// <see cref="IIngestionNotifier"/> : un incident de report ne doit jamais faire échouer l'ingestion.
/// </summary>
public interface IIngestionProgressReporter
{
    /// <summary>
    /// Signale l'entrée dans une nouvelle étape. Toujours persisté immédiatement (une transition
    /// d'étape est un jalon rare et important) et notifié.
    /// </summary>
    Task EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default);

    /// <summary>
    /// Signale l'avancement fin de l'étape courante. Toujours notifié en temps réel ; persisté
    /// seulement selon la politique de throttling de l'implémentation (pour ne pas marteler la base).
    /// </summary>
    /// <param name="percent">Pourcentage de l'étape (0-100), ou <c>null</c> si indéterminé.</param>
    /// <param name="counters">Compteurs de détail de l'étape.</param>
    Task ReportAsync(Guid documentId, IngestionStage stage, int? percent,
        IngestionProgressCounters counters, CancellationToken ct = default);

    /// <summary>
    /// Efface l'avancement persisté (remet la colonne à <c>null</c>). Appelé en fin de traitement
    /// réussi : les statistiques finales du document suffisent alors, l'avancement n'a plus d'objet.
    /// </summary>
    Task ClearAsync(Guid documentId, CancellationToken ct = default);
}
