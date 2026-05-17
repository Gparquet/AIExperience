# Architecture — AIExperience.Rag

Ce document décrit l'architecture technique du projet **AIExperience.Rag**.  
Il est destiné aux développeurs, aux assistants IA (GitHub Copilot, Claude, Gemini, etc.) et à tout contributeur ayant besoin de comprendre le système avant d'effectuer des modifications.

---

## Couches de la Clean Architecture

La solution est organisée en quatre couches avec une **règle de dépendance vers l'intérieur** stricte.  
Une couche interne ne connaît jamais une couche externe.

```
Domain  ←  Application  ←  Infrastructure  ←  Console
```

### Responsabilités des couches

| Couche | Projet | Responsabilités | Peut référencer |
|--------|--------|----------------|-----------------|
| **Domain** | `AIExperience.Rag.Domain` | Entités, interfaces, enums, value objects. Zéro dépendance externe. | Rien |
| **Application** | `AIExperience.Rag.Application` | Cas d'usage (MediatR), extraction de texte, chunking, normalisation, validation. | Domain uniquement |
| **Infrastructure** | `AIExperience.Rag.Infrastructure` | OpenAI, pgvector, EF Core, Semantic Kernel, implémentations du pipeline RAG. | Domain + Application |
| **Console** | `AIExperience.App.Console` | Racine de composition DI, menu console interactif. | Toutes les couches |

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
| `DocumentConfiguration` | Mapping Fluent API pour `Document` |
| `DocumentChunkConfiguration` | Mapping Fluent API pour `DocumentChunk` (colonne vectorielle) |
| `CitationConfiguration` | Mapping Fluent API pour `Citation` |
| `ConversationSessionConfiguration` | Mapping Fluent API pour `ConversationSession` |
| `ChatMessageConfiguration` | Mapping Fluent API pour `ChatMessage` |

### Configuration (`Options/`)

| Classe | Liée depuis |
|--------|-------------|
| `AiProviderOptions` | `appsettings.json` → section `"AI"` |
| `RagOptions` | `appsettings.json` → section `"RagOptions"` |

Sous-options de `RagOptions` :

| Sous-option | Rôle |
|------------|------|
| `HydeOptions` | Activation HyDE + longueur du document hypothétique |
| `MultiQueryOptions` | Nombre de variantes de requête pour Fusion |
| `RetrievalOptions` | Top-K chunks, seuil de similarité |
| `RerankerOptions` | Paramètres du reranker cross-encoder |
| `ContextCompressionOptions` | Activation/désactivation de la compression de contexte |
| `CacheOptions` | Cache Redis des réponses |

---

## Hôte Console (`AIExperience.App.Console`)

`Program.cs` est la **racine de composition** :

1. Crée l'`IHost` via `Host.CreateApplicationBuilder`.
2. Appelle `services.AddInfrastructure(config).AddApplication()`.
3. Résout les services depuis le conteneur DI et exécute une boucle interactive.

Options du menu interactif :
- **1** — Ingérer des documents (appelle `UploadDocumentCommand` via MediatR)
- **2** — Poser une question (appelle `IRagPipelineService.AskAsync`)
- **3** — Charger des documents déjà ingérés
- **0** — Quitter

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

---

## Diagramme de flux de données

```
Utilisateur
 │  uploade un fichier
 ▼
UploadDocumentCommand (MediatR)
 │
 ▼
UploadDocumentHandler
 │
 ├─► ICompositeTextExtractor  →  texte brut
 │
 ├─► ITextNomalize            →  texte nettoyé
 │
 ├─► ITextChunker             →  List<TextChunk>
 │
 ├─► IEmbeddingService        →  float[] par chunk
 │
 ├─► IDocumentRepository      →  persistance Document + DocumentChunks
 │
 └─► IUnitOfWork.CommitAsync  →  commit de la transaction


Utilisateur
 │  pose une question
 ▼
IRagPipelineService.AskAsync
 │
 ├─► IAdaptiveQueryRouter      →  RagStrategy
 │
 ├─► IEmbeddingService         →  embedding de la question
 │
 ├─► IVectorStoreService       →  List<(DocumentChunk, score)>
 │
 ├─► IContextCompressorService →  chunks compressés
 │
 ├─► IConversationRepository   →  historique de chat
 │
 ├─► IChatClient               →  réponse du LLM
 │
 └─► Citation.Create(...)      →  RagResponse
```
