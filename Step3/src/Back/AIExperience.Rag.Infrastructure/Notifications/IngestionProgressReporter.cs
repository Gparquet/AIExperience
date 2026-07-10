using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AIExperience.Rag.Infrastructure.Notifications;

/// <summary>
/// Reporter d'avancement : pousse chaque tick au front en temps réel et persiste l'avancement en
/// base de façon throttlée (une transition d'étape est toujours écrite ; les ticks intermédiaires
/// ne le sont qu'au-delà d'un intervalle minimal). L'écriture est <b>ciblée</b> (UPDATE de la seule
/// colonne <c>ingestion_progress</c>, hors tracking EF) pour ne jamais écraser un changement de
/// statut fait en parallèle par le worker. Singleton : conserve en mémoire l'horodatage de la
/// dernière persistance par document (le worker traite les documents un par un, pas de contention).
/// </summary>
public class IngestionProgressReporter(
    IServiceScopeFactory scopeFactory,
    IIngestionNotifier notifier,
    IOptions<IngestionOptions> options,
    ILogger<IngestionProgressReporter> logger) : IIngestionProgressReporter
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastPersist = new();
    private TimeSpan Throttle => TimeSpan.FromSeconds(options.Value.ProgressPersistThrottleSeconds);

    /// <inheritdoc/>
    public async Task EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default)
    {
        var progress = IngestionProgress.Create(stage);
        // Une transition d'étape est un jalon important : toujours persistée.
        await PersistAndTrackAsync(documentId, progress, ct);
        await SafeNotifyAsync(documentId, progress, ct);
    }

    /// <inheritdoc/>
    public async Task ReportAsync(Guid documentId, IngestionStage stage, int? percent,
        IngestionProgressCounters counters, CancellationToken ct = default)
    {
        var progress = IngestionProgress.Create(stage, percent, counters);

        // Persistance throttlée : on n'écrit que si l'intervalle minimal est écoulé depuis la dernière.
        var last = _lastPersist.TryGetValue(documentId, out var t) ? t : DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - last >= Throttle)
            await PersistAndTrackAsync(documentId, progress, ct);

        // Le temps réel, lui, n'est jamais throttlé : chaque tick est poussé.
        await SafeNotifyAsync(documentId, progress, ct);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(Guid documentId, CancellationToken ct = default)
    {
        _lastPersist.TryRemove(documentId, out _);
        await PersistAsync(documentId, null, ct);
    }

    // Persiste puis mémorise l'instant de persistance (pour le throttling).
    private async Task PersistAndTrackAsync(Guid documentId, IngestionProgress progress, CancellationToken ct)
    {
        await PersistAsync(documentId, progress, ct);
        _lastPersist[documentId] = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Écrit la seule colonne <c>ingestion_progress</c> via un UPDATE ciblé, dans un scope dédié.
    /// Surchargeable pour les tests (évite le besoin d'une base réelle).
    /// </summary>
    protected virtual async Task PersistAsync(Guid documentId, IngestionProgress? progress, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Documents
                .Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IngestionProgress, progress), ct);
        }
        catch (Exception ex)
        {
            // Best-effort : un échec de persistance de l'avancement ne doit jamais interrompre
            // l'ingestion elle-même (le statut en base reste la source de vérité).
            logger.LogWarning(ex, "Échec de persistance de l'avancement du document {DocumentId}.", documentId);
        }
    }

    private async Task SafeNotifyAsync(Guid documentId, IngestionProgress progress, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyProgressAsync(documentId, progress, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Échec de notification d'avancement du document {DocumentId}.", documentId);
        }
    }
}
