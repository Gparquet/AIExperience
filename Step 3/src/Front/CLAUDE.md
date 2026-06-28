# CLAUDE.md — AIExperience Step 3 (Front-end React/TypeScript)

Contexte essentiel pour les assistants IA travaillant sur le front-end de Step 3.

---

## Rôle et comportement de l'assistant

Tu es un **leader technique senior** spécialisé en **React / TypeScript / UX**.

### Règles impératives — à respecter sans exception

1. **Langue** : toutes les réponses et tous les commentaires de code sont rédigés **en français**, sans exception.
2. **Mode de réponse** : structurer la réponse sous forme de **plan** (étapes numérotées, sections claires) avant d'expliquer ou de coder.
3. **Commentaires dans le code** : **tout le code produit doit être commenté** — chaque composant, fonction, bloc logique non trivial reçoit un commentaire JSDoc/inline (`//` ou `/* */`).
4. **Posture** : adopter le point de vue d'un tech lead — proposer des solutions robustes, scalables, et signaler les risques ou améliorations identifiés.

---

## Vue d'ensemble — Step 3

**Application React 19 + TypeScript / Vite pour le RAG avec support vidéo.**

Step 3 conserve l'interface des steps précédentes (pages Documents, Chat) et **ajoute une nouvelle page Vidéo** permettant de transcriber des fichiers audio/vidéo localement (via l'API back-end Whisper/FFmpeg) et de les ingérer dans le RAG.

- **3 pages principales** : Documents, Vidéo (NOUVELLE), Chat (enrichie)
- **Chat avec 3 modes de démonstration** : Full-text (recherche PostgreSQL), LLM direct (sans RAG), RAG complet (avec citations enrichies)
- **Timestamps vidéo propagés** dans les citations (affichage de la position temporelle dans la source vidéo)

---

## Commandes essentielles

```bash
# Dans Step 3/src/Front
npm install                    # Installer les dépendances
npm run dev                    # Démarrer dev server (http://localhost:5173)
npm run build                  # Build production
npm run preview               # Preview du build production
npm run lint                   # Linter (si configuré)
```

---

## Structure des fichiers

```
src/
├── App.tsx                    ← Composant racine + Router
├── pages/
│   ├── DocumentsPage.tsx      ← Upload PDF/vidéo, gestion des documents
│   ├── VideoPage.tsx          ← NOUVELLE — Transcription vidéo/audio
│   └── ChatPage.tsx           ← Enrichi — 3 modes (full-text / LLM / RAG)
├── api/
│   └── client.ts              ← Client HTTP typé (namespaces: documents, video, chat)
├── types/
│   └── index.ts               ← Types TypeScript partagés
└── index.css / main.tsx       ← Styles globaux + entrée
```

---

## Routes (Router)

| Route | Page | Description |
|-------|------|-------------|
| `/` | `DocumentsPage` | Upload de fichiers (PDF, vidéo, audio), liste des documents |
| `/video` | `VideoPage` | **NOUVEAU** — Transcription vidéo/audio avec options |
| `/chat` | `ChatPage` | Interface chat RAG avec 3 modes de démonstration |

**Navigation :** Topbar avec 3 onglets (Documents, Vidéo, Chat)

---

## VideoPage.tsx — Nouvelle page Step 3

### Fonctionnalités principales

**Upload de fichier :**
- Accepte vidéo/audio (formats : `.mp4`, `.mkv`, `.webm`, `.avi`, `.mov`, `.wav`, `.mp3`, `.m4a`)
- Validation du fichier côté client

**Options de transcription :**
- **Langue** (dropdown, défaut "fr") — code ISO 639-1
- **Nettoyage LLM** (checkbox, défaut désactivé) — supprime balises timing, envoie texte au LLM
- **Auto-ingest RAG** (checkbox, défaut activé) — ingère automatiquement le document après transcription
- **Titre** (input optionnel) — titre du document source

**Affichage du résultat :**
- Statistiques : durée vidéo, nombre de segments Whisper, temps de traitement
- **Transcription nettoyée** (affichée en priorité si présente)
- **Transcription brute** (repliable/dépliable, affiche timestamps `[HH:MM:SS]`)
- Bouton **"Interroger ce document dans le Chat →"** — navigue vers `/chat` en passant le `documentId` en state

### Appel API

