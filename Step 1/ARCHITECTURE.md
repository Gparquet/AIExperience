# Architecture — AIExperience.Rag

Ce document décrit l'architecture technique du projet **AIExperience.Rag**.  
Il est destiné aux développeurs, aux assistants IA (GitHub Copilot, Claude, Gemini, etc.) et à tout contributeur ayant besoin de comprendre le système avant d'effectuer des modifications.

---

## Structure des dossiers

```
Step 1/
├── docker-compose.yml
├── scripts/init.sql
└── src/
    ├── Back/                          ← Tous les projets C# / .NET
    │   ├── AIExperience.slnx
    │   ├── AIExperience.Rag.Domain/
    │   ├── AIExperience.Rag.Application/
    │   ├── AIExperience.Rag.Infrastructure/
    │   ├── AIExperience.App.Console/
    │   └── AIExperience.Web.Api/
    └── Front/                         ← Application React + TypeScript (Vite)
        ├── src/
        │   ├── api/client.ts          ← Client HTTP vers l'API REST
        │   ├── pages/                 ← DocumentsPage, ChatPage
        │   ├── components/            ← Composants réutilisables
        │   └── types/index.ts         ← Types TypeScript partagés
        ├── vite.config.ts
        └── package.json
```

---

## Couches de la Clean Architecture

La solution .NET est organisée en quatre couches avec une **règle de dépendance vers l'intérieur** stricte.  
Une couche interne ne connaît jamais une couche externe.

```
Domain  ←  Application  ←  Infrastructure  ←  Web.Api / Console
```

### Responsabilités des couches

| Couche | Projet | Responsabilités | Peut référencer |
|--------|--------|----------------|-----------------|
| **Domain** | `AIExperience.Rag.Domain` | Entités, interfaces, enums, value objects. Zéro dépendance externe. | Rien |
| **Application** | `AIExperience.Rag.Application` | Cas d'usage (MediatR), extraction de texte, chunking, normalisation, validation. | Domain uniquement |
| **Infrastructure** | `AIExperience.Rag.Infrastructure` | OpenAI, pgvector, EF Core, Semantic Kernel, implémentations du pipeline RAG. | Domain + Application |
| **Web.Api** | `AIExperience.Web.Api` | Racine de composition DI, controllers REST, CORS, OpenAPI (Scalar). | Toutes les couches |
| **Console** | `AIExperience.App.Console` | Racine de composition DI, menu console interactif. | Toutes les couches |

---

## API REST (`AIExperience.Web.Api`)

### Endpoints

| Méthode | Route | Description |
|---------|-------|-------------|
| `GET`    | `/api/documents`       | Liste tous les documents de l'utilisateur |
| `POST`   | `/api/documents`       | Upload + ingestion d'un PDF (multipart/form-data) |
| `GET`    | `/api/documents/{id}`  | Récupère un document par son id |
| `DELETE` | `/api/documents/{id}`  | Supprime un document et ses chunks |
| `POST`   | `/api/chat/ask`        | Pose une question RAG sur les documents sélectionnés |

### Documentation interactive

En environnement de développement, l'API expose :
- OpenAPI schema : `http://localhost:5000/openapi/v1.json`
- Scalar UI : `http://localhost:5000/scalar/v1`

### CORS

Configuré dans `appsettings.json → Cors.AllowedOrigins`.  
Par défaut : `http://localhost:5173` (Vite dev server).

---

## Frontend React (`src/Front/`)

Application **React 19 + TypeScript** compilée avec **Vite**.

### Pages

| Page | Route | Description |
|------|-------|-------------|
| `DocumentsPage` | `/` | Liste, upload et suppression de PDFs |
| `ChatPage` | `/chat` | Interface de chat RAG avec sélection de documents et affichage des citations |

### Client API (`src/api/client.ts`)

Toutes les requêtes passent par `api.documents.*` et `api.chat.*`.  
L'URL de base est configurable via `VITE_API_URL` (défaut : `http://localhost:5000`).

Le proxy Vite (`vite.config.ts`) redirige `/api/*` vers `http://localhost:5000` en développement.

### Lancer le frontend

```powershell
cd "Step 1/src/Front"
npm install
npm run dev   # http://localhost:5173
```

---

## Couche Domain (`AIExperience.Rag.Domain`)

### Entités

| Entité | Description |
|--------|-------------|
| `Document` | Fichier uploadé par un utilisateur. Suit le cycle de vie d'ingestion via `IngestionStatus`. |
| `DocumentChunk` | Fragment d'un document (texte + embedding pgvector). |
| `Citation` | Référence source incluse dans un `RagResponse` (id document, extrait, score, page). |
| `ConversationSession` | Session de chat multi-tour liée à un utilisateur. |
| `ChatMessage` | Message unique dans une conversation (rôle + contenu + horodatage). |

Toutes les entités utilisent des **setters privés** et des **méthodes factory statiques** (`Entity.Create(...)`).

### Interfaces

**Services :**

```
IRagPipelineService       — Orchestre le pipeline RAG complet (AskAsync)
IIngestionService         — Ingère un document : extraction → chunking → embedding → persistance
IEmbeddingService         — Génère des embeddings float[] à partir d'un texte
IVectorStoreService       — Recherche sémantique dans pgvector
ITextExtractor            — Extrait le texte brut d'un fichier (PDF, HTML…)
ICompositeTextExtractor   — Délègue au bon ITextExtractor selon le type MIME
ITextChunker              — Découpe le texte en chunks avec chevauchement
ITextNomalize             — Normalise le texte avant embedding
IAdaptiveQueryRouter      — Choisit la stratégie RAG pour une question donnée
IContextCompressorService — Compresse les chunks récupérés pour réduire la taille du contexte LLM
IUnitOfWork               — Commit la transaction courante
```

