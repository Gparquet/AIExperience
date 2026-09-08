# Architecture — AIExperience Step 3

Ce document décrit l'architecture technique de **Step 3**. Il est destiné aux développeurs, aux
assistants IA et à tout contributeur ayant besoin de comprendre le système avant de le modifier.

## Comment cette documentation est organisée

Trois documents, trois rôles distincts — pour éviter qu'une même information vive à deux endroits
et y diverge :

| Document | Répond à | Se périme ? |
|----------|----------|-------------|
| **Ce fichier** | *Qu'est-ce qui existe, et pourquoi c'est construit ainsi ?* — inventaire des couches et décisions structurantes | Oui, à maintenir avec le code |
| [`src/Back/CLAUDE.md`](src/Back/CLAUDE.md) · [`src/Front/CLAUDE.md`](src/Front/CLAUDE.md) | *Comment travailler dessus ?* — conventions, pièges, dette technique | Oui, à maintenir avec le code |
| [`docs/adr/`](docs/adr/) | *Pourquoi cette décision, à cette date, contre quelles alternatives ?* | **Non** — un ADR est daté par nature et ne se modifie pas |
| [`PLAN-AMELIORATION-RAG.md`](PLAN-AMELIORATION-RAG.md) · [`DETTE-TECHNIQUE.md`](DETTE-TECHNIQUE.md) | *Que reste-t-il à faire ?* — RAG d'un côté, socle de l'autre | Oui, listes de suivi |

> Quand une décision est consignée dans un ADR, ce document s'y réfère au lieu de la recopier.

---

## Vue d'ensemble

Système RAG complet : ingestion multi-format (documents, données, vidéo/audio) et restitution
conversationnelle avec citations. Tout fonctionne **en local** — aucun appel cloud n'est requis,
ni pour le LLM, ni pour les embeddings, ni pour la transcription.

```
INGESTION (asynchrone)                    RESTITUTION
──────────────────────                    ───────────
upload ──► 202 Accepted                   question
             │                               │
      outbox_messages                     résolution de stratégie
             │                               │
      IngestionWorker                     récupération hybride
             │                            (vectoriel + lexical → RRF)
   ┌─────────┴─────────┐                     │
document            vidéo                 reranking LLM batché
   │                   │                     │
extraction      FFmpeg → Whisper          complétion LLM (SSE)
   │                   │                     │
   └────────┬──────────┘                  citations + persistance
        chunking                             │
            │                             conversation_sessions
        embeddings                         chat_messages
            │
   pgvector + tsvector
            │
   notification SignalR
```

---

## Structure des dossiers

```
Step3/
├── docker-compose.yml              PostgreSQL 17 + pgvector (port 5433)
├── ARCHITECTURE.md                 ce document
├── PLAN-AMELIORATION-RAG.md        diagnostic RAG et backlog priorisé
├── docs/adr/                       12 décisions d'architecture datées
├── scripts/                        init.sql + 6 scripts de migration
├── models/                         modèles Whisper (.bin, non versionnés)
└── src/
    ├── Back/
    │   ├── AIExperience.slnx
    │   ├── AIExperience.Rag.Domain/
    │   ├── AIExperience.Rag.Application/
    │   ├── AIExperience.Rag.Infrastructure/
    │   ├── AIExperience.Web.Api/
    │   ├── AIExperience.App.Console/
    │   └── AIExperience.Tests/
    └── Front/                      React 19 + TypeScript (Vite)
```

---

## Couches de la Clean Architecture

Quatre couches, **règle de dépendance vers l'intérieur** stricte. Une couche interne ne connaît
jamais une couche externe.

```
Domain  ←  Application  ←  Infrastructure  ←  Web.Api / Console
```

| Couche | Projet | Responsabilités | Peut référencer |
|--------|--------|----------------|-----------------|
| **Domain** | `AIExperience.Rag.Domain` | Entités, interfaces, enums, modèles de transfert. Zéro dépendance externe. | Rien |
| **Application** | `AIExperience.Rag.Application` | CQRS maison, cas d'usage, extraction de texte, chunking, worker d'ingestion. | Domain |
| **Infrastructure** | `AIExperience.Rag.Infrastructure` | Pipeline RAG, EF Core, pgvector, Whisper, FFmpeg, clients IA. | Domain + Application |
| **Web.Api** | `AIExperience.Web.Api` | Composition DI, controllers REST, hub SignalR, CORS, OpenAPI. | Toutes |
| **Console** | `AIExperience.App.Console` | Composition DI, menu interactif. | Toutes |