```typescript
api.video.transcribe(file, {
  language: "fr",
  cleanWithLlm: false,
  autoIngest: true,
  title: "Mon vidéo"
})

// → POST /api/video/transcribe (multipart/form-data)
// → TranscribeVideoResponse
```

### Types TypeScript

```typescript
interface TranscribeVideoResponse {
  rawTranscription: string;             // Transcription brute avec timestamps
  cleanedTranscription?: string;        // Transcription nettoyée (optionnelle)
  duration: number;                     // Durée en secondes
  segmentCount: number;                 // Nombre de segments Whisper
  documentId?: string;                  // ID du document créé (si autoIngest=true)
  processingTimeMs: number;             // Temps de traitement en ms
}

interface VideoTranscribeOptions {
  language?: string;        // Code langue ISO (défaut "fr")
  cleanWithLlm?: boolean;   // Nettoyage LLM (défaut false)
  autoIngest?: boolean;     // Auto-ingest RAG (défaut true)
  title?: string;           // Titre du document
}
```

---

## ChatPage.tsx — Enrichi en Step 3

### 3 modes de démonstration

La page affiche 3 onglets permettant de comparer les approches :

| Mode | Paramètres API | Comportement | Badge | Cas d'usage |
|------|---|---|---|---|
| **Classique** | `UseLlm=false`, `UseRag=true` | Recherche PostgreSQL `plainto_tsquery` sans embeddings ni LLM. Retourne chunks bruts. | "🔍 Recherche" | Démonstration full-text, requêtes factuelles, faible latence |
| **LLM** | `UseLlm=true`, `UseRag=false` | Question directement au LLM, sans récupération documentaire. Répond sur la base du modèle seul. | "🤖 Sans documents" | Démonstration capacités LLM, questions générales |
| **RAG** | `UseLlm=true`, `UseRag=true` | Pipeline RAG complet : embed question → cosinus pgvector → LLM. Retourne réponse + citations enrichies. | "📄 Avec documents" | Production, questions spécialisées, réponses avec sources |

### Fonctionnalités enrichies Step 3

**Panneau prompt système :**
- Par mode (3 prompts séparés chargés depuis `GET /api/chat/system-prompts`)
- Éditeur de texte libre (modification utilisateur)
- Bouton "Reset" pour restaurer le prompt par défaut

**Sélecteur de document :**
- Dropdown listant tous les documents disponibles
- Option "Tous les documents" (défaut)
- Filtre la recherche/RAG sur un document spécifique si sélectionné

**Rendu des réponses :**
- Mode **Classique** : liste de résultats de recherche (chunks bruts)
- Modes **LLM** et **RAG** : bulle de chat avec réponse du LLM

**Citations enrichies :**
- Contenu du chunk
- Nom du document source
- Score de pertinence (cosinus pour RAG)
- Section/titre (si disponible)
- **Index du chunk** dans le document
- **Timestamps vidéo** (si document source est une vidéo) :
  - `startTimeSeconds` et `endTimeSeconds`
  - Affichage formaté `[HH:MM:SS]` (peut être lié à un lecteur vidéo dans les améliorations futures)

### Types TypeScript — Enrichis

```typescript
interface CitationResponse {
  content: string;              // Contenu du chunk cité
  documentName: string;         // Nom du document source
  score: number;                // Score de pertinence (cosinus)
  sectionTitle?: string;        // NOUVEAU — Titre de la section (ex: "Introduction")
  chunkIndex?: number;          // NOUVEAU — Index du chunk dans le document
  startTimeSeconds?: number;    // NOUVEAU — Début du segment vidéo en secondes
  endTimeSeconds?: number;      // NOUVEAU — Fin du segment vidéo en secondes
}

interface AskQuestionRequest {
  question: string;
  documentIds?: string[];       // IDs des documents à interroger
  strategy?: string;            // Stratégie RAG (Direct, HyDE, Fusion, Adaptive)
  useLlm?: boolean;             // NOUVEAU — Utiliser LLM (false = mode full-text)
  useRag?: boolean;             // NOUVEAU — Utiliser RAG (false = LLM direct)
  systemPrompt?: string;        // NOUVEAU — Prompt système personnalisé
}

interface SystemPromptsResponse {
  ragSystem: string;            // Prompt RAG par défaut
  directLlmSystem: string;      // Prompt LLM direct par défaut
}
```

---

## client.ts — API Client HTTP

### Namespaces disponibles

