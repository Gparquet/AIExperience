# CLAUDE.md — Step 3, front-end React / TypeScript

Contexte essentiel pour les assistants IA travaillant sur le front-end.
Les règles de comportement et les conventions transverses sont définies dans le
[CLAUDE.md racine](../../../CLAUDE.md) — ce fichier ne couvre que le front.

---

## Vue d'ensemble

Application **React 19 + TypeScript**, servie par **Vite**, sans bibliothèque d'état ni de style
externe. Trois notions structurent l'interface :

1. **L'ingestion est asynchrone.** Un upload renvoie immédiatement `202 Accepted` : le formulaire se
   débloque tout de suite, et l'avancement réel arrive ensuite par **SignalR**.
2. **Le chat a trois modes de démonstration** — recherche classique, LLM seul, RAG complet — pour
   comparer les approches côte à côte.
3. **Les conversations sont persistées** et pilotées par l'URL (`/chat/:sessionId`).

---

## Commandes

```bash
# Depuis Step3/src/Front
npm install
npm run dev       # http://localhost:5173
npm run build     # tsc -b && vite build
npm run lint      # ESLint
```

**Le back-end doit tourner sur `http://localhost:5406`.** Vite proxifie `/api` **et** `/hubs` vers
lui (`vite.config.ts`) — le proxy `/hubs` a `ws: true`, indispensable à la négociation WebSocket de
SignalR. Sans lui, la connexion part vers le serveur Vite et échoue silencieusement.

---

## Structure

```
src/
├── App.tsx                              ← Router + topbar de navigation
├── main.tsx                             ← Point d'entrée, monte le provider de notifications
├── index.css                            ← Tous les styles (CSS natif, un seul fichier global)
├── api/client.ts                        ← Client HTTP typé — SEUL point d'accès au back-end
├── types/index.ts                       ← Types partagés, miroir manuel des DTO C#
├── pages/
│   ├── DocumentsPage.tsx                ← Upload + liste des documents
│   ├── DocumentDetailPage.tsx           ← Suivi fin de l'ingestion d'un document
│   ├── VideoPage.tsx                    ← Upload vidéo/audio + transcription
│   └── ChatPage.tsx                     ← Chat RAG, 3 modes, citations
├── components/
│   ├── ChatSidebar.tsx                  ← Liste des conversations + suppression
│   └── IngestionStepper.tsx             ← Frise des étapes d'ingestion
├── context/IngestionNotificationsContext.tsx  ← Notifications d'ingestion transverses
└── realtime/ingestionHub.ts             ← Connexion SignalR
```

---

## Routes

| Route | Page | Rôle |
|-------|------|------|
| `/` | `DocumentsPage` | Upload et liste des documents |
| `/documents/:id` | `DocumentDetailPage` | Suivi étape par étape de l'ingestion |
| `/video` | `VideoPage` | Upload vidéo/audio, consultation de la transcription |
| `/chat` | `ChatPage` | Nouvelle conversation |
| `/chat/:sessionId` | `ChatPage` | Reprise d'une conversation existante |

---

## Notifications d'ingestion — `IngestionNotificationsContext`

C'est la pièce la moins évidente du front. Le provider est monté dans `App.tsx`, **autour du bloc
`<Routes>`**, ce qui lui permet de survivre aux changements de page : un utilisateur qui lance un
import puis navigue vers le chat reçoit quand même le toast de fin.

Il expose trois choses :

| API | Usage |
|-----|-------|
| `registerPendingDocument(id, fileName)` | Déclaré à l'upload, pour nommer le document dans le toast de fin |
| `subscribe(listener)` | Changements de **statut** (`documentStatusChanged`) — utilisé par les listes |
| `subscribeProgress(documentId, listener)` | Changements de **progression** (`documentProgressChanged`), **filtrés par document** |

**Pourquoi deux canaux séparés** : la progression émet à haute fréquence (pourcentages, compteurs de
chunks). La filtrer par `documentId` évite d'imposer ces rafales à la liste des documents, qui n'a
besoin que du statut.

**Repli automatique** : si SignalR est indisponible, le contexte bascule sur un *polling* toutes les
4 secondes. Une modification de ce fichier doit préserver les deux chemins.

---

## `api/client.ts` — le seul point d'accès au back-end

Aucun composant ne doit appeler `fetch` directement. Le client expose trois espaces de noms.

```ts
// Documents
api.documents.list()  ·  get(id)  ·  delete(id)
api.documents.checkDuplicate(fileName, contentHash)
api.documents.upload(file, strategy, replaceDocumentId?, onProgress?)   // → 202

// Vidéo
api.video.transcribe(file, options, onProgress?)                        // → 202
api.video.getTranscription(id)

// Chat
api.chat.getSystemPrompts()
api.chat.listSessions()  ·  getSession(id)
api.chat.deleteSession(id)  ·  deleteAllSessions()
api.chat.ask(payload)
api.chat.askStream(payload)                                             // AsyncGenerator<StreamEvent>
```

Deux mécanismes de transport cohabitent :

- **`request<T>()`** — `fetch` classique pour tout le reste.
- **`uploadWithProgress<T>()`** — `XMLHttpRequest`, uniquement pour les uploads. `fetch` ne sait pas
  reporter la progression d'un **envoi** ; c'est la seule raison de cette exception. Ne pas
  « moderniser » en `fetch` sans perdre la barre de progression.