---

## Couche Domain

### Entités

| Entité | Description |
|--------|-------------|
| `Document` | Fichier uploadé. Porte son cycle de vie (`IngestionStatus`), son avancement fin (`IngestionProgress`) et son empreinte (`ContentHash`) pour la détection de doublons. |
| `DocumentChunk` | Fragment d'un document : texte, embedding pgvector, position (`ChunkIndex`, `PageNumber`, `SectionTitle`) et bornes temporelles (`StartTime`/`EndTime`) pour les vidéos. |
| `Citation` | Référence source d'une réponse (document, extrait, score, page). |
| `ConversationSession` | Conversation multi-tour d'un utilisateur. |
| `ChatMessage` | Message d'une conversation (rôle, contenu, stratégie, métriques). |
| `OutboxMessage` | Travail d'ingestion mis en file, écrit dans la même transaction que le document. |

Toutes les entités ont des **setters privés** et se créent par **méthode factory statique**
(`Entity.Create(...)`).

### Modèles de transfert (non-entités)

| Modèle | Description |
|--------|-------------|
| `RagQuery` | Requête entrante : question, stratégie, filtres documentaires, mode (`UseLlm`/`UseRag`), session, prompt système. |
| `RagResponse` | Réponse complète : texte, citations, stratégie utilisée, métriques, `SessionId`. |
| `RagStreamChunk` | Unité de streaming : un `Token`, ou `IsDone = true` avec le `FinalResponse`. |
| `TextChunk` | Chunk produit par un chunker, avant persistance. |
| `IngestionProgress` | Étape courante + compteurs, persisté en JSONB et diffusé par SignalR. |
| `ConversationSessionSummary` | Projection légère pour la liste latérale des conversations. |
| `TranscriptionResult` / `TranscriptionSegment` | Sortie de Whisper : texte complet, segments horodatés, durée, langue. |

### Interfaces — services

```
IRagPipelineService        Orchestre le pipeline RAG (AskAsync + AskStreamAsync)
IIngestionService          Extraction → chunking → embedding → persistance
IEmbeddingService          Génère les embeddings (EmbeddingTaskType obligatoire)
IVectorStoreService        Recherche vectorielle, lexicale et full-text dans pgvector
ITextExtractor             Extrait le texte brut d'un fichier
IPageAwareTextExtractor    Variante conservant la pagination (PDF, PPTX)
ICompositeTextExtractor    Délègue au bon extracteur selon le type de contenu
IContentTypeResolver       Résout le type de contenu d'un fichier
ITextChunker               Découpe le texte avec chevauchement
ITemporalChunker           Découpe des segments Whisper en respectant leurs frontières
ITextNomalize              Normalise le texte avant embedding
ILanguageDetectionService  Détecte la langue (heuristique par mots vides, sans dépendance)
IFileHashService           Empreinte de fichier pour la détection de doublons
IAdaptiveQueryRouter       Choisit la stratégie RAG pour une question
IHydeService               Génère un document hypothétique (stratégie HyDE)
IMultiQueryService         Génère des variantes de question (stratégie Fusion)
IRerankerService           Reclasse les chunks récupérés
IContextCompressorService  Compresse le contexte avant le prompt LLM
IIngestionNotifier         Notifie le front d'un changement de statut
IIngestionProgressReporter Publie l'avancement fin d'une ingestion
ITranscriptionService      Transcrit un audio en segments horodatés
IVideoProcessorService     Extrait la piste audio d'une vidéo, sonde sa durée
IUnitOfWork                Valide la transaction courante
```

### Interfaces — repositories

```
IDocumentRepository      Document + DocumentChunk
IConversationRepository  ConversationSession + ChatMessage + Citation
IOutboxRepository        File de travaux d'ingestion
```

### Enums

