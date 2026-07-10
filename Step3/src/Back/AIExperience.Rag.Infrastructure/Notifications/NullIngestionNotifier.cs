using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Infrastructure.Notifications;

/// <summary>
/// Notifieur par défaut, sans effet, utilisé par les composition roots qui n'exposent pas de canal
/// temps réel (ex. l'application console) — évite qu'un tel host échoue à résoudre
/// <see cref="IIngestionNotifier"/> alors qu'aucune UI n'écoute de toute façon. Le Web.Api enregistre
/// sa propre implémentation SignalR, qui prend le pas sur celle-ci (voir <c>TryAddSingleton</c> côté DI).
/// </summary>
public sealed class NullIngestionNotifier : IIngestionNotifier
{
    public Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default)
        => Task.CompletedTask;
}
