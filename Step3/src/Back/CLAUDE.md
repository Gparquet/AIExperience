# CLAUDE.md — Step 3, back-end C# .NET

Contexte essentiel pour les assistants IA travaillant sur le back-end.
Les règles de comportement et les conventions transverses sont définies dans le
[CLAUDE.md racine](../../../CLAUDE.md) — ce fichier ne couvre que le back-end.

---

## Vue d'ensemble

Système RAG en **.NET 10**, avec ingestion **asynchrone** multi-format (documents bureautiques,
web, données, vidéo/audio) et restitution en **trois modes**.

Deux caractéristiques structurent tout le reste :

1. **L'ingestion ne se fait jamais dans la requête HTTP.** Un upload renvoie `202 Accepted` ; le
   travail réel est mis en file dans la table `outbox_messages` et exécuté par un `BackgroundService`.
2. **La récupération est hybride.** Chaque recherche vectorielle est fusionnée avec une recherche
   lexicale PostgreSQL par *Reciprocal Rank Fusion* — c'est ce qui sépare réellement le signal du
   bruit sur ce corpus, le score cosinus seul n'étant pas assez discriminant avec le modèle
   d'embedding utilisé.

---

## Structure des projets

```
src/Back/AIExperience.slnx
├── AIExperience.Rag.Domain/          ← Entités, interfaces, enums, modèles — zéro dépendance
├── AIExperience.Rag.Application/     ← CQRS maison, handlers, chunkers, extracteurs, worker
├── AIExperience.Rag.Infrastructure/  ← Pipeline RAG, EF Core, pgvector, Whisper, FFmpeg
├── AIExperience.Web.Api/             ← API REST + hub SignalR (racine de composition DI)
├── AIExperience.App.Console/         ← Interface console alternative
└── AIExperience.Tests/               ← Tests xUnit + FluentAssertions
```

---

## Commandes essentielles

```powershell
# Depuis le répertoire Step3

docker-compose up -d                              # PostgreSQL 17 + pgvector, port 5433

dotnet build src/Back/AIExperience.slnx
dotnet test  src/Back/AIExperience.slnx
dotnet run --project src/Back/AIExperience.Web.Api
# → http://localhost:5406            API REST
# → https://localhost:7405           API REST (HTTPS)
# → http://localhost:5406/scalar/v1  documentation interactive Scalar

dotnet run --project src/Back/AIExperience.App.Console   # alternative sans front

dotnet run --project src/Back/AIExperience.Eval -- run       # harnais d'évaluation RAG (recall@k + fidélité)
dotnet run --project src/Back/AIExperience.Eval -- compare <run-a.json> <run-b.json>   # avant/après
```

---

## CQRS maison — remplace MediatR

MediatR et FluentValidation **ont été retirés du projet**. Un dispatcher maison, plus petit et sans
dépendance externe, les remplace. Voir [ADR 012](../../docs/adr/012-cqrs-maison-composition.md).

### Les contrats (`Application/Common/Cqrs/`)

| Type | Rôle | Équivalent MediatR |
|------|------|--------------------|
| `ICommand<TResponse>` | Marque une commande d'écriture | `IRequest<T>` |
| `ICommandHandler<TCommand, TResponse>` | Logique métier pure | `IRequestHandler<,>` |
| `ICommandDispatcher` | Point d'entrée unique (`SendAsync`) | `ISender` |
| `ICommandValidator<TCommand>` | Règles de validation d'une commande | `AbstractValidator<T>` |
| `ICommandPipelineStep<TCommand, TResponse>` | Maillon transverse, modèle middleware | `IPipelineBehavior<,>` |

Les maillons livrés sont `ValidationStep` (accumule les erreurs dans `ValidationErrors`, lève
`CommandValidationException`) et `LoggingStep`. Une `CommandValidationException` est traduite en
`400 ValidationProblemDetails` par `CommandValidationExceptionHandler`, enregistré dans `Program.cs`.

### Écrire une nouvelle commande

1. `MaCommande.cs` — record implémentant `ICommand<MaReponse>`.
2. `MaCommandeHandler.cs` — implémente `ICommandHandler<MaCommande, MaReponse>`, **logique métier seule**.
3. *(optionnel)* `MaCommandeValidator.cs` — implémente `ICommandValidator<MaCommande>`.
4. Enregistrement automatique par assembly scanning (`CqrsServiceCollectionExtensions`).

**Rappel** : les **lectures** n'utilisent pas ce mécanisme — le contrôleur appelle directement le
repository ou le service.

---

## Ingestion asynchrone — le cœur du back-end