**Repositories :**

```
IDocumentRepository       — CRUD pour Document + DocumentChunk
IConversationRepository   — CRUD pour ConversationSession + ChatMessage
```

### Enums

| Enum | Valeurs |
|------|---------|
| `RagStrategy` | `Direct`, `HyDE`, `Fusion`, `Adaptive` |
| `IngestionStatus` | `Pending`, `Processing`, `Completed`, `Failed` |
| `ChunkingStrategy` | `Recursive` (extensible) |
| `MessageRole` | `User`, `Assistant`, `System` |

---

## Couche Application (`AIExperience.Rag.Application`)

### CQRS avec MediatR

Toute opération utilisateur en écriture est modélisée comme un `IRequest<T>` :

```
UploadDocumentCommand  →  UploadDocumentHandler  →  UploadDocumentResponse
```

Un behavior de pipeline `ValidationBehavior<TRequest, TResponse>` exécute **FluentValidation** avant chaque handler.

### Services

| Service | Description |
|---------|-------------|
| `IngestionService` | Coordonne extraction → chunking → normalisation → embedding → persistance |
| `RecursiveChunker` | Implémente `ITextChunker` — découpe le texte récursivement avec chevauchement |
| `TextNormalizer` | Implémente `ITextNomalize` — mise en minuscules, suppression de la ponctuation, etc. |
| `PdfTextExtractor` | Extrait le texte des fichiers PDF via PdfPig |
| `HtmlTextExtractor` | Extrait le texte des fichiers HTML |
| `CompositeTextExtractor` | Sélectionne le bon extracteur selon le `ContentType` |

---

## Couche Infrastructure (`AIExperience.Rag.Infrastructure`)

### Pipeline RAG (`AI/Rag/`)

| Classe | Rôle |
|--------|------|
| `RagPipelineService` | Orchestrateur principal : résolution de stratégie → récupération → compression → LLM → citations |
| `AdaptiveQueryRouter` | Analyse la complexité de la requête via SK et retourne la meilleure `RagStrategy` |
| `ContextCompressorService` | Utilise SK pour compresser les chunks récupérés avant de construire le prompt LLM |
| `RagPrompts` | Templates de prompts statiques utilisés par les fonctions SK |

#### Étapes du pipeline RAG (dans `RagPipelineService.AskAsync`)

```
1. Résolution de stratégie
   └─ Si Adaptive : appel à IAdaptiveQueryRouter.GetRagStrategyAsync
   └─ Sinon :       utilisation de query.Strategy directement

2. Récupération des chunks (RetrieveChunksAsync)
   └─ Direct :  embed la question → IVectorStoreService.SearchAsync
   └─ HyDE :    génère un doc hypothétique → embed → IVectorStoreService.SearchAsync  [TODO]
   └─ Fusion :  génère N variantes → N recherches → fusion RRF                        [TODO]

3. Compression du contexte (optionnel, piloté par RagOptions.ContextCompression.Enabled)
   └─ IContextCompressorService.CompressAsync

4. Construction du prompt (BuildChatHistoryAsync)
   └─ Charge l'historique de conversation depuis IConversationRepository
   └─ Construit le ChatHistory SK : prompt système + messages précédents + contexte compressé

5. Complétion LLM
   └─ IChatClient.GetResponseAsync (abstraction Microsoft.Extensions.AI)

6. Construction des citations
   └─ Associe chaque chunk classé → Citation.Create(...)

7. Retourne RagResponse { Answer, Citations, StrategyUsed, TotalTokens }
```

### Clients IA (`AI/Embedding/`)

| Classe | Rôle |
|--------|------|
| `OpenAIEmbeddingService` | Appelle Azure OpenAI / OpenAI pour produire des embeddings `float[]` |

### Vector store (`VectorStore/`)

| Classe | Rôle |
|--------|------|
| `PgVectorStoreService` | Exécute une recherche par similarité cosinus sur la table PostgreSQL avec pgvector |

### Persistance (`Persistence/`)

| Classe | Rôle |
|--------|------|
| `AppDbContext` | DbContext EF Core 10 avec colonnes pgvector |
| `DocumentRepository` | Implémentation EF Core de `IDocumentRepository` |
| `ConversationRepository` | Implémentation EF Core de `IConversationRepository` |

### Configuration (`Options/`)

| Classe | Liée depuis |
|--------|-------------|
| `AiProviderOptions` | `appsettings.json` → section `"AI"` |
| `RagOptions` | `appsettings.json` → section `"RagOptions"` |

---

## Décisions d'architecture clés

| Décision | Justification |
|----------|---------------|
| `IChatClient` (MEA) plutôt que le SDK OpenAI direct | Indépendant du fournisseur ; facile de passer à Ollama, Mistral, etc. |
| Semantic Kernel pour les appels structurés | Templates de prompts, function calling, support du pipeline SK. |
| pgvector plutôt qu'une base vectorielle dédiée | Stack simplifié (une seule base de données) ; suffisant pour une échelle modérée. |
| MediatR CQRS uniquement pour les commandes | Les requêtes sont des appels directs ; la surcharge MediatR n'est pas justifiée pour les lectures. |
| Pattern Options pour toute la configuration | Fortement typé, validé au démarrage, facilement testable. |
| `IUnitOfWork` encapsule `SaveChangesAsync` | Évite les appels `SaveChanges` dispersés dans les repositories. |
| Proxy Vite en développement | Évite les problèmes CORS en dev sans changer la config du serveur. |