| Enum | Valeurs |
|------|---------|
| `RagStrategy` | `Direct`, `HyDE`, `Fusion`, `Adaptive`, `FullText`, `DirectLlm` |
| `IngestionStatus` | `Pending`, `Processing`, `Completed`, `Failed` |
| `IngestionStage` | `Queued`, `ExtractingAudio`, `Transcribing`, `ExtractingText`, `Chunking`, `Embedding`, `Storing`, `Completed`, `Failed` |
| `EmbeddingTaskType` | `Document`, `Query` — impose le bon préfixe pour un modèle asymétrique |
| `ChunkingStrategy` | `Recursive`, `SlidingWindow` |
| `MessageRole` | `System`, `User`, `Assistant` |
| `DocumentMatchType` | `ExactDuplicate`, `SameNameDifferentContent` |

> `DocumentMetadata` réside dans le dossier `Enums/` alors que c'est un record — placement historique.

---

## Couche Application

### CQRS maison

MediatR et FluentValidation ont été retirés au profit d'un dispatcher maison composé de maillons
— voir [ADR-012](docs/adr/012-cqrs-maison-composition.md).

```
ICommand<TResponse>  →  ICommandDispatcher  →  [ ValidationStep → LoggingStep → Handler ]
```

Seules les **écritures** passent par ce mécanisme. Les **lectures** appellent directement le
repository ou le service : le coût d'un dispatcher n'est pas justifié pour un `GetById`.

| Domaine | Commandes |
|---------|-----------|
| Document | `UploadDocumentCommand`, `IngestDocumentCommand`, `DeleteDocumentCommand`, `ReembedCorpusCommand` |
| Vidéo | `CreateVideoTranscriptionJobCommand`, `ProcessVideoTranscriptionJobCommand` |
| Conversation | `DeleteSessionCommand`, `DeleteAllSessionsCommand` |

La séparation création/traitement du côté vidéo est ce qui rend l'upload non bloquant : la première
commande crée le document et met le travail en file, la seconde est exécutée par le worker.

### Traitement en arrière-plan (`Jobs/`)

| Classe | Rôle |
|--------|------|
| `IngestionWorker` | `BackgroundService` : sonde `outbox_messages`, dispatche la commande correspondante |
| `IngestionSignal` | Réveille le worker immédiatement après une mise en file, sans attendre la sonde |
| `IngestionJobPayload` / `IngestionEventTypes` | Contrat sérialisé des messages d'outbox |
| `IngestionOptions` | Répertoire de travail, intervalle de sonde, taille de lot, tentatives |

Voir [ADR-002](docs/adr/002-ingestion-asynchrone-outbox.md) et
[ADR-008](docs/adr/008-workfilestore-fichiers-de-travail.md).

### Services

| Service | Rôle |
|---------|------|
| `IngestionService` | Coordonne extraction → chunking → normalisation → embedding → persistance |
| `RecursiveChunker` | Découpe récursive avec chevauchement, conserve les titres de section |
| `TemporalChunker` | Regroupe les segments Whisper sans jamais les couper ([ADR-006](docs/adr/006-chunking-temporel-video.md)) |
| `TextNormalizer` | Normalisation du texte avant embedding |
| `IngestionBatching` | Découpe les appels d'embedding en sous-lots |
| `FileHashService` | Empreinte de contenu ([ADR-007](docs/adr/007-detection-doublons-upload.md)) |
| `ContentTypeResolver` | Type de contenu à partir de l'extension et de l'en-tête |
| `StopwordLanguageDetectionService` | Détection de langue par mots vides (fr/en/es/de/it), sans dépendance externe |
| `DocumentIngestionStatusUpdater` | Point unique de transition de statut, partagé par les deux pipelines |

### Extraction de texte (`Services/TextExtractor/`)

`CompositeTextExtractor` dispatche vers l'extracteur adapté — voir
[ADR-011](docs/adr/011-extraction-texte-multi-format-composite.md).