**Documents :**
```typescript
api.documents.upload(file)          // POST /api/documents (multipart/form-data)
api.documents.getAll()              // GET  /api/documents
api.documents.delete(id)            // DELETE /api/documents/{id}
```

**Vidéo (NOUVEAU Step 3) :**
```typescript
api.video.transcribe(file, options) // POST /api/video/transcribe (multipart/form-data)
// options: { language?, cleanWithLlm?, autoIngest?, title? }
```

**Chat :**
```typescript
api.chat.ask(request)               // POST /api/chat/ask (blocking)
api.chat.stream(request, callbacks) // POST /api/chat/stream (Server-Sent Events)
// callbacks: { onMessage, onError, onComplete }

api.chat.getSystemPrompts()         // GET /api/chat/system-prompts — NOUVEAU Step 3
```

### Exemple d'utilisation — Transcription vidéo

```typescript
const response = await api.video.transcribe(file, {
  language: "fr",
  cleanWithLlm: false,
  autoIngest: true,
  title: "Ma présentation"
});

console.log(response.documentId);  // UUID du document créé
console.log(response.duration);    // Durée en secondes
```

### Exemple d'utilisation — Chat RAG enrichi

```typescript
const response = await api.chat.ask({
  question: "Quels sont les points clés?",
  documentIds: ["uuid-1"],
  useLlm: true,
  useRag: true,
  systemPrompt: "Répondez en 3 bullet points."
});

// Citations avec timestamps vidéo (si applicable)
response.citations.forEach(c => {
  if (c.startTimeSeconds !== undefined) {
    console.log(`[${formatTime(c.startTimeSeconds)}] ${c.content}`);
  }
});
```

---

## types/index.ts — Types partagés

```typescript
// ===== Documents =====
interface Document {
  id: string;
  fileName: string;
  fileType: string;
  fileSize: number;
  uploadedAt: string;
  contentType: string;
}

// ===== Citations (enrichies Step 3) =====
interface CitationResponse {
  content: string;
  documentName: string;
  score: number;
  sectionTitle?: string;           // NOUVEAU
  chunkIndex?: number;             // NOUVEAU
  startTimeSeconds?: number;       // NOUVEAU
  endTimeSeconds?: number;         // NOUVEAU
}

// ===== Chat (enrichi Step 3) =====
interface AskQuestionRequest {
  question: string;
  documentIds?: string[];
  strategy?: string;
  useLlm?: boolean;                // NOUVEAU
  useRag?: boolean;                // NOUVEAU
  systemPrompt?: string;           // NOUVEAU
}

interface AskQuestionResponse {
  answer: string;
  citations: CitationResponse[];
  strategyUsed: string;
  totalTokens: number;
  durationMs: number;
}

interface StreamChunk {
  type: "delta" | "citations" | "complete";
  data: string;
}

// ===== Vidéo (NOUVEAU Step 3) =====
interface TranscribeVideoResponse {
  rawTranscription: string;
  cleanedTranscription?: string;
  duration: number;
  segmentCount: number;
  documentId?: string;
  processingTimeMs: number;
}

interface VideoTranscribeOptions {
  language?: string;
  cleanWithLlm?: boolean;
  autoIngest?: boolean;
  title?: string;
}

// ===== Prompts système (NOUVEAU Step 3) =====
interface SystemPromptsResponse {
  ragSystem: string;
  directLlmSystem: string;
}
```

---

## Stack technique

| Technologie | Version | Usage |
|------------|---------|-------|
| React | 19 | Framework UI |
| TypeScript | 5.x | Langage typé |
| Vite | 5.x | Build tool + dev server |
| React Router | v7 | Routing multi-pages |
| Fetch API | natif | Appels HTTP vers l'API REST |

---

## Patterns et conventions

### Composants React

- Composants fonctionnels avec hooks
- Props typés via TypeScript interfaces
- Noms de fichiers en PascalCase (ex: `VideoPage.tsx`)

### État local

- `useState` pour l'état local du composant
- Pas de context/Redux (simple, scope local suffit)

### Appels API

- Client HTTP typé dans `client.ts`
- Namespaces pour organiser par ressource
- Gestion d'erreurs côté composant (try/catch)

### Styles

- CSS CSS natif (fichiers `.css` globaux et locaux)
- Pas de CSS-in-JS compliqué

---

## DocumentsPage.tsx — Gestion des documents

### Fonctionnalités

