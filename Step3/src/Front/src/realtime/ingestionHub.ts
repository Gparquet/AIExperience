import * as signalR from '@microsoft/signalr';

const BASE_URL = import.meta.env.VITE_API_URL ?? '';

/**
 * Construit la connexion au hub d'ingestion. La reconnexion automatique gère les coupures
 * transitoires ; en cas d'échec prolongé, c'est au contexte de notifications de basculer sur
 * le repli en polling (le hub ne fait ici aucune tentative infinie ni blocante).
 */
export function createIngestionHubConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${BASE_URL}/hubs/ingestion`)
    .withAutomaticReconnect()
    .build();
}