| Extracteur | Formats | Bibliothèque |
|-----------|---------|--------------|
| `PdfTextExtractor` | PDF (paginé) | PdfPig |
| `DocxTextExtractor` | DOCX | DocumentFormat.OpenXml |
| `ExcelTextExtractor` | XLSX, CSV | ClosedXML + parseur RFC 4180 maison |
| `PowerPointTextExtractor` | PPTX (paginé par slide) | DocumentFormat.OpenXml |
| `HtmlTextExtractor` | HTML | AngleSharp |
| `PlainTextExtractor` | TXT, MD | — |
| `JsonTextExtractor` | JSON (aplatissement récursif) | — |

L'OCR des PDF scannés est **hors périmètre assumé**.

---

## Couche Infrastructure

### Pipeline RAG (`AI/Rag/`)

| Classe | Rôle |
|--------|------|
| `RagPipelineService` | Orchestrateur : stratégie → récupération → fusion hybride → reranking → compression → LLM → citations → persistance |
| `RagStrategyResolver` | Fonction pure de repli : ramène à `Direct` si la stratégie demandée est désactivée en configuration |
| `AdaptiveQueryRouter` | Choisit la stratégie en analysant la question |
| `HydeService` | Génère un document hypothétique à embedder |
| `MultiQueryService` | Génère N variantes de la question |
| `ReciprocalRankFusion` | Fusion par rang (RRF) de plusieurs listes de résultats |
| `LlmRerankerService` | Reranking en **un seul appel LLM batché** pour tous les extraits |
| `BatchedRerankResponseParser` | Parseur pur de la réponse du reranker, tolérant au bruit des petits modèles |
| `ContextCompressorService` | Compression du contexte avant le prompt (désactivée par défaut) |
| `RagPrompts` | Templates de prompts |

#### Étapes du pipeline

```
1. Résolution de stratégie
   └─ Adaptive → IAdaptiveQueryRouter, puis RagStrategyResolver.ResolveFallback

2. Récupération des chunks
   ├─ Direct : embed la question
   ├─ HyDE   : génère un document hypothétique, puis l'embed
   └─ Fusion : génère N variantes, embed chacune, fusionne par RRF

3. Fusion hybride  (appliquée aux trois stratégies)
   └─ RRF entre le résultat vectoriel et la recherche lexicale, puis Take(TopK)

4. Reranking       (un appel LLM batché, repli sur le score cosinus en cas d'échec)

5. Compression du contexte  (optionnelle)

6. Construction du prompt
   └─ ChatHistory SK : prompt système + historique de la session + contexte

7a. Complétion bloquante   IChatClient.GetResponseAsync
7b. Complétion streaming   IChatClient.GetStreamingResponseAsync → IAsyncEnumerable<RagStreamChunk>

8. Citations + persistance de l'échange (messages et citations)
```

**Pourquoi la fusion hybride est au cœur du dispositif** : le score cosinus seul ne discrimine pas
suffisamment sur ce corpus — des questions totalement hors sujet obtiennent des scores comparables
à des chunks pertinents. La fusion **par rang** contourne ce problème là où aucun seuil de score ne
le peut. Analyse détaillée dans [PLAN-AMELIORATION-RAG.md](PLAN-AMELIORATION-RAG.md).

### Autres composants Infrastructure

| Dossier | Classes | Rôle |
|---------|---------|------|
| `AI/Embedding/` | `OpenAIEmbeddingService` | Embeddings, préfixes de tâche, résilience Polly |
| `AI/Transcription/` | `WhisperTranscriptionService` | Whisper.net, modèle chargé une seule fois ([ADR-005](docs/adr/005-transcription-video-locale-whisper.md)) |
| `AI/Video/` | `FFmpegVideoProcessorService` | Extraction audio WAV 16 kHz mono, sonde de durée |
| `VectorStore/` | `PgVectorStoreService`, `PostgresTextSearchConfig` | SQL brut : cosinus, recherche lexicale et full-text, upsert |
| `Persistence/` | `AppDbContext`, 3 repositories, 6 configurations EF | Accès aux données |
| `Notifications/` | `IngestionProgressReporter`, `NullIngestionNotifier` | Avancement persisté et diffusé |
| `Options/` | `AiProviderOptions`, `RagOptions`, `WhisperOptions` | Configuration typée |

---

## Couche Web.Api

### Endpoints