- Upload de fichier (PDF, vidéo, audio)
- Validation du type de fichier
- Liste des documents uploadés avec delete
- Boutons de navigation vers /video et /chat

### Types acceptés

- **PDF** : `.pdf`
- **Vidéo** : `.mp4`, `.mkv`, `.webm`, `.avi`, `.mov`
- **Audio** : `.wav`, `.mp3`, `.m4a`, `.ogg`, `.flac`

---

## TODOs connus Front Step 3

| Fonctionnalité | Statut | Impact |
|----------------|--------|--------|
| Affichage lien/lecteur vidéo pour timestamps dans citations | Non implémenté | UX — données API disponibles, rendu manquant |
| Streaming SSE en temps réel dans ChatPage | À vérifier | UX — peut être à câbler |
| Indicateur "chargement..." pendant transcription vidéo | À vérifier | UX — expérience utilisateur |
| Prévisualisation transcription en direct (mode streaming) | Non implémenté | Nice-to-have |

---

## Architecture et données

### Flux de transcription vidéo complet

```
1. User upload via VideoPage
   ↓
2. POST /api/video/transcribe (FormData + options)
   ↓
3. Back-end : FFmpeg → Whisper → LLM nettoyage → Ingestion RAG
   ↓
4. Réponse : TranscribeVideoResponse { documentId, rawTranscription, ... }
   ↓
5. Front-end affiche résultats
   ↓
6. User clique "Interroger dans le Chat →"
   ↓
7. Navigate vers /chat avec documentId en state
   ↓
8. ChatPage reçoit state, pré-sélectionne le document
```

### Flux de chat avec citations vidéo

```
1. User pose question dans ChatPage (mode RAG)
   ↓
2. POST /api/chat/ask avec { question, documentIds, useLlm, useRag, systemPrompt }
   ↓
3. Back-end retourne AskQuestionResponse avec citations enrichies
   ↓
4. Citations contiennent startTimeSeconds / endTimeSeconds
   ↓
5. Front-end affiche timestamps formatés [HH:MM:SS]
   ↓
6. (Futur) Clic sur timestamp → ouvre lecteur vidéo à la position
```

---

## Fichiers clés à connaître

| Fichier | Rôle |
|---------|------|
| `src/App.tsx` | Composant racine, Router avec 3 routes, topbar navigation |
| `src/pages/DocumentsPage.tsx` | Upload documents, liste, suppression |
| `src/pages/VideoPage.tsx` | **NOUVEAU** — Upload vidéo, options, affichage transcription |
| `src/pages/ChatPage.tsx` | Chat RAG enrichi, 3 modes, prompt editor, citations |
| `src/api/client.ts` | Client HTTP typé, namespaces documents/video/chat |
| `src/types/index.ts` | Tous les types TypeScript (Document, Citation, AskQuestion, Transcribe, SystemPrompts) |
| `src/index.css` | Styles globaux |
| `src/main.tsx` | Entrée de l'application |
| `vite.config.ts` | Config Vite (si existe) |
| `package.json` | Dependencies (React 19, Vite, TypeScript, React Router) |
| `tsconfig.json` | Config TypeScript |

---

## Intégration avec Back-end

### Ports

- **Front-end dev** : `http://localhost:5173`
- **Back-end API** : `http://localhost:50406` (HTTP) ou `https://localhost:50405` (HTTPS)

### CORS

Le back-end (`Program.cs`) configure CORS pour autoriser :
- `http://localhost:5173` (dev front-end Vite)
- `http://localhost:5174` (alternative)
- `http://localhost:3000` (alt)

### Configuration API

L'URL de base de l'API est hardcodée ou configurable dans `client.ts`. Exemple :

```typescript
const API_BASE = "http://localhost:50406";

export const api = {
  video: {
    transcribe: async (file, opts) => {
      const formData = new FormData();
      formData.append("file", file);
      if (opts.language) formData.append("language", opts.language);
      // ...
      return fetch(`${API_BASE}/api/video/transcribe`, { ... });
    }
  }
};
```

---

## Checklist avant de coder

- [ ] Compris les 3 modes RAG (Full-text, LLM, RAG complet)?
- [ ] Connu les timestamps vidéo dans CitationResponse?
- [ ] Checké le schema TypeScript des réponses API?
- [ ] Testé l'API back-end `/api/video/transcribe` avec Scalar ou Postman?
- [ ] Vérifier que les ports (5173 front, 50406 back) sont accessibles?