Voir [ADR 002](../../docs/adr/002-ingestion-asynchrone-outbox.md) et
[ADR 003](../../docs/adr/003-notification-signalr.md).

```
POST /api/documents  (ou /api/video/transcribe)
   │
   ├─ écrit le fichier dans un répertoire de travail durable (WorkFileStore)
   ├─ crée l'entité Document (statut Queued)
   ├─ insère un message dans outbox_messages       ← même transaction : atomicité garantie
   └─ renvoie 202 Accepted immédiatement
                    │
IngestionWorker (BackgroundService)
   ├─ sonde outbox_messages toutes les 3 s, réveil immédiat via IngestionSignal
   ├─ dispatche IngestDocumentCommand ou ProcessVideoTranscriptionJobCommand
   ├─ publie l'avancement par étape via IIngestionProgressReporter (persisté en JSONB, throttlé)
   └─ notifie le front via IIngestionNotifier → hub SignalR /hubs/ingestion
```

**Pourquoi l'outbox plutôt qu'une file en mémoire** : la mise en file est écrite dans la même
transaction que la création du document, et une reprise après crash devient un simple effet de bord
de la sonde périodique — pas un mécanisme de reprise à écrire séparément.

**Deux pipelines distincts** (document / vidéo) partagent le worker, le `DocumentIngestionStatusUpdater`
et `IngestionSignal`. Ils restent deux commandes séparées : le pipeline vidéo commence par une
extraction audio et une transcription que le pipeline document n'a pas.

### Suivi fin de l'avancement

L'enum `IngestionStage` (`Queued`, `ExtractingAudio`, `Transcribing`, `ExtractingText`, `Chunking`,
`Embedding`, `Storing`, `Completed`, `Failed`) est persisté avec ses compteurs dans
`documents.ingestion_progress` (JSONB) et poussé en direct par l'événement SignalR
`documentProgressChanged` — distinct de `documentStatusChanged`, pour ne pas imposer une cadence
élevée à la liste des documents.

---

## Extraction de texte — 9 formats

`CompositeTextExtractor` dispatche sur l'extracteur adapté au type de contenu :

| Format | Extracteur | Particularité |
|--------|-----------|---------------|
| PDF | `PdfTextExtractor` (PdfPig) | Pagination conservée (`IPageAwareTextExtractor`) |
| DOCX | `DocxTextExtractor` (OpenXml) | |
| XLSX / CSV | `ExcelTextExtractor` (ClosedXML) | Parseur CSV RFC 4180 maison |
| PPTX | `PowerPointTextExtractor` | Pagination par slide |
| HTML | `HtmlTextExtractor` (AngleSharp) | |
| TXT / MD | `PlainTextExtractor` | |
| JSON | `JsonTextExtractor` | Aplatissement récursif |
| Vidéo / audio | FFmpeg → Whisper | Chemin séparé, voir ci-dessous |

**OCR : hors périmètre** (décision assumée) — un PDF scanné sans couche texte ne produit rien.

⚠️ Le validateur d'upload (`UploadDocumentValidator`) et la liste `accept` du front doivent rester
cohérents avec cette table : un format ajouté ici sans être autorisé là est rejeté avant d'arriver
à l'extracteur.

---

## Pipeline vidéo — transcription 100 % locale

```
ProcessVideoTranscriptionJobCommand
  ├─ 1. FFmpegVideoProcessorService.ExtractAudioAsync()   → WAV 16 kHz mono
  ├─ 2. WhisperTranscriptionService.TranscribeAsync()     → segments horodatés
  │      (singleton, modèle .bin chargé une seule fois, callback de progression par segment)
  ├─ 3. (optionnel) nettoyage du texte par le LLM
  └─ 4. IngestionService.IngestFromSegmentsAsync()
         → TemporalChunker : accumule les segments jusqu'à ~1400 caractères
```

**Point important sur `TemporalChunker`** ([ADR 006](../../docs/adr/006-chunking-temporel-video.md)) :
le contenu du chunk est du **texte pur**. Les horodatages vivent dans les propriétés `StartTime` /
`EndTime`, jamais dans le texte — les inclure polluait l'embedding et effondrait la similarité
question/chunk sur les vidéos. Le chunker ne coupe jamais un segment en deux, chevauche un segment
entre chunks consécutifs et scinde les segments surdimensionnés.

**Prérequis manuels** : le modèle Whisper (`ggml-medium.bin`, ~1,5 Go, à télécharger depuis
Hugging Face `ggerganov/whisper.cpp`) et FFmpeg installé sur la machine. Les deux chemins sont
configurés dans `appsettings.json`.