| Méthode | Route | Description |
|---------|-------|-------------|
| `GET` | `/api/documents` · `/api/documents/{id}` | Liste et détail |
| `POST` | `/api/documents` | Upload → **202 Accepted**, ingestion en arrière-plan |
| `POST` | `/api/documents/check-duplicate` | Détection doublon / nouvelle version avant envoi |
| `DELETE` | `/api/documents/{id}` | Supprime le document et ses chunks |
| `POST` | `/api/documents/reembed-corpus` | Ré-embed les chunks existants sans réextraction |
| `POST` | `/api/video/transcribe` | Upload vidéo/audio → **202 Accepted** |
| `GET` | `/api/video/{id}/transcription` | Transcription d'un document traité |
| `GET` | `/api/chat/system-prompts` | Prompts système par défaut |
| `GET` | `/api/chat/sessions` · `/api/chat/sessions/{id}` | Conversations : résumés et détail |
| `DELETE` | `/api/chat/sessions/{id}` · `/api/chat/sessions` | Suppression unitaire et purge |
| `POST` | `/api/chat/ask` | Question RAG, réponse complète |
| `POST` | `/api/chat/stream` | Question RAG, réponse en SSE token par token |

### Format SSE de `/api/chat/stream`

Flux `text/event-stream`, deux types d'événements :

```
event: token
data: {"token":"Bonjour"}

event: done
data: {"answer":"...","citations":[...],"strategyUsed":"Direct","totalTokens":0,"durationMs":1234,"sessionId":"..."}
```

Le client accumule les `token` pour l'affichage progressif, puis enrichit le message final avec les
citations et métriques reçues dans `done`. Détail complet et points critiques (`DisableBuffering`
côté serveur, `flushSync` côté client) dans [ADR-001](docs/adr/001-streaming-rag-reponse.md).

### Temps réel hors requête — SignalR

Le hub `/hubs/ingestion` diffuse deux événements distincts : `documentStatusChanged` (transitions de
statut, faible fréquence) et `documentProgressChanged` (avancement fin, haute fréquence, filtré par
document côté client). Repli en polling si le hub est indisponible —
voir [ADR-003](docs/adr/003-notification-signalr.md).

### Documentation interactive et CORS

En développement, l'API expose son schéma OpenAPI et l'interface Scalar sur
`http://localhost:5406/scalar/v1`. Les origines autorisées sont configurées dans
`appsettings.json → Cors.AllowedOrigins` ; `AllowCredentials()` est nécessaire au repli long-polling
de SignalR.

---

## Front-end React

Application **React 19 + TypeScript**, compilée par **Vite**. Détail des conventions et des pièges
dans [`src/Front/CLAUDE.md`](src/Front/CLAUDE.md).

| Route | Page | Description |
|-------|------|-------------|
| `/` | `DocumentsPage` | Upload et liste des documents |
| `/documents/:id` | `DocumentDetailPage` | Suivi étape par étape de l'ingestion |
| `/video` | `VideoPage` | Upload vidéo/audio et transcription |
| `/chat` · `/chat/:sessionId` | `ChatPage` | Chat RAG, 3 modes, reprise de conversation |

Toutes les requêtes passent par `src/api/client.ts` (`api.documents.*`, `api.video.*`, `api.chat.*`).
L'URL de base vient de `VITE_API_URL`, vide par défaut : en développement, le proxy Vite redirige
`/api` **et** `/hubs` vers `http://localhost:5406`.

`api.chat.askStream()` est un **générateur async** qui lit le `ReadableStream` de la réponse `fetch`,
découpe les blocs SSE (séparés par `\n\n`) et `yield` chaque `StreamEvent` typé.

---

## Décisions d'architecture

### Consignées dans un ADR

