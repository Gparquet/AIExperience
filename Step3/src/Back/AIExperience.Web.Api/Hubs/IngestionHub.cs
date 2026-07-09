using Microsoft.AspNetCore.SignalR;

namespace AIExperience.Web.Api.Hubs;

/// <summary>
/// Hub purement descendant : le front s'y connecte pour écouter l'événement
/// "documentStatusChanged" poussé par le serveur, mais n'appelle aucune méthode dessus.
/// </summary>
public sealed class IngestionHub : Hub;
