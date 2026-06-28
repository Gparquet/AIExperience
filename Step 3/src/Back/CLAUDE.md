# CLAUDE.md — AIExperience Step 3 (Back-end C# .NET)

Contexte essentiel pour les assistants IA travaillant sur le back-end de Step 3.

---

## Rôle et comportement de l'assistant

Tu es un **leader technique senior** spécialisé en **.NET / Intelligence Artificielle**.

### Règles impératives — à respecter sans exception

1. **Langue** : toutes les réponses et tous les commentaires de code sont rédigés **en français**, sans exception.
2. **Mode de réponse** : structurer la réponse sous forme de **plan** (étapes numérotées, sections claires) avant d'expliquer ou de coder.
3. **Commentaires dans le code** : **tout le code produit doit être commenté** — chaque classe, méthode, bloc logique non trivial reçoit un commentaire XML (`///` en C#).
4. **Posture** : adopter le point de vue d'un tech lead — proposer des solutions robustes, scalables, respectueuses de la Clean Architecture et des conventions du projet, et signaler les risques ou dettes techniques identifiés.

---

## Vue d'ensemble — Step 3

**Système RAG (Retrieval-Augmented Generation) enrichi de capacités vidéo.**

Step 3 conserve tous les éléments des steps précédentes (ingestion de documents, chunking, embeddings, recherche vectorielle, LLM avec citations) et **ajoute un pipeline complet de transcription vidéo/audio** :

- **Transcription locale** via [Whisper.net](https://github.com/sandrohanea/whisper.net) (FFmpeg + whisper.cpp, 100% on-premise, aucun appel cloud)
- **Timestamps temporels** propagés à travers chunking → embeddings → citations
- **3 modes de fonctionnement RAG** : Full-text (PostgreSQL sans embeddings), LLM direct (sans récupération), RAG complet (pipeline 7 étapes)
- **Tests unitaires** avec xUnit + FluentAssertions (nouveau en Step 3)

---

## Structure des projets

```
Step 3/src/Back/AIExperience.slnx
├── AIExperience.Rag.Domain/                    ← Entités, interfaces, enums
├── AIExperience.Rag.Application/               ← CQRS (MediatR), services, chunkers
├── AIExperience.Rag.Infrastructure/            ← Pipeline RAG, DB, AI services
├── AIExperience.Web.Api/                       ← API REST ASP.NET Core
├── AIExperience.App.Console/                   ← Interface console alternative
└── AIExperience.Tests/                         ← Tests xUnit (NOUVEAU Step 3)
```

---

## Commandes essentielles

```powershell
# Dans le répertoire Step 3

# Démarrer PostgreSQL (port 5433 — différent de Step 1)
docker-compose up -d

# Compiler la solution
dotnet build "src/Back/AIExperience.slnx"

# Lancer l'API Web (HTTP: 50406, HTTPS: 50405)
dotnet run --project "src/Back/AIExperience.Web.Api"
# → API REST : http://localhost:50406
# → Documentation interactive Scalar : http://localhost:50406/scalar/v1

# Lancer l'application console (alternative sans front-end)
dotnet run --project "src/Back/AIExperience.App.Console"

# Exécuter tous les tests
dotnet test "src/Back/AIExperience.slnx"

# Exécuter les tests du projet Test spécifiquement
dotnet test "src/Back/AIExperience.Tests/AIExperience.Tests.csproj"
```

---

## Architecture — Clean Architecture en 4 couches

```
Domain ← Application ← Infrastructure ← Web.Api / Console
```

| Couche | Projet | Peut référencer |
|--------|--------|----------------|
| Domain | `AIExperience.Rag.Domain` | Rien (zéro dépendance externe) |
| Application | `AIExperience.Rag.Application` | Domain uniquement |
| Infrastructure | `AIExperience.Rag.Infrastructure` | Domain + Application |
| Web.Api | `AIExperience.Web.Api` | Toutes les couches (racine de composition DI) |

**Règle absolue :** une couche interne ne connaît JAMAIS une couche externe.

---

## Nouveaux concepts Step 3 — Pipeline Vidéo

### Flux complet de transcription vidéo

```
POST /api/video/transcribe
  ↓ MediatR
  TranscribeVideoCommand + TranscribeVideoHandler
  ├─ 1. FFmpegVideoProcessorService.ExtractAudioAsync()
  │     → Extrait la piste audio en WAV 16 kHz mono
  │
  ├─ 2. WhisperTranscriptionService.TranscribeAsync()
  │     → Transcription locale + segments avec timestamps
  │     → TranscriptionResult { FullText, Segments, Duration, Language }
  │
  ├─ 3. (Optionnel) Nettoyage LLM
  │     → Supprime les balises de timing, envoie 8000 chars max au LLM
  │     → Retourne texte propre
  │
  └─ 4. IngestionService.IngestFromSegmentsAsync()
        → TemporalChunker : accumule segments jusqu'à 800 chars
        → Format : [HH:MM:SS → HH:MM:SS] texte du segment
        → Embedding batch (OpenAI / Ollama)
        → Upsert pgvector (StartTime / EndTime persistés)
  ↓
  TranscribeVideoResponse
  {
    RawTranscription: string,
    CleanedTranscription?: string,
    Duration: double,
    SegmentCount: int,
    DocumentId?: string,
    ProcessingTimeMs: long
  }
```

### Interfaces Domain — Nouvelles en Step 3

**`ITemporalChunker`** — Chunking respectant les frontières temporelles
```csharp
Task<IReadOnlyList<TextChunk>> ChunkSegments(
    IReadOnlyList<TranscriptionSegment> segments,
    int maxCharsPerChunk);
```
- Accumule les segments Whisper jusqu'à `maxCharsPerChunk` (800 par défaut)
- Formate chaque chunk avec timestamps : `[HH:MM:SS → HH:MM:SS] texte`
- Ne coupe **jamais** un segment en deux
- Retourne des `TextChunk` avec `StartTime` et `EndTime`

**`ITranscriptionService`** — Transcription audio → segments avec timestamps
```csharp
Task<TranscriptionResult> TranscribeAsync(
    string audioPath,
    string language,
    CancellationToken ct);
```
- Entrée : chemin vers un fichier audio WAV 16 kHz mono
- Retour : résultat avec texte complet + liste de segments avec `Start`, `End`, `Text`

**`IVideoProcessorService`** — Extraction audio d'une vidéo
```csharp
Task<string> ExtractAudioAsync(string videoPath, string outputAudioPath, CancellationToken ct);
bool IsSupported(string filePath);
```
- Convertit vidéo → WAV 16 kHz mono via FFmpeg
- `IsSupported()` vérifie l'extension (`.mp4`, `.mkv`, `.webm`, `.avi`, `.mov`, `.wav`, `.mp3`, `.m4a`, `.ogg`, `.flac`)

### Modèles Domain — Nouveaux en Step 3

**`TranscriptionResult`** — Résultat complet de la transcription
```csharp
record TranscriptionResult(
    string FullText,                                    // Transcription complète
    IReadOnlyList<TranscriptionSegment> Segments,      // Segments Whisper avec timestamps
    double Duration,                                    // Durée en secondes
    string Language);                                   // Langue détectée
```

**`TranscriptionSegment`** — Segment élémentaire Whisper
```csharp
record TranscriptionSegment(
    double Start,     // Début en secondes
    double End,       // Fin en secondes
    string Text);     // Texte du segment
```

### Couche Application — Nouveaux services Step 3

**`TemporalChunker`** — Implémentation de `ITemporalChunker`
- Accumule les segments Whisper jusqu'à `maxCharsPerChunk` (800 par défaut)
- Chaque ligne : `[HH:MM:SS → HH:MM:SS] texte`
- Respecte les frontières des segments (jamais de coupure en milieu de phrase)

**`VideoTextExtractor`** — Extracteur vidéo/audio
- Implémente `ITextExtractor`
- S'intègre dans `CompositeTextExtractor`
- Permet d'uploader une vidéo via `POST /api/documents` sans code client spécifique
- Gère formats : `.mp4`, `.mkv`, `.webm`, `.avi`, `.mov`, `.wav`, `.mp3`, `.m4a`, `.ogg`, `.flac`

**`IIngestionService`** — Nouvelles méthodes
```csharp
Task IngestTextAsync(
    string text,
    string documentId,
    Dictionary<string, string>? metadata,
    CancellationToken ct);

Task IngestFromSegmentsAsync(
    IReadOnlyList<TranscriptionSegment> segments,
    string documentId,
    Dictionary<string, string>? metadata,
    CancellationToken ct);
```

**`TranscribeVideoCommand` + `TranscribeVideoHandler`** — CQRS MediatR
- Commande : `FilePath`, `Language`, `CleanWithLlm`, `AutoIngest`, `Title`
- Handler : orchestration complet (extraction → transcription → nettoyage → ingestion)
- Response : `TranscribeVideoResponse`

### Couche Infrastructure — Services vidéo Step 3

**`WhisperTranscriptionService`** — Singleton avec lazy init thread-safe
- Charge le modèle `.bin` une seule fois (via `SemaphoreSlim` + `Lazy<T>`)
- Utilise `Whisper.net` (wrapper C# de whisper.cpp)
- Produit segments avec `Start` (secondes), `End` (secondes), `Text`
- Configuration : `WhisperOptions.ModelPath` (chemin vers `ggml-*.bin`), `Threads` (défaut 4)

**`FFmpegVideoProcessorService`** — Singleton
- Utilise `FFMpegCore` pour extraire audio
- Convertit en WAV 16 kHz mono (format requis par Whisper)
- Chemin binaire configuré via `FFmpeg:BinaryPath` dans `appsettings.json`

**`WhisperOptions`** — Options Pattern
```csharp
public class WhisperOptions
{
    /// <summary>Chemin vers le fichier modèle ggml-*.bin</summary>
    public string ModelPath { get; init; } = string.Empty;

    /// <summary>Nombre de threads pour la transcription</summary>
    public int Threads { get; init; } = 4;
}
```

### Entités Domain — Enrichissements Step 3

**`DocumentChunk`** — Nouvelles propriétés optionnelles
```csharp
/// <summary>Début du segment temporel (pour les vidéos)</summary>
public TimeSpan? StartTime { get; init; }

/// <summary>Fin du segment temporel (pour les vidéos)</summary>
public TimeSpan? EndTime { get; init; }
```
- Persistés en base comme colonnes `start_time_seconds` / `end_time_seconds` (type `double precision`)
- Conversion automatique via EF Core value converter

**`Citation`** — Timestamps propagés depuis le chunk
```csharp
public TimeSpan? StartTime { get; init; }   // [NotMapped] — non persisté
public TimeSpan? EndTime { get; init; }     // [NotMapped] — non persisté
```

**`RagQuery`** — Contrôle du mode de fonctionnement
```csharp
/// <summary>Utiliser le LLM dans le pipeline (défaut: true)</summary>
public bool UseLlm { get; init; } = true;

/// <summary>Utiliser la récupération documentaire (défaut: true)</summary>
public bool UseRag { get; init; } = true;

/// <summary>Prompt système personnalisé (défaut: RagPrompts.RagSystem)</summary>
public string? SystemPrompt { get; init; }

/// <summary>Inclure l'historique de la conversation</summary>
public bool IncludeHistory { get; init; } = true;

/// <summary>Nombre maximal de tours d'historique à inclure</summary>
public int MaxHistoryTurns { get; init; } = 5;
```

---

## Nouveaux endpoints REST — Step 3

### `POST /api/video/transcribe`

Transcription vidéo/audio complète avec ingestion optionnelle en RAG.

**Paramètres :**
- `file` (form file) — Fichier vidéo/audio
- `language` (query, défaut "fr") — Code langue ISO
- `cleanWithLlm` (query, défaut false) — Nettoyage du texte via LLM
- `autoIngest` (query, défaut true) — Ingest automatique dans la base RAG
- `title` (query, optionnel) — Titre du document source

**Réponse :**
```json
{
  "rawTranscription": "string",
  "cleanedTranscription": "string ou null",
  "duration": 123.45,
  "segmentCount": 42,
  "documentId": "uuid ou null",
  "processingTimeMs": 12345
}
```

**Attributs :**
```csharp
[DisableRequestSizeLimit]
[RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
public async Task<TranscribeVideoResponse> TranscribeVideo(IFormFile file, ...)
```

### `GET /api/chat/system-prompts` — NOUVEAU

Expose les prompts système utilisables dans les différents modes RAG.

**Réponse :**
```json
{
  "ragSystem": "Vous êtes un assistant IA spécialisé...",
  "directLlmSystem": "Vous êtes un assistant IA conversationnel..."
}
```

### `POST /api/chat/ask` — Enrichi en Step 3

Champs ajoutés à `AskQuestionRequest` :
```csharp
/// <summary>Utiliser le LLM (false = mode full-text)</summary>
public bool? UseLlm { get; init; }

/// <summary>Utiliser la récupération RAG (false = LLM direct)</summary>
public bool? UseRag { get; init; }

/// <summary>Prompt système personnalisé</summary>
public string? SystemPrompt { get; init; }
```

`CitationResponse` enrichi :
```csharp
/// <summary>Titre de la section (ex: "Introduction")</summary>
public string? SectionTitle { get; init; }

/// <summary>Index du chunk dans le document</summary>
public int? ChunkIndex { get; init; }

/// <summary>Début du segment vidéo en secondes (null si document non-vidéo)</summary>
public double? StartTimeSeconds { get; init; }

/// <summary>Fin du segment vidéo en secondes (null si document non-vidéo)</summary>
public double? EndTimeSeconds { get; init; }
```

### `POST /api/chat/stream` — Enrichi en Step 3

Identique à `/ask` en mode streaming SSE, avec les mêmes paramètres enrichis.

---

## Modes de pipeline RAG — Step 3

Le pipeline RAG supporte désormais 3 modes distincts :

| Condition | Mode | Comportement | Cas d'usage |
|-----------|------|-------------|------------|
| `UseLlm = false` | **Full-text** | Recherche PostgreSQL `plainto_tsquery('french', question)` + `ts_rank`. Sans embedding ni LLM. Retourne chunks bruts. | Démonstration, requêtes factuelles, faible latence |
| `UseRag = false` | **LLM direct** | Question envoyée directement au LLM, sans récupération documentaire. Répond sur la base du modèle seul. | Démonstration, questions générales, comparaison de capabilities |
| Les deux `true` | **RAG complet** | Pipeline 7 étapes (embed question → cosinus pgvector → reranking optionnel → compression → LLM). Supporte stratégies Direct/HyDE/Fusion/Adaptive. | Production, questions spécialisées avec contexte documentaire |

---

## Configuration — appsettings.json

### Exemple Step 3
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5433;Database=ragdocumentchat;Username=postgres;Password=postgres"
  },
  "AI": {
    "Provider": "OpenAI",
    "Endpoint": "http://localhost:1234/v1",
    "ChatModel": "llama-3.2-1b-instruct",
    "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
    "ApiKey": "sk-lm-..."
  },
  "RagOptions": {
    "DefaultStrategy": "Adaptive",
    "Retrieval": { "TopK": 10, "ScoreThreshold": 0.3 }
  },
  "Whisper": {
    "ModelPath": "C:\\Users\\geoff\\Downloads\\ggml-small.bin",
    "Threads": 8
  },
  "FFmpeg": {
    "BinaryPath": "C:\\path\\to\\ffmpeg\\bin"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173", "http://localhost:5174", "http://localhost:3000"]
  }
}
```

### Sections nouvelles Step 3

**`Whisper`** — Configuration transcription locale
- `ModelPath` : Chemin complet vers le fichier modèle `.bin` (téléchargé manuellement depuis Hugging Face `ggerganov/whisper.cpp`)
- `Threads` : Nombre de threads CPU pour la transcription (défaut 4, ajuster à vos ressources)

**`FFmpeg`** — Configuration extraction audio
- `BinaryPath` : Chemin vers l'exécutable FFmpeg ou son répertoire `bin` (FFmpeg doit être installé sur la machine hôte)

### Prérequis manuels Step 3

1. **Modèle Whisper** — À télécharger manuellement depuis [Hugging Face ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp)
   - Recommandé : `ggml-small.bin` (569 MB, 10-15s par minute audio sur CPU moderne)
   - Alternative : `ggml-medium.bin` (1.5 GB, meilleure qualité)
   - Placer le fichier quelque part sur disque, mettre le chemin dans `appsettings.json`

2. **FFmpeg** — Installation système
   - Windows : Télécharger depuis [ffmpeg.org](https://ffmpeg.org) ou via Chocolatey `choco install ffmpeg`
   - Mettre le chemin vers `ffmpeg.exe` (ou son répertoire `bin`) dans `appsettings.json`

3. **PostgreSQL port 5433** — Lancé via `docker-compose up -d`
   - Différent de Step 1 qui utilisait le port 5432
   - Scripts : `scripts/init.sql` (schéma initial), `scripts/migrate-temporal-chunks.sql` (ajout colonnes vidéo)

---

## Tests — Step 3

### Projet de test

Nouveau projet `AIExperience.Tests` avec dépendances :
- `xunit` v2.9.3
- `FluentAssertions` v6.12.0
- `Microsoft.NET.Test.Sdk` v17.14.1

### Tests existants

**`TemporalChunkerTests.cs`** — 6 cas de test
1. Liste vide → chunks vides
2. Un seul segment → un chunk avec timestamps corrects
3. Segments dépassant taille max → split en plusieurs chunks
4. Petits segments groupés → un seul chunk
5. Ne coupe jamais un segment en deux
6. Format timestamp `[HH:MM:SS → HH:MM:SS]` dans le contenu

### Exécution des tests
```powershell
dotnet test "Step 3/src/Back/AIExperience.slnx"
```

---

## TODOs connus Step 3

| Fonctionnalité | Localisation | Statut | Impact |
|----------------|-------------|--------|--------|
| Stratégies HyDE et Fusion | `RagPipelineService.RetrieveChunksAsync` | TODO commenté | RAG avancé non opérationnel |
| Reranking cross-encoder | Config existe, code absent | Non commencé | Amélioration précision, non critique |
| Cache Redis | Config `RagOptions.Cache`, code absent | Non commencé | Optimisation, non critique |
| Extracteur HTML | `HtmlTextExtractor` class vide | Placeholder | Multi-format non complet |
| Extracteur DOCX/XLSX/TXT | Non créé | Non commencé | Multi-format non complet |
| `init.sql` → colonnes vidéo | Schéma incomplet | À corriger | **CRITIQUE : appliquée manuellement via migration** |
| `ARCHITECTURE.md` + `README.md` | À la racine Step 3 | Non mis à jour | Documentation obsolète (contenu Step 2) |

### Critique : Schéma PostgreSQL et colonnes vidéo

Le fichier `Step 3/scripts/init.sql` ne contient **pas** les colonnes `start_time_seconds` et `end_time_seconds` dans la table `document_chunks`. Ces colonnes sont :
- Mappées par `DocumentChunkConfiguration.cs` (EF Core)
- Utilisées dans les requêtes SQL brutes de `PgVectorStoreService`

**Solution :** Appliquer manuellement la migration :
```sql
-- Dans une base Step 3 existante, exécuter:
ALTER TABLE document_chunks
ADD COLUMN start_time_seconds double precision NULL,
ADD COLUMN end_time_seconds double precision NULL;
```

Ou utiliser le script `scripts/migrate-temporal-chunks.sql` fourni.

---

## Stack technique — Packages NuGet

| Package | Version | Usage |
|---------|---------|-------|
| .NET | 10.0 | Framework cible |
| ASP.NET Core | 10 | API REST |
| Entity Framework Core | 10 | ORM PostgreSQL |
| pgvector | 0.3.0 | Recherche vectorielle |
| MediatR | 14.1.0 | CQRS (commandes) |
| FluentValidation | 12.1.1 | Validation commandes |
| Microsoft.Extensions.AI | 10.5.0 | Abstraction `IChatClient` |
| Semantic Kernel | 1.74.0 | Templates prompts, appels LLM structurés |
| OllamaSharp | 5.4.25 | Support modèles locaux Ollama |
| PdfPig | 0.1.15 | Extraction texte PDF |
| **FFMpegCore** | **5.x** | **Extraction audio vidéo (NOUVEAU Step 3)** |
| **Whisper.net** | **1.x** | **Transcription locale (NOUVEAU Step 3)** |
| **Whisper.net.Runtime** | **1.x** | **Binaires natifs Whisper (NOUVEAU Step 3)** |
| xunit | 2.9.3 | Tests unitaires |
| FluentAssertions | 6.12.0 | Assertions fluides |

---

## Patterns de codage — Conventions

### Entités
- Setters **privés** obligatoires
- Création via **méthode factory statique** : `Entity.Create(...)`
- Jamais de constructeur public avec paramètres

### CQRS avec MediatR
- **Commandes** (écriture) → MediatR : `IRequest<T>` + handler + validator
- **Requêtes** (lecture) → appel de service direct (pas de MediatR pour les lectures)

### Options Pattern
- Toute configuration = classe `*Options` liée à une section `appsettings.json`
- Binding via `services.Configure<TOptions>(config.GetSection("..."))`

### Injection de dépendances
- Registration dans les méthodes d'extension : `AddInfrastructure()` et `AddApplication()`
- Jamais de `new` pour les services dans le code métier

### API REST
- Controllers dans `AIExperience.Web.Api/Controllers/`
- DTOs dans `AIExperience.Web.Api/DTOs/` (records C#)
- Pas de logique métier dans les controllers — déléguer aux services

---

## Fichiers clés à connaître

| Fichier | Rôle |
|---------|------|
| `src/Back/AIExperience.Web.Api/Program.cs` | Racine DI + CORS + OpenAPI + configuration Whisper/FFmpeg |
| `src/Back/AIExperience.Web.Api/appsettings.json` | Config runtime (ConnectionString port 5433, Whisper, FFmpeg, Cors) |
| `src/Back/AIExperience.Web.Api/Controllers/VideoController.cs` | Endpoint `POST /api/video/transcribe` |
| `src/Back/AIExperience.Web.Api/Controllers/ChatController.cs` | Endpoints chat : ask, stream, system-prompts |
| `src/Back/AIExperience.Web.Api/DTOs/ChatDtos.cs` | `AskQuestionRequest`, `AskQuestionResponse`, `CitationResponse`, `SystemPromptsResponse` |
| `src/Back/AIExperience.Rag.Domain/Interfaces/Services/Video/ITranscriptionService.cs` | Contrat transcription audio |
| `src/Back/AIExperience.Rag.Domain/Interfaces/Services/Video/IVideoProcessorService.cs` | Contrat extraction audio vidéo |
| `src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITemporalChunker.cs` | Contrat chunking temporel |
| `src/Back/AIExperience.Rag.Domain/Models/Video/TranscriptionResult.cs` | Record résultat transcription |
| `src/Back/AIExperience.Rag.Domain/Models/Video/TranscriptionSegment.cs` | Record segment Whisper |
| `src/Back/AIExperience.Rag.Infrastructure/AI/Transcription/WhisperTranscriptionService.cs` | Implémentation transcription locale Whisper.net |
| `src/Back/AIExperience.Rag.Infrastructure/AI/Video/FFmpegVideoProcessorService.cs` | Implémentation extraction audio FFmpeg |
| `src/Back/AIExperience.Rag.Infrastructure/Options/WhisperOptions.cs` | Configuration Whisper |
| `src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs` | Pipeline RAG + modes Full-text/LLM direct |
| `src/Back/AIExperience.Rag.Infrastructure/Persistence/PgVectorStoreService.cs` | Requêtes pgvector + `SearchFullTextAsync` + `UpsertBatchAsync` |
| `src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs` | Chunking temporel des segments Whisper |
| `src/Back/AIExperience.Rag.Application/Services/TextExtractor/VideoTextExtractor.cs` | Extracteur vidéo/audio intégré dans `CompositeTextExtractor` |
| `src/Back/AIExperience.Rag.Application/Video/Command/TranscribeVideoCommand.cs` | Commande MediatR transcription vidéo |
| `src/Back/AIExperience.Rag.Application/Video/Command/TranscribeVideoHandler.cs` | Orchestration pipeline vidéo complet |
| `src/Back/AIExperience.Rag.Application/Video/Command/TranscribeVideoValidator.cs` | Validation commande transcription |
| `src/Back/AIExperience.Rag.Application/DependencyInjection.cs` | Registration `ITemporalChunker`, `VideoTextExtractor`, handlers MediatR |
| `src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs` | Registration `AddVideoTranscription()`, `WhisperTranscriptionService`, `FFmpegVideoProcessorService` |
| `src/Back/AIExperience.Tests/TemporalChunkerTests.cs` | Tests unitaires TemporalChunker |
| `scripts/init.sql` | Schéma PostgreSQL complet (attention : colonnes vidéo manquantes) |
| `scripts/migrate-temporal-chunks.sql` | Migration ALTER TABLE colonnes `start_time_seconds`, `end_time_seconds` |
| `ARCHITECTURE.md` | Documentation architecture (à mettre à jour — contenu Step 2) |
| `README.md` | Documentation (à mettre à jour — contenu Step 2) |
