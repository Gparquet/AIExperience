# ADR-001 — Streaming de la réponse RAG via Server-Sent Events

**Date :** 2026-06-01  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Le pipeline RAG effectue plusieurs opérations coûteuses avant de retourner une réponse :
stratégie, recherche vectorielle, compression du contexte, puis génération LLM.
La génération LLM est la plus longue (plusieurs secondes). Sans streaming, l'utilisateur
voit un écran figé avec une animation "typing", et reçoit le texte d'un seul bloc.

L'objectif est d'afficher les tokens LLM **au fur et à mesure** qu'ils sont générés,
comme le font ChatGPT, Claude ou Mistral.

---

## Décision

Implémenter le streaming via **Server-Sent Events (SSE) sur un endpoint POST**.

### Pourquoi SSE et non WebSocket ?

| Critère | SSE | WebSocket |
|---------|-----|-----------|
| Direction | Unidirectionnel (serveur → client) | Bidirectionnel |
| Protocole | HTTP natif | Upgrade HTTP |
| Complexité | Faible | Élevée |
| Reconnexion | Automatique (EventSource) | Manuelle |
| Besoin réel | ✅ Le client n'envoie rien pendant la réponse | ❌ Surdimensionné |

Le chat RAG n'a aucune communication serveur → client déclenchée par le serveur en dehors
d'une requête. SSE est le bon outil.

### Pourquoi POST et non GET (EventSource natif) ?

`EventSource` (l'API SSE native du navigateur) ne supporte que `GET` et ne permet pas
d'envoyer un body JSON. Or la question RAG, les filtres et la stratégie doivent être
transmis. On utilise donc `fetch` avec lecture du `ReadableStream` de la réponse.

### Pourquoi pas le long polling ?

Le long polling ouvre une connexion, attend une réponse complète, puis ré-ouvre une
connexion. Incompatible avec un affichage token par token.

---

## Architecture de la solution

### Couche Domain — nouveau modèle `RagStreamChunk`

```csharp
public sealed record RagStreamChunk
{
    public string? Token { get; init; }        // null si IsDone = true
    public bool IsDone { get; init; }
    public RagResponse? FinalResponse { get; init; } // peuplé uniquement quand IsDone
}
```

Ce modèle est le contrat entre l'Infrastructure (pipeline RAG) et le Web.Api
(transport HTTP). Il n'expose aucune dépendance HTTP.

### Couche Domain — interface `IRagPipelineService`

```csharp
IAsyncEnumerable<RagStreamChunk> AskStreamAsync(RagQuery query, CancellationToken ct = default);
```

L'interface reste dans le Domain. Le pipeline produit un `IAsyncEnumerable<RagStreamChunk>`
indépendamment du fait que le consommateur soit un controller HTTP, un test unitaire
ou une console.

### Couche Infrastructure — `RagPipelineService.AskStreamAsync`

Les étapes 1 à 4 (stratégie, récupération, compression, historique) sont identiques à
`AskAsync`. Seule l'étape 5 change :

```csharp
// Avant (bloquant)
var result = await chatClient.GetResponseAsync(messages, options, ct);

// Après (streaming)
await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, ct))
{
    if (!string.IsNullOrEmpty(update.Text))
        yield return new RagStreamChunk { Token = update.Text };
}
yield return new RagStreamChunk { IsDone = true, FinalResponse = finalResponse };
```

`IChatClient.GetStreamingResponseAsync` est fourni par `Microsoft.Extensions.AI` et
supporte tous les providers configurés (OpenAI, LM Studio, Ollama, Azure).

### Couche Web.Api — endpoint SSE

```
POST /api/chat/stream
Content-Type: application/json
Body: { "question": "...", "documentIds": [], "strategy": "Adaptive" }

← HTTP 200
   Content-Type: text/event-stream; charset=utf-8

event: token
data: {"token":"Bonjour"}

event: token
data: {"token":" voici"}

event: done
data: {"answer":"...","citations":[...],"strategyUsed":"Direct","durationMs":1234}
```

#### Deux points critiques côté serveur

1. **`IHttpResponseBodyFeature.DisableBuffering()`** — à appeler avant d'écrire la
   réponse. Sans ça, ASP.NET Core peut buffuriser plusieurs tokens avant de les envoyer,
   rendant le streaming invisible pour le client.

2. **`Response.Body.FlushAsync(ct)`** — après chaque `Response.WriteAsync`. Force Kestrel
   à envoyer le chunk immédiatement au socket TCP.

### Couche Front — parsing SSE avec `fetch` + `ReadableStream`

`EventSource` natif ne supporte pas POST. On utilise `fetch` et on lit le corps
en streaming :

```typescript
async *askStream(payload): AsyncGenerator<StreamEvent> {
  const res = await fetch('/api/chat/stream', { method: 'POST', body: JSON.stringify(payload) });
  const reader = res.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const parts = buffer.split('\n\n');  // blocs SSE séparés par double newline
    buffer = parts.pop() ?? '';          // garder le bloc incomplet en buffer

    for (const block of parts) {
      // parse event/data et yield StreamEvent
    }
  }
}
```

Le buffer est indispensable : un `reader.read()` peut retourner des octets qui coupent
un bloc SSE en plein milieu.

#### Point critique côté client — `flushSync`

React 18 introduit le **batching automatique** : plusieurs `setState` successifs dans
le même contexte async sont regroupés en un seul rendu pour des raisons de performance.

Si plusieurs tokens arrivent dans le même chunk réseau (`reader.read()` retourne plusieurs
blocs SSE), tous les `setStreamingContent` seront batchés et le texte n'apparaîtra qu'à
la fin du chunk — pas en live.

**Solution :** `flushSync` de `react-dom` force React à vider la file de rendu
synchroniquement après chaque token.

```tsx
import { flushSync } from 'react-dom';

flushSync(() => {
  setStreamingContent(streamingRef.current);
});
```

Un `useRef` (`streamingRef`) accumule le texte sans déclencher de rendu, ce qui évite
un effet stale closure tout en gardant `flushSync` minimal.

---

## Conséquences

### Positives

- L'utilisateur voit les tokens apparaître mot par mot, sans attendre la fin de la génération.
- Les étapes coûteuses (vectorisation, recherche, compression) restent invisibles dans
  la latence perçue car le streaming commence dès le premier token.
- Le pipeline `IAsyncEnumerable` est testable indépendamment du transport HTTP.
- Compatible avec tous les providers AI (OpenAI, LM Studio, Ollama, Azure).

### Négatives / points d'attention

- `flushSync` force des rendus synchrones, ce qui peut peser sur les performances si
  les tokens arrivent très vite (> 200/s). À surveiller si le modèle est très rapide.
- L'endpoint `/stream` retourne `Task` (pas `IActionResult`) — il n'est pas visible dans
  la documentation Scalar/OpenAPI sans configuration supplémentaire.
- Si le client se déconnecte en cours de génération, le `CancellationToken` ASP.NET Core
  annule le `foreach` et la génération LLM s'arrête proprement (via `OperationCanceledException`).

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| WebSocket (SignalR) | Bidirectionnel, surcharge inutile pour un use-case unidirectionnel. |
| EventSource GET natif | Pas de body JSON possible, incompatible avec la structure de la requête RAG. |
| Long polling | Impossible d'afficher token par token ; chaque poll attend une réponse complète. |
| gRPC streaming | Requiert des changements majeurs d'infrastructure ; HTTP/2 obligatoire. |
| Chunked JSON (`ndjson`) | Parsing plus complexe ; SSE est un standard conçu pour ce cas. |
