# Design — Lot 3 (R-2) : Historique de conversation réel

Date : 2026-07-13
Périmètre : `Step3/src/Back` + `Step3/src/Front`
Référence plan : `Step3/PLAN-AMELIORATION-RAG.md`, Lot 3, point 17 (R-2)

---

## 1. Contexte et constat

Le plan d'amélioration RAG liste R-2 comme non commencé : « Historique de conversation mort —
`ChatController.Ask`/`Stream` ne transmettent jamais de `SessionId` ni ne persistent les messages ».

Une exploration du code (2026-07-13) montre que **la moitié du travail existe déjà, mais n'est
jamais branchée** :

- `ConversationSession`, `ChatMessage`, `Citation` (entités Domain), `IConversationRepository` +
  son implémentation EF Core existent déjà et sont enregistrés en DI.
- Le schéma SQL (`conversation_sessions`, `chat_messages`, `citations`) existe déjà dans
  `init.sql` — **aucune migration nécessaire**.
- `RagPipelineService` sait déjà **lire** l'historique (`conversationRepository.GetMessagesAsync`)
  dans `AskAsync`, `AskStreamAsync`, `AskDirectLlmAsync`, `StreamDirectLlmAsync`, à condition que
  `query.IncludeHistory && query.SessionId != Guid.Empty`.

Ce qui manque, vérifié par grep sur tout `src/Back` :

- `AddSessionAsync` et `AddMessageAsync` (écriture) ne sont **jamais appelés** nulle part dans le
  code applicatif — seulement définis dans le repository.
- `ChatController` ne transmet jamais de `SessionId` : `RagQuery.SessionId` reste toujours
  `Guid.Empty`, ce qui désactive silencieusement la lecture d'historique déjà câblée.
- Le front (`ChatPage.tsx`) n'a aucune notion de session : les messages vivent en `useState` React
  et disparaissent au rechargement de la page. Aucune liste de conversations, aucun bouton
  « nouvelle conversation ».

Conséquence : la fonctionnalité multi-tour annoncée par Step 3 (`RagQuery.IncludeHistory`,
`MaxHistoryTurns`) est actuellement inopérante en pratique, malgré une infrastructure prête aux
trois quarts.

Limitation connue et assumée (validée avec l'utilisateur) : `Citation` a 4 propriétés
`[NotMapped]` (`SectionTitle`, `ChunkIndex`, `StartTime`, `EndTime`) — non persistées en base.
Une conversation reprise depuis l'historique affichera donc des citations avec document/page/
score/extrait, mais **sans** section ni timestamp vidéo précis. Pas de migration de schéma dans ce
lot pour combler cet écart.

---

## 2. Architecture back-end

### 2.1. Résolution et persistance de session dans `RagPipelineService`

Deux méthodes privées ajoutées à `RagPipelineService`, appelées aux 5 points d'entrée existants
(`AskAsync`, `AskStreamAsync`, `AskDirectLlmAsync`, `StreamDirectLlmAsync`, `AskFullTextAsync`) :

- **`ResolveSessionAsync(RagQuery query, CancellationToken ct) : Task<Guid>`**
  Si `query.SessionId == Guid.Empty` : crée une `ConversationSession` (titre = question tronquée à
  ~60 caractères), `AddSessionAsync`, retourne le nouvel id. Sinon : charge la session existante et
  appelle `Touch()`, retourne l'id inchangé. Le `Guid` résolu est passé explicitement aux méthodes
  suivantes (le `RagQuery` record n'est pas muté — `BuildChatHistoryAsync`/`AskDirectLlmAsync`
  continuent de lire `query.SessionId` pour la relecture d'historique, qui doit rester basée sur
  l'id déjà connu **avant** l'ajout des messages du tour courant).

- **`PersistExchangeAsync(Guid sessionId, string question, RagResponse response, CancellationToken ct) : Task`**
  Crée `ChatMessage.CreateUserMessage(sessionId, question)` puis
  `ChatMessage.CreateAssistantMessage(sessionId, response.Answer, response.TotalTokens,
  response.StrategyUsed, response.DurationMs)`, recrée chaque `Citation` de `response.Citations`
  avec le vrai `MessageId` de l'assistant (le `Guid.Empty` actuellement câblé dans
  `RagPipelineService` était un trou volontaire en attendant ce lot), puis un seul
  `SaveChangesAsync` final. Entouré d'un `try/catch` : une erreur de persistance est loguée en
  `Warning` mais **ne fait pas échouer la réponse déjà générée** à l'utilisateur (dégradation
  gracieuse — cohérent avec le reste du pipeline qui ne bloque jamais sur du non-critique).

Décision : la session est créée/alimentée **quel que soit le mode** (`classic`/`llm`/`rag`), pas
seulement en mode RAG complet, pour que la sidebar montre un fil cohérent même si l'utilisateur
change d'onglet en cours de conversation. Seul le flag `IncludeHistory` (déjà en place) continue de
décider si l'historique est réinjecté au LLM — le mode `classic` (full-text) n'a jamais consulté
l'historique et ne commence pas à le faire ici.

### 2.2. Changements de signature

- `RagPipelineService` reçoit `IUnitOfWork` en plus de `IConversationRepository` (déjà injecté).
- `RagResponse` (Domain) gagne une propriété `Guid SessionId` — portée jusqu'au contrôleur.

### 2.3. Nouvelle méthode de lecture pour la sidebar

`IConversationRepository.GetSessionSummariesAsync(string userId, CancellationToken ct)` — projection
légère (`Id`, `Title`, `UpdatedAt`, nombre de messages) triée par `UpdatedAt` décroissant, pour
éviter de charger tous les messages d'une session juste pour peupler la liste (contrairement à
`GetSessionsByUserIdAsync`, qui reste inutilisée pour l'instant).

