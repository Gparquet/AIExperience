# Suppression de conversations — design

Date : 2026-07-23
Branche : `feat/lot3-historique`
Périmètre : `Step3/src/Back`, `Step3/src/Front`

## Contexte

La sidebar des conversations (`ChatSidebar.tsx`, ajoutée dans le lot historique) liste les
conversations de l'utilisateur mais n'offre aucun moyen de les supprimer. L'utilisateur veut :
1. supprimer une conversation individuelle (icône corbeille sur chaque item) ;
2. supprimer toutes les conversations d'un coup.

## Back-end

### Repository (`IConversationRepository` / `ConversationRepository`)

Deux nouvelles méthodes, delete physique (pas de soft-delete — aucune autre entité du projet n'en
utilise) :

```csharp
Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);
Task<int> DeleteAllSessionsAsync(string userId, CancellationToken ct = default);
```

- `DeleteSessionAsync` : `FindAsync` puis `Remove` si trouvée (même idiome que
  `DocumentRepository.DeleteAsync`). Retourne `false` si la session n'existe pas.
- `DeleteAllSessionsAsync` : charge les sessions de l'utilisateur (`Where(UserId ==)`),
  `RemoveRange`, retourne le nombre supprimé.
- Les tables `chat_messages` et `citations` ont déjà `ON DELETE CASCADE` vers `conversation_sessions`
  (`init.sql`) — un seul `DELETE` sur `conversation_sessions` suffit, pas besoin de charger les
  messages/citations avant suppression.

### CQRS (`AIExperience.Rag.Application/Conversation/Command/`)

Nouveau dossier `Conversation/Command/`, symétrique à `Document/Command/` :

- `DeleteSessionCommand : ICommand<bool>` — `SessionId`, `UserId`.
- `DeleteSessionHandler` — recharge la session via `GetSessionByIdAsync` (même vérification de
  propriété que l'endpoint `GetSession` existant : session absente OU `UserId` différent → `false`),
  puis appelle `DeleteSessionAsync` + `IUnitOfWork.SaveChangesAsync`.
- `DeleteAllSessionsCommand : ICommand<int>` — `UserId`.
- `DeleteAllSessionsHandler` — appelle `DeleteAllSessionsAsync` + `SaveChangesAsync`, retourne le
  nombre supprimé (non consommé par le front dans un premier temps, mais utile pour logs/tests).

### API (`ChatController`)

Ajoute `ICommandDispatcher dispatcher` au constructeur (suit le pattern de `DocumentsController`).

- `DELETE /api/chat/sessions/{id:guid}` → `NoContent()` si supprimée, `NotFound()` sinon.
- `DELETE /api/chat/sessions` → supprime toutes les sessions de l'utilisateur courant, `NoContent()`.

## Front-end

### `client.ts`

```ts
deleteSession: (id: string) => request<void>(`/api/chat/sessions/${id}`, { method: 'DELETE' }),
deleteAllSessions: () => request<void>('/api/chat/sessions', { method: 'DELETE' }),
```

### `ChatSidebar.tsx`

- Chaque item de la liste reçoit un bouton corbeille (🗑️) à droite du compteur de messages.
  Au clic (avec `stopPropagation` pour ne pas déclencher la navigation vers la conversation), ouvre
  une modale de confirmation.
- Sous la liste, un lien discret "Tout supprimer" — visible uniquement si `sessions.length > 0` —
  ouvre une modale de confirmation équivalente pour la suppression globale.
- Modale de confirmation : réutilise les classes CSS existantes de `DocumentsPage`
  (`.modal-overlay`, `.modal`, `.modal-warning`, `.modal-actions`, `.btn-danger`, `.btn-ghost`) pour
  rester visuellement cohérent avec le reste de l'app. Un seul état local `pendingDelete: 'all' | { id: string; title: string } | null`
  pilote l'affichage (item ciblé vs suppression globale) et le texte du message.
- Après suppression réussie :
  - suppression d'un item : retire l'item de `sessions` en local (pas de re-fetch) ; si
    `id === activeSessionId`, `navigate('/chat')` pour revenir à un état vierge.
  - suppression globale : vide `sessions` ; si `activeSessionId` est défini, `navigate('/chat')`.
- En cas d'erreur réseau : affichage simple (texte d'erreur dans la modale), la modale reste ouverte
  pour permettre de réessayer ou d'annuler.

### Pas de changement à `ChatPage.tsx`

`ChatSidebar` gère déjà sa propre liste de sessions (`useEffect` + `reloadSignal`) ; la navigation
vers `/chat` déclenchée en cas de suppression de la conversation active suffit à réinitialiser
`ChatPage` (son `useEffect` sur `params.sessionId` vide déjà `messages` quand `sid` est absent).

## Hors périmètre

- Pas de undo / corbeille temporaire — suppression immédiate et définitive après confirmation.
- Pas de pagination/loading state spécifique pour "tout supprimer" (liste de sessions typiquement
  petite en usage dev/démo).