| Décision | ADR |
|----------|-----|
| Streaming de la réponse RAG en SSE sur POST | [001](docs/adr/001-streaming-rag-reponse.md) |
| Ingestion asynchrone via outbox + BackgroundService | [002](docs/adr/002-ingestion-asynchrone-outbox.md) |
| Notification de fin d'ingestion par SignalR + repli polling | [003](docs/adr/003-notification-signalr.md) |
| Progression d'upload via `XMLHttpRequest` plutôt que `fetch` | [004](docs/adr/004-upload-progression-xhr.md) |
| Transcription vidéo 100 % locale (Whisper.net + FFmpeg) | [005](docs/adr/005-transcription-video-locale-whisper.md) |
| Chunking temporel et propagation des timestamps | [006](docs/adr/006-chunking-temporel-video.md) |
| Détection de doublons à l'upload | [007](docs/adr/007-detection-doublons-upload.md) |
| Fichiers de travail durables (`WorkFileStore`) | [008](docs/adr/008-workfilestore-fichiers-de-travail.md) |
| Trois modes de restitution | [009](docs/adr/009-trois-modes-restitution-rag.md) |
| Schéma PostgreSQL par scripts SQL manuels | [010](docs/adr/010-schema-postgres-scripts-sql.md) |
| Extraction multi-format par extracteur composite | [011](docs/adr/011-extraction-texte-multi-format-composite.md) |
| CQRS maison en remplacement de MediatR | [012](docs/adr/012-cqrs-maison-composition.md) |

Voir [`docs/adr/README.md`](docs/adr/README.md) pour l'index commenté et les fils conducteurs entre ADR.

### Décisions structurantes sans ADR dédié

| Décision | Justification |
|----------|---------------|
| `IChatClient` (Microsoft.Extensions.AI) plutôt que le SDK OpenAI direct | Indépendance vis-à-vis du fournisseur : passer à Ollama, Azure OpenAI ou GitHub Models ne change que la configuration. |
| Semantic Kernel pour les appels structurés | Templates de prompts et gestion du `ChatHistory` sans les réécrire. |
| pgvector plutôt qu'une base vectorielle dédiée | Une seule base à opérer ; suffisant à cette échelle, et permet de fusionner recherche vectorielle et recherche lexicale dans la même requête. |
| CQRS pour les écritures uniquement | Les lectures sont des appels directs : la surcharge d'un dispatcher n'est pas justifiée pour un `GetById`. |
| Ne jamais relire une entité après écriture | La réponse d'un handler doit suffire à construire la réponse HTTP ; un repository injecté dans un contrôleur ne sert qu'aux vraies lectures. |
| Pattern Options pour toute la configuration | Fortement typé et testable. *Réserve : la validation au démarrage (`ValidateOnStart`) n'est pas encore branchée.* |
| `IUnitOfWork` encapsule `SaveChangesAsync` | Évite des `SaveChanges` dispersés dans les repositories. |
| Proxy Vite en développement | Supprime les problèmes de CORS et de certificat auto-signé sans modifier la configuration serveur. |
| `IAsyncEnumerable` dans `IRagPipelineService` | Le pipeline reste testable et découplé du transport HTTP. |
| Aucun framework de mock dans les tests | Faux écrits à la main et extraction de la logique en classes pures — pousse à un découpage testable plutôt qu'à des tests couplés à l'implémentation. |

---

## Limites connues de l'architecture

Ces points sont structurels, pas des bugs. Leur traitement est priorisé dans
[PLAN-AMELIORATION-RAG.md](PLAN-AMELIORATION-RAG.md) pour la qualité du RAG et dans
[DETTE-TECHNIQUE.md](DETTE-TECHNIQUE.md) pour le socle (sécurité, exploitation, tests).

- **Pas d'authentification** : l'`UserId` vient de la configuration (`DevAuth`), et l'isolation par
  utilisateur est absente de la chaîne de lecture — les requêtes pgvector et full-text ne filtrent
  pas sur `user_id`.
- **Pas de condensation de question multi-tour** : une question de suivi elliptique est embeddée
  telle quelle, ce qui dégrade la récupération dès le deuxième tour d'une conversation.
- **Originaux non conservés** : les fichiers sources sont supprimés après ingestion, ce qui interdit
  la consultation d'une source citée et impose un re-upload pour toute ré-ingestion.
- **Pas de budget de tokens en entrée** : rien ne borne la taille du prompt envoyé au LLM.
- **Schéma géré à la main** : toute évolution impose de modifier `init.sql` *et* d'ajouter un script
  `migrate-*.sql`, sans quoi une base neuve et une base existante divergent silencieusement.
- **Socle d'exploitation absent** : ni health check, ni validation des options au démarrage, ni CI.