---

## Pipeline RAG — `RagPipelineService`

C'est le fichier le plus dense du projet. Il porte les trois modes et les quatre stratégies.

### Les trois modes ([ADR 009](../../docs/adr/009-trois-modes-restitution-rag.md))

| Condition | Mode | Comportement |
|-----------|------|-------------|
| `UseLlm = false` | **Full-text** | Recherche PostgreSQL seule, chunks bruts retournés. Ni embedding ni LLM. |
| `UseRag = false` | **LLM direct** | Question envoyée au LLM sans récupération documentaire. |
| Les deux `true` | **RAG complet** | Pipeline complet ci-dessous. |

### Le pipeline complet

```
1. Résolution de stratégie   AdaptiveQueryRouter (si Adaptive), puis RagStrategyResolver.ResolveFallback
                             → repli sur Direct si la stratégie résolue est désactivée en configuration
2. Récupération              Direct : embed question
                             HyDE   : génère un document hypothétique, puis l'embed
                             Fusion : génère N variantes, embed chacune, fusionne par RRF
3. Fusion hybride            FuseWithFullTextAsync : RRF entre le résultat vectoriel et
                             SearchLexicalAsync (to_tsquery en OR), puis Take(TopK)
4. Reranking                 LlmRerankerService — un SEUL appel LLM batché pour tous les extraits
5. Compression de contexte   optionnelle, désactivée par défaut
6. Complétion LLM            IChatClient, avec l'historique de conversation si SessionId présent
7. Citations + persistance   Citation.Create, puis PersistExchangeAsync (messages + citations)
```

**Sur le reranker** : `LlmRerankerService` envoie un prompt unique listant tous les extraits numérotés
et attend des lignes `numéro:score`. `BatchedRerankResponseParser` est un parseur pur (regex, clamp
0-10) tolérant au bruit des petits modèles ; si un extrait manque de la réponse ou si l'appel échoue,
le repli est le score cosinus. Ce découpage rend le parsing testable sans mock du LLM.

**Sur les embeddings** : `nomic-embed-text-v1.5` est un modèle **asymétrique**. `IEmbeddingService`
prend donc un `EmbeddingTaskType` **obligatoire** (`Document` / `Query`) et `OpenAIEmbeddingService`
préfixe le texte de `search_document:` ou `search_query:` selon le cas. Changer ce comportement
**impose une ré-ingestion complète du corpus**.

### Historique de conversation

`ResolveSessionAsync` crée ou réutilise la session, `PersistExchangeAsync` enregistre messages et
citations à chaque tour. Les deux ne sont appelés que depuis `AskAsync` / `AskStreamAsync`, jamais
depuis les helpers privés — c'est ce qui garantit un seul point de persistance.

