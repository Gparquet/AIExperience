using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Web.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AIExperience.Web.Api.Notifications;

/// <summary>
/// Diffuse les changements de statut d'ingestion à tous les clients connectés au hub. Volontairement
/// simple (pas de segmentation par utilisateur) tant qu'il n'y a pas d'authentification — à revoir
/// quand elle arrivera.
/// </summary>
public sealed class SignalRIngestionNotifier(IHubContext<IngestionHub> hubContext) : IIngestionNotifier
{
    public async Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        => await hubContext.Clients.All.SendAsync("documentStatusChanged",
            new { documentId, status, errorMessage }, ct);
}