---

## 3. Contrats API (`ChatController`)

Toujours sans MediatR pour les endpoints ajoutés ici (lecture directe via repository, cohérent avec
la convention du projet : « Requêtes → appel de service direct »).

### DTOs modifiés

```csharp
// AskQuestionRequest — ajout
Guid? SessionId = null

// AskQuestionResponse — ajout
Guid SessionId   // toujours renvoyé, nouveau ou réutilisé
```

### Nouveaux DTOs / endpoints

```
GET /api/chat/sessions
  → List<ChatSessionSummaryResponse>
    ChatSessionSummaryResponse(Guid Id, string Title, DateTimeOffset UpdatedAt, int MessageCount)
  Triée par activité récente, scope = DevAuthOptions.DefaultUserId (même pattern que
  DocumentsController).

GET /api/chat/sessions/{id}
  → ChatSessionDetailResponse
    ChatSessionDetailResponse(Guid Id, string Title, List<ChatMessageResponse> Messages)
    ChatMessageResponse(string Role, string Content, List<CitationResponse>? Citations,
      string? StrategyUsed, int TokensUsed, long DurationMs, DateTimeOffset CreatedAt)
  404 si la session n'existe pas ou n'appartient pas à l'utilisateur courant.
```

`POST /api/chat/stream` : le `SessionId` résolu est renvoyé dans l'événement `done` (payload
`AskQuestionResponse`), pas de nouvel événement SSE.

---

## 4. Front-end

- **Route** : `/chat` devient `/chat/:sessionId?` (paramètre optionnel, même pattern que
  `/documents/:id`). `ChatPage` lit `useParams<{ sessionId?: string }>()`.
- **Nouveau composant `ChatSidebar`** : charge `api.chat.listSessions()` au montage et après chaque
  nouvel échange (pour remonter la conversation active en tête de liste) ; bouton
  **« + Nouvelle conversation »** → `navigate('/chat')` + reset de l'état local ; clic sur un item
  → `navigate('/chat/' + id)`.
- **Chargement d'une session existante** : `useEffect` sur `sessionId` — si présent,
  `api.chat.getSession(id)` hydrate `messages`. Mapping `StrategyUsed` → `mode` local (pas de champ
  dédié nécessaire, `StrategyUsed` suffit à reconstituer le rendu) :
  - `"FullText"` → `mode: 'classic'`
  - `"DirectLlm"` → `mode: 'llm'`
  - toute autre valeur (`Direct`/`HyDE`/`Fusion`/`Adaptive`) → `mode: 'rag'`
  Si `sessionId` absent : état vierge (comportement actuel).
- **Après le premier envoi d'une conversation vierge** : la réponse (`res.sessionId`) déclenche
  `navigate('/chat/' + res.sessionId, { replace: true })` — pas de nouvelle entrée d'historique
  navigateur, l'URL devient rechargeable/partageable.
- **Layout** : `ChatPage` passe d'une colonne unique à un flex `sidebar | conversation`, en
  réutilisant les classes CSS existantes (`chat-main`) pour le panneau de droite.
- **Session invalide dans l'URL** (404 du back-end) : retour silencieux à une conversation vierge,
  pas d'erreur bloquante affichée.

### Hors périmètre (YAGNI, à reconfirmer plus tard si besoin)

- Suppression ou renommage d'une conversation.
- Recherche dans l'historique des conversations.
- Pagination de la liste des sessions.
- Authentification multi-utilisateur réelle (le design est compatible : `ConversationSession.UserId`
  et les nouvelles méthodes de lecture filtrent déjà par `userId`, aujourd'hui la valeur hardcodée
  `DevAuthOptions.DefaultUserId` comme partout ailleurs dans le projet ; remplacer cette source par
  un utilisateur authentifié ne demandera aucune reprise du schéma ni de la logique de sessions).

---

## 5. Cas limites & robustesse

| Cas | Comportement |
|-----|--------------|
| `GET /api/chat/sessions/{id}` sur un id inexistant ou d'un autre utilisateur | 404 → front revient à une conversation vierge |
| `SaveChangesAsync` échoue dans `PersistExchangeAsync` après une réponse LLM réussie | Réponse quand même retournée à l'utilisateur ; erreur loguée en `Warning` ; le tour n'est simplement pas persisté |
| Concurrence sur une même session (deux onglets) | Non géré — hors périmètre (mono-utilisateur en dev) |
| Titre de session | Généré une seule fois depuis la première question (troncature ~60 car.), jamais régénéré ensuite |

---

## 6. Tests

- **`RagPipelineServiceTests`** (nouveaux cas, réutilisant les fakes `IConversationRepository`/
  `IUnitOfWork` déjà présents dans le projet de tests) :
  - Création de session quand `SessionId` est vide.
  - Réutilisation + `Touch()` quand un `SessionId` valide est fourni.
  - Persistance du message utilisateur, du message assistant et des citations avec le bon
    `MessageId`.
  - La réponse n'échoue pas si `SaveChangesAsync` lève une exception (dégradation gracieuse).
- **Repository** : pas de test dédié pour `GetSessionSummariesAsync` (requête EF simple, même
  niveau que le reste du repository, non testé unitairement aujourd'hui) — vérifié par intégration
  manuelle.
- **Front** : aucune suite de tests JS dans ce projet à ce jour — vérification manuelle en
  navigateur (nouvelle conversation, rechargement via URL, reprise depuis la sidebar, session
  invalide dans l'URL).