> ✅ **Condensation de question multi-tour** : une question de suivi elliptique (« et pour les week-ends ? ») est désormais
> réécrite en question autonome avant d'irriguer la récupération (routage, HyDE, multi-query,
> recherche lexicale, reranking, compression), via `QueryCondensationService` (appelé par
> `CondenseQueryAsync` dans `RunRagPipelineAsync`/`AskStreamAsync`, juste après `ResolveSessionAsync`
> et avant la résolution de stratégie). La question originale reste seule utilisée pour le prompt
> final et la persistance. Pas de condensation au premier tour (pas d'historique) ni si
> `RagOptions.Condensation.Enabled = false` ; repli sur la question brute si l'appel LLM échoue ou si
> sa réponse est inexploitable (`CondensationResponseCleaner`).

> ⚠️ `Citation.SectionTitle`, `ChunkIndex`, `StartTime` et `EndTime` sont `[NotMapped]` : une
> conversation **rechargée depuis l'historique** affiche des citations sans section ni horodatage vidéo.

---

## Endpoints REST

### `ChatController` — `/api/chat`

| Méthode | Route | Rôle |
|---------|-------|------|
| `GET` | `/system-prompts` | Prompts système par défaut, pour éviter leur duplication côté front |
| `GET` | `/sessions` | Résumés des conversations de l'utilisateur (sidebar) |
| `GET` | `/sessions/{id}` | Détail d'une conversation : titre + messages + citations |
| `DELETE` | `/sessions/{id}` | Supprime une conversation (`204`, ou `404` si absente/autre utilisateur) |
| `DELETE` | `/sessions` | Supprime toutes les conversations de l'utilisateur |
| `POST` | `/ask` | Question RAG bloquante |
| `POST` | `/stream` | Même chose en streaming SSE ([ADR 001](../../docs/adr/001-streaming-rag-reponse.md)) |

`AskQuestionRequest` porte `Question`, `DocumentIds`, `Strategy`, `UseLlm`, `UseRag`,
`SystemPrompt` et `SessionId` (null = nouvelle conversation). `AskQuestionResponse` renvoie
toujours le `SessionId` utilisé.

### `DocumentsController` — `/api/documents`

| Méthode | Route | Rôle |
|---------|-------|------|
| `GET` | `/` · `/{id}` | Liste et détail (lecture directe du repository) |
| `POST` | `/check-duplicate` | Détection doublon/nouvelle version avant upload ([ADR 007](../../docs/adr/007-detection-doublons-upload.md)) |
| `POST` | `/` | Upload → **202 Accepted** |
| `DELETE` | `/{id}` | Suppression |
| `POST` | `/reembed-corpus` | Ré-embed les chunks existants **sans réextraction** |

### `VideoController` — `/api/video`

| Méthode | Route | Rôle |
|---------|-------|------|
| `POST` | `/transcribe` | Upload vidéo/audio → **202 Accepted** |
| `GET` | `/{id}/transcription` | Transcription d'un document vidéo déjà traité |

---

## Configuration

```json
{
  "ConnectionStrings": { "Postgres": "Host=localhost;Port=5433;..." },
  "AI": {
    "Provider": "OpenAI",              // AzureOpenAI | OpenAI | Ollama | GitHubModels
    "Endpoint": "http://localhost:1234/v1",
    "ChatModel": "llama-3.2-3b-instruct",
    "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
    "EmbeddingTaskPrefixes": true      // préfixes search_document:/search_query:
  },
  "RagOptions": {
    "DefaultStrategy": "Adaptive",
    "HyDE":       { "Enabled": true },
    "MultiQuery": { "Enabled": true, "VariantCount": 3 },
    "Retrieval":  { "TopK": 10, "ScoreThreshold": 0.5 },
    "Reranker":   { "Enabled": true, "TopKAfterRerank": 5 },
    "ContextCompression": { "Enabled": false },
    "Cache":      { "Enabled": true },
    "Condensation": { "Enabled": true }
  },
  "Whisper":   { "ModelPath": "...ggml-medium.bin", "Threads": 8 },
  "FFmpeg":    { "BinaryPath": "..." },
  "Ingestion": { "WorkDirectory": "App_Data/ingestion-work", "PollIntervalSeconds": 3 },
  "DevAuth":   { "DefaultUserId": "..." }
}
```

**Pièges de configuration à connaître :**

- `Cache.Enabled = true` **ne fait rien** : la section existe, l'implémentation n'a jamais été écrite.
- `ScoreThreshold` n'est **pas** un garde-fou de pertinence fiable ici : des questions totalement
  hors sujet obtiennent le même score cosinus que des chunks pertinents (le modèle compresse toutes
  les similarités dans une bande étroite). Ne pas compter dessus pour détecter « aucun document pertinent ».
- La clé d'API et les chemins Whisper/FFmpeg sont **en clair dans le fichier versionné** — à basculer
  en user-secrets.
- Aucune option n'est validée au démarrage : un `ModelPath` erroné ne se voit qu'à la première
  transcription.

---

## Base de données

Schéma géré par `scripts/init.sql` + scripts de migration appliqués **à la main**
([ADR 010](../../docs/adr/010-schema-postgres-scripts-sql.md)) :

```
init.sql                          schéma complet
migrate-temporal-chunks.sql       colonnes start/end_time_seconds
migrate-lot1-fulltext.sql         colonne content_tsv + index GIN
migrate-content-hash.sql          détection de doublons
migrate-i6-language-fts.sql       tsvector par langue détectée
migrate-lot2ter-async-ingestion.sql   outbox + statuts d'ingestion
migrate-ingestion-progress.sql    colonne ingestion_progress (JSONB)
```

**Toute évolution de schéma implique de modifier `init.sql` ET d'ajouter un `migrate-*.sql`** —
sinon une base neuve et une base existante divergent silencieusement.

`content_tsv` n'est **pas** une colonne générée : `to_tsvector(regconfig, text)` est `STABLE` et non
`IMMUTABLE`, ce qui interdit `GENERATED` dès lors que la langue varie. Elle est donc calculée à
l'écriture par le code.

---

## Tests

xUnit + FluentAssertions, 37 fichiers dans `AIExperience.Tests`. La ligne de conduite : **aucun
framework de mock n'est utilisé**. Les tests s'appuient sur des faux écrits à la main (`FakeUnitOfWork`,
`FakeOutboxRepository`…) et sur l'extraction de la logique testable en classes pures
(`BatchedRerankResponseParser`, `RagStrategyResolver`, `IngestionBatching`, `PostgresTextSearchConfig`).

**Quand vous ajoutez une méthode à une interface Domain, pensez aux faux des tests** : ils ne
compilent plus et doivent recevoir une implémentation no-op.

**Non couvert** : `PgVectorStoreService` (SQL brut, RRF, tsvector — candidat naturel à Testcontainers)
et la chaîne HTTP complète (`WebApplicationFactory`).

---

## Stack technique

| Package | Version | Usage |
|---------|---------|-------|
| .NET / ASP.NET Core | 10.0 | Framework, API REST, SignalR |
| Entity Framework Core + Npgsql | 10 | ORM PostgreSQL |
| Pgvector.EntityFrameworkCore | 0.3.0 | Recherche vectorielle |
| Microsoft.Extensions.AI | 10.5.0 | Abstraction `IChatClient` indépendante du provider |
| Microsoft.SemanticKernel | 1.74.0 | `ChatHistory`, templates de prompts |
| Microsoft.Extensions.Resilience | 10.7.0 | Politiques de retry sur les appels d'embedding |
| OllamaSharp | 5.4.25 | Modèles locaux Ollama |
| PdfPig · DocumentFormat.OpenXml · ClosedXML · AngleSharp | — | Extraction multi-format |
| Whisper.net (+ Runtime) · FFMpegCore | 1.x · 5.x | Transcription locale, extraction audio |
| Scalar.AspNetCore | 2.6.0 | Documentation interactive |
| xunit · FluentAssertions | 2.9.3 · 6.12.0 | Tests |

> `MediatR` et `FluentValidation` **ne font plus partie du projet**.
> `Microsoft.OpenApi` 2.0.0 remonte un avertissement de vulnérabilité connue (NU1903) au build.

---

## Fichiers clés

| Fichier | Rôle |
|---------|------|
| `AIExperience.Web.Api/Program.cs` | Composition DI, CORS, SignalR, gestion d'exceptions |
| `AIExperience.Rag.Infrastructure/DependencyInjection.cs` | Enregistrement de tous les services Infrastructure |
| `AIExperience.Rag.Application/DependencyInjection.cs` | Chunkers, extracteurs, scan CQRS |
| `AIExperience.Rag.Application/Common/Cqrs/` | Le dispatcher maison et ses maillons |
| `AIExperience.Rag.Application/Jobs/IngestionWorker.cs` | `BackgroundService` de traitement de la file |
| `AIExperience.Rag.Application/Services/IngestionService.cs` | Extraction → chunking → embedding → stockage |
| `AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs` | Cœur du pipeline RAG, 3 modes, 4 stratégies |
| `AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs` | SQL brut : cosinus, full-text, upsert |
| `AIExperience.Rag.Infrastructure/Persistence/AppDbContext.cs` | DbContext EF Core |
| `AIExperience.Web.Api/Hubs/IngestionHub.cs` | Hub SignalR `/hubs/ingestion` |
| `scripts/init.sql` | Schéma PostgreSQL complet |

---

## Dette technique connue — ne pas « redécouvrir »

| Sujet | État |
|-------|------|
| Budget de tokens en entrée du LLM | Spécifié, **non implémenté** — troncature silencieuse possible |
| Cache de réponses | Configuration présente, **code absent** |
| Stockage durable des originaux | **Absent** — les fichiers sources sont supprimés après ingestion, donc pas de consultation ni de ré-ingestion sans re-upload |
| Authentification | `UserId` fixe via `DevAuth`, **et aucun filtre `user_id` dans la recherche vectorielle / full-text** |
| Health checks, `ValidateOnStart`, CI | **Absents** |
| Rate limiting sur `/api/chat` | **Absent** — une question déclenche jusqu'à 5-8 appels LLM |
| Validation d'upload | Par extension, pas par signature de fichier ; `[DisableRequestSizeLimit]` sur la vidéo |
| Prompt injection via documents ingérés | **Aucune mitigation** — ne jamais brancher d'outils sur ce pipeline en l'état |
| OCR (PDF scannés) | Hors périmètre assumé |

Ce tableau est un résumé. Les listes de référence :
[PLAN-AMELIORATION-RAG.md](../../PLAN-AMELIORATION-RAG.md) pour la qualité du RAG,
[DETTE-TECHNIQUE.md](../../DETTE-TECHNIQUE.md) pour le socle (sécurité, exploitation, tests).