L'URL de base vient de `import.meta.env.VITE_API_URL`, vide par défaut — ce qui fait passer les
appels par le proxy Vite en développement.

---

## `ChatPage` — les points délicats

### Les trois modes

```ts
type Mode = 'classic' | 'llm' | 'rag';
```

| Mode | Paramètres envoyés | Comportement |
|------|--------------------|--------------|
| `classic` | `useLlm: false` | Recherche full-text PostgreSQL, chunks bruts, aucun LLM |
| `llm` | `useLlm: true, useRag: false` | Le LLM répond seul, sans documents |
| `rag` | `useLlm: true, useRag: true` | Pipeline RAG complet avec citations |

Le prompt système n'est envoyé **que** pour `llm` et `rag` — la recherche full-text n'en a aucun
usage. Les prompts par défaut sont chargés depuis `GET /api/chat/system-prompts` : le back-end est la
source de vérité, ne jamais les redéclarer en dur côté front.

À la relecture d'une conversation, le mode d'affichage est **déduit** de la stratégie enregistrée :
`FullText` → `classic`, `DirectLlm` → `llm`, tout le reste → `rag`.

### Le pilotage par l'URL

Un `useEffect` sur `params.sessionId` gouverne l'état de la conversation :

- `/chat` sans identifiant → conversation vierge ;
- `/chat/:id` → rechargement via `getSession` ;
- session invalide (404) → retour silencieux à `/chat`, sans erreur bloquante.

**Le piège à connaître** : après le premier tour d'une conversation vierge, le composant appelle
`navigate('/chat/{id}', { replace: true })`. Sans garde, cette navigation redéclencherait l'effet et
**écraserait les citations riches** (section, horodatages vidéo) affichées à l'instant par la version
appauvrie relue en base — ces champs ne sont pas persistés côté back. C'est le rôle de `skipLoadRef` :
il marque la session qui vient d'être créée pour que l'effet ignore ce seul rechargement. Toute
modification de cet effet doit préserver ce comportement.

### Streaming SSE

`api.chat.stream` consomme trois types d'événements :

```ts
type StreamEvent =
  | { event: 'token'; data: { token: string } }
  | { event: 'done';  data: AskQuestionResponse }
  | { event: 'error'; data: { message: string } };
```

Les citations et les métadonnées n'arrivent qu'avec `done` — pendant le flux, seul le texte se
construit.

---

## `types/index.ts` — miroir manuel des DTO C#

Ce fichier est écrit **à la main** et doit refléter `Web.Api/DTOs/`. Il n'y a aucune génération
automatique : **toute évolution d'un DTO côté back impose une modification manuelle ici**, sans quoi
l'erreur n'apparaît qu'à l'exécution.

Principaux types : `DocumentResponse`, `DocumentStatus`, `IngestionStage`, `IngestionProgress`,
`DocumentStatusChangedEvent`, `DocumentProgressChangedEvent`, `CheckDuplicateResponse`,
`CitationResponse`, `AskQuestionRequest` / `Response`, `ChatSessionSummary`, `ChatSessionDetail`,
`StreamEvent`, `VideoTranscriptionResponse`.

> Génération de ces types depuis l'OpenAPI du back (`openapi-typescript`) : piste identifiée, non mise
> en œuvre.

---

## Conventions

- **Composants fonctionnels** avec hooks, props typées, fichiers en PascalCase.
- **État local** via `useState`. Le seul contexte est celui des notifications d'ingestion — il existe
  parce que l'information doit survivre à un changement de page, pas par principe.
- **Styles** : CSS natif dans `index.css`, classes préfixées par composant (`chat-sidebar-*`,
  `ingestion-stepper-*`). Ni CSS-in-JS, ni framework, ni modules CSS.
- **Erreurs** : `try/catch` dans le composant, message affiché à l'utilisateur en français.
- **Aucun appel réseau hors de `client.ts`.**

---

## Dette technique connue

> Résumé côté front. Liste de référence complète : [DETTE-TECHNIQUE.md](../../DETTE-TECHNIQUE.md).

| Sujet | État |
|-------|------|
| **Tests** | **Zéro test côté front.** Le parseur SSE de `client.ts` est le premier candidat évident (Vitest). |
| Citations non cliquables | Les horodatages vidéo et numéros de page sont affichés mais n'ouvrent rien — le back ne conserve pas les fichiers originaux |
| Citations appauvries à la relecture | Une conversation rechargée perd section et horodatages (champs non persistés côté back) |
| Drag & drop | Reporté — l'upload passe uniquement par le sélecteur de fichiers |
| Types manuels | Pas de génération depuis l'OpenAPI |

---

## Checklist avant de coder

- [ ] Le back tourne-t-il sur le port 5406 (sinon proxy Vite en échec) ?
- [ ] Le type touché existe-t-il déjà dans `types/index.ts`, et correspond-il au DTO C# actuel ?
- [ ] L'appel réseau passe-t-il bien par `client.ts` ?
- [ ] Si l'écran affiche un état d'ingestion : passe-t-il par `IngestionNotificationsContext`
      plutôt que par un polling ad hoc ?
