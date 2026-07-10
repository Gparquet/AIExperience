# ADR-003 — Notification de fin d'ingestion en temps réel via SignalR + repli en polling

**Date :** 2026-07-08  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Depuis [ADR-002](002-ingestion-asynchrone-outbox.md), l'upload répond `202 Accepted` avec un
statut `Pending` : le traitement réel (extraction, chunking, embedding, ou transcription vidéo)
se poursuit en arrière-plan et peut durer de quelques secondes à plusieurs minutes.

Le front doit donc apprendre **après coup** que le document est passé `Completed` (ou `Failed`)
pour rafraîchir sa liste, débloquer le bouton « Interroger dans le Chat », ou afficher l'erreur.

La question est : **comment le serveur informe-t-il le client d'un changement de statut qu'il
n'a pas lui-même déclenché par une requête ?**

---

## Décision

Pousser les changements de statut au client via **SignalR** (WebSocket avec fallbacks
intégrés), avec un **repli en polling** côté front si la connexion temps réel n'est pas
disponible.

### Pourquoi SignalR ici, alors que l'[ADR-001](001-streaming-rag-reponse.md) a rejeté SignalR au profit de SSE ?

Ce n'est **pas** contradictoire — les deux besoins sont différents :

| | Streaming RAG (ADR-001) | Notification d'ingestion (cet ADR) |
|---|-------------------------|------------------------------------|
| Déclencheur | Une requête client en cours | Un **worker serveur**, hors de toute requête |
| Cible | **La** requête qui a posé la question | **Tous** les onglets/clients intéressés |
| Durée de vie | Le temps d'une réponse | **Permanente** (connexion de fond) |
| Outil adapté | SSE sur la réponse POST | Connexion persistante multiplexée |

Le streaming RAG s'accroche au corps d'une réponse HTTP existante : SSE suffit. La notification
d'ingestion, elle, est un événement **serveur → clients** émis en dehors de tout cycle
requête/réponse, potentiellement vers plusieurs clients. C'est le cas d'usage canonique d'une
connexion persistante, et SignalR gère nativement la connexion, la reconnexion et le broadcast.

---

## Architecture de la solution

### Couche Domain — interface `IIngestionNotifier`

Le worker d'ingestion (couche Application) ne connaît **pas** SignalR. Il dépend d'une
abstraction du Domain :

```csharp
Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default);
```

L'implémentation SignalR vit dans `Web.Api` (racine de composition), respectant la règle
« une couche interne ne connaît jamais une couche externe ».

### Couche Web.Api — hub descendant et notifier

Le hub est **purement descendant** : le client s'y abonne mais n'appelle aucune méthode dessus.

```csharp
/// Hub purement descendant : le front écoute "documentStatusChanged", n'appelle rien.
public sealed class IngestionHub : Hub;
```

```csharp
public sealed class SignalRIngestionNotifier(IHubContext<IngestionHub> hubContext) : IIngestionNotifier
{
    public async Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        => await hubContext.Clients.All.SendAsync("documentStatusChanged",
            new { documentId, status, errorMessage }, ct);
}
```

Diffusion à **`Clients.All`** volontairement : en l'absence d'authentification
(`UserId` hardcodé en dev), il n'y a pas encore de segmentation par utilisateur. À revoir dès
qu'une authentification existera (`Clients.User(...)` ou groupes).

Endpoint du hub : `/hubs/ingestion`.

### Couche Front — connexion + repli

```typescript
export function createIngestionHubConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${BASE_URL}/hubs/ingestion`)
    .withAutomaticReconnect()   // gère les coupures transitoires
    .build();
}
```

Le contexte de notifications du front écoute `documentStatusChanged` et met à jour la liste des
documents en direct. **`withAutomaticReconnect()`** absorbe les micro-coupures ; en cas d'échec
prolongé de la connexion temps réel, le front **bascule sur un polling** périodique
(`GET /api/documents`) comme filet de sécurité — l'information finit toujours par arriver, au
prix d'un léger délai.

---

## Conséquences

### Positives

- Mise à jour **quasi instantanée** de l'UI dès qu'un document change de statut, sans que
  l'utilisateur rafraîchisse.
- Le worker reste découplé du transport (dépend de `IIngestionNotifier`, pas de SignalR).
- Le repli en polling garantit la **robustesse** : si le WebSocket tombe, l'app reste correcte.
- La reconnexion automatique masque les coupures réseau transitoires.

### Négatives / points d'attention

- **Broadcast à tous les clients** : tant qu'il n'y a pas d'authentification, chaque client
  reçoit les événements de tous. Dette assumée, à corriger avec l'auth.
- Dépendance front supplémentaire (`@microsoft/signalr`).
- Deux chemins de mise à jour (SignalR + polling) à garder cohérents : le polling doit rester
  idempotent vis-à-vis de l'état déjà reçu par SignalR.
- CORS : le hub doit être inclus dans la politique CORS (origines front autorisées).

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| Polling seul (`GET /api/documents` en boucle) | Latence + charge inutiles ; conservé uniquement comme **repli**, pas comme mécanisme principal. |
| SSE (comme ADR-001) | Adapté à un flux attaché à une requête, moins naturel pour une connexion de fond persistante multi-clients avec reconnexion. |
| WebSocket brut | Réimplémenter reconnexion, fallback transport et sérialisation que SignalR fournit déjà. |
| Long polling manuel | Complexité de gestion des connexions pour un résultat inférieur à SignalR. |
