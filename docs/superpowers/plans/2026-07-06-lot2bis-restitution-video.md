# Lot 2-bis — Qualité de la restitution vidéo — Plan d'implémentation

> **Pour les agents exécutants :** SOUS-SKILL REQUISE : utiliser superpowers:subagent-driven-development
> (recommandé) ou superpowers:executing-plans pour exécuter ce plan tâche par tâche. Les étapes
> utilisent la syntaxe case à cocher (`- [ ]`) pour le suivi.

**Objectif :** Corriger la restitution médiocre des vidéos dans le RAG (Step 3), diagnostiquée dans
`Step 3/PLAN-AMELIORATION-RAG.md` révision 06/07 — points 29 à 32 (I-21, I-22, I-10, montée Whisper).

**Architecture :** Aucun nouveau composant structurel — corrections ciblées dans la couche
Application (`TemporalChunker`, `IngestionService`) et Infrastructure (`RagPipelineService`), plus
un point d'entrée unique pour l'ingestion vidéo/audio (suppression du chemin redondant
`VideoTextExtractor`).

**Tech Stack :** .NET 10, xUnit + FluentAssertions (`AIExperience.Tests`), Whisper.net/FFmpeg déjà en
place.

## Contraintes globales

- Toute réponse et tout commentaire de code doit être en français (`Step 3/src/Back/CLAUDE.md`).
- Setters privés + factory statique pour les entités ; ne pas déroger aux conventions existantes.
- Pas de mock/Moq/NSubstitute dans `AIExperience.Tests` — les services à dépendances multiples
  (orchestration I/O) ne sont pas unit-testés dans ce projet (convention déjà établie pour
  `RagPipelineService.RetrieveChunksAsync`, `TranscribeVideoHandler.Handle`) ; vérification par
  relecture + test manuel end-to-end à la place.
- ⚠️ **Ré-ingestion des vidéos existantes obligatoire après ce lot** (Tâche 6) : les vecteurs déjà
  stockés ont été calculés sur du texte pollué par les timestamps inline (I-21) et/ou un découpage
  différent (I-10) — comparables à des requêtes différentes, donc à recalculer.

---

### Task 1 : I-21 — `TemporalChunker` : contenu de chunk épuré (texte pur, sans timestamp inline)

**Fichiers :**
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs`
- Test : `Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs`

**Interfaces :**
- Consomme : `TranscriptionSegment { TimeSpan Start, TimeSpan End, string Text }` (Domain, inchangé),
  `TextChunk { string Content, TimeSpan? StartTime, TimeSpan? EndTime, ... }` (Domain, inchangé).
- Produit : `ITemporalChunker.ChunkSegments(segments, maxCharsPerChunk = 800)` — signature inchangée
  dans cette tâche (la valeur par défaut change en Tâche 4). Consommé par
  `IngestionService.IngestFromSegmentsAsync` (inchangé) et la future
  `IngestionService.IngestVideoOrAudioAsync` (Tâche 3).

- [ ] **Étape 1 : Réécrire les tests pour attendre du texte pur (ils doivent d'abord échouer)**

Remplacer entièrement le contenu de `Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs` par :

```csharp
using AIExperience.Rag.Application.Services;
using AIExperience.Rag.Domain.Models.Video;
using FluentAssertions;

namespace AIExperience.Tests;

public sealed class TemporalChunkerTests
{
    private readonly TemporalChunker _sut = new();

    [Fact]
    public void ChunkSegments_ReturnsEmpty_WhenNoSegments()
    {
        var result = _sut.ChunkSegments([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkSegments_SingleSegment_ProducesOneChunk_WithPlainTextContent()
    {
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(5),
                Text = "Bonjour tout le monde."
            }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 800);

        result.Should().HaveCount(1);
        result[0].StartTime.Should().Be(TimeSpan.FromSeconds(0));
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(5));
        // I-21 : le contenu embeddé ne doit plus contenir de balisage temporel inline.
        result[0].Content.Should().Be("Bonjour tout le monde.");
        result[0].Content.Should().NotContain("[");
        result[0].Content.Should().NotContain("→");
    }

    [Fact]
    public void ChunkSegments_GroupsSmallSegments_IntoSingleChunk_JoinedBySpace()
    {
        var segments = new[]
        {
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(2), Text = "Un." },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(2), End = TimeSpan.FromSeconds(4), Text = "Deux." },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(4), End = TimeSpan.FromSeconds(6), Text = "Trois." }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 800);

        result.Should().HaveCount(1);
        result[0].StartTime.Should().Be(TimeSpan.FromSeconds(0));
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(6));
        result[0].Content.Should().Be("Un. Deux. Trois.");
    }

    [Fact]
    public void ChunkSegments_SplitsIntoMultipleChunks_WhenOverMaxSize()
    {
        // 3 segments de 40 caractères, maxCharsPerChunk = 80 : deux segments accolés (81 avec
        // l'espace de jonction) dépassent déjà la limite, donc chaque segment forme son propre chunk.
        var segments = Enumerable.Range(0, 3).Select(i => new TranscriptionSegment
        {
            Start = TimeSpan.FromSeconds(i * 10),
            End = TimeSpan.FromSeconds(i * 10 + 9),
            Text = new string('A', 40)
        }).ToArray();

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 80);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void ChunkSegments_NeverMergesBeyondMaxSize()
    {
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(10),
                Text = new string('X', 45)
            },
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(10),
                End = TimeSpan.FromSeconds(20),
                Text = "Fin."
            }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 50);

        result.Should().HaveCount(2);
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(10));
        result[1].StartTime.Should().Be(TimeSpan.FromSeconds(10));
        result[1].Content.Should().Be("Fin.");
    }
}
```

- [ ] **Étape 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter TemporalChunkerTests`
Expected: FAIL (les anciennes assertions sur `[HH:MM:SS → HH:MM:SS]` dans `Content` ne correspondent
plus au comportement attendu par les nouveaux tests — ex. `Content.Should().Be("Bonjour tout le
monde.")` échoue car `Content` contient encore le préfixe).

- [ ] **Étape 3 : Réécrire `TemporalChunker.cs` pour produire du texte pur**

```csharp
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Découpe les segments Whisper en chunks en respectant les frontières naturelles de segments.
/// Algorithme : accumulation de segments jusqu'à saturation de la taille max, puis création d'un chunk.
/// I-21 : le contenu embeddé (<see cref="TextChunk.Content"/>) est le texte pur des segments —
/// les timestamps ne sont plus incrustés dans le texte (ils diluaient l'embedding sémantique d'environ
/// 25 à 35 % par chunk). Le timing reste porté par <see cref="TextChunk.StartTime"/>/<see cref="TextChunk.EndTime"/>,
/// injecté séparément dans l'en-tête d'extrait par <c>RagPipelineService.BuildChatHistoryAsync</c>.
/// </summary>
public sealed class TemporalChunker : ITemporalChunker
{
    /// <inheritdoc/>
    public IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 800)
    {
        if (segments.Count == 0)
            return [];

        var chunks = new List<TextChunk>();
        var buffer = new List<TranscriptionSegment>();
        var bufferLength = 0;

        foreach (var segment in segments)
        {
            var text = segment.Text.Trim();

            // Si le buffer est non vide et que l'ajout dépasserait la limite : flush.
            // +1 : l'espace de jonction inséré entre segments par BuildChunk.
            if (buffer.Count > 0 && bufferLength + text.Length + 1 > maxCharsPerChunk)
            {
                chunks.Add(BuildChunk(buffer));
                buffer.Clear();
                bufferLength = 0;
            }

            buffer.Add(segment);
            bufferLength += text.Length + 1;
        }

        if (buffer.Count > 0)
            chunks.Add(BuildChunk(buffer));

        return chunks;
    }

    /// <summary>Crée un <see cref="TextChunk"/> depuis un buffer de segments : texte pur, sans balisage temporel.</summary>
    private static TextChunk BuildChunk(List<TranscriptionSegment> buffer)
    {
        var content = string.Join(" ", buffer.Select(s => s.Text.Trim()));

        return new TextChunk
        {
            Content = content,
            StartTime = buffer[0].Start,
            EndTime = buffer[^1].End
        };
    }
}
```

- [ ] **Étape 4 : Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter TemporalChunkerTests`
Expected: PASS (5/5)

- [ ] **Étape 5 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs" "Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs"
git commit -m "fix(rag): I-21 - contenu de chunk video epure des timestamps inline"
```

---

### Task 2 : I-21 — Injecter la plage temporelle dans l'en-tête d'extrait (`RagPipelineService`)

**Fichiers :**
- Modifier : `Step 3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs:502-515`
  (méthode privée `BuildChatHistoryAsync`)

**Interfaces :**
- Consomme : `DocumentChunk.StartTime`/`EndTime` (`TimeSpan?`, Domain, déjà persistés et chargés par
  `SearchAsync`/`SearchLexicalAsync`).
- Produit : aucun changement de signature publique — le format textuel de l'en-tête `[Extrait {i}]`
  envoyé au LLM change uniquement.

> Pas de test unitaire : `BuildChatHistoryAsync` est une méthode privée d'un service à 10+
> dépendances injectées, sans mock disponible dans ce projet (même constat déjà posé pour R-1/R-16
> dans `Step 3/PLAN-AMELIORATION-RAG.md` §4.5). Vérification par relecture du diff + test manuel
> (Tâche 6).

- [ ] **Étape 1 : Modifier la construction de l'en-tête d'extrait**

Dans `RagPipelineService.cs`, remplacer :

```csharp
                // Source lisible : nom du document + page optionnelle + section optionnelle
                var source = chunk.DocumentName ?? "Document inconnu";
                var page   = chunk.PageNumber is { } p ? $", p.{p}" : string.Empty;
                var section = string.IsNullOrWhiteSpace(chunk.SectionTitle)
                    ? string.Empty
                    : $", Section: {chunk.SectionTitle}";

                contextBuilder.AppendLine($"[Extrait {i}] Source: {source}{page}{section}");
```

par :

```csharp
                // Source lisible : nom du document + page optionnelle + section optionnelle
                var source = chunk.DocumentName ?? "Document inconnu";
                var page   = chunk.PageNumber is { } p ? $", p.{p}" : string.Empty;
                var section = string.IsNullOrWhiteSpace(chunk.SectionTitle)
                    ? string.Empty
                    : $", Section: {chunk.SectionTitle}";
                // I-21 : plage temporelle vidéo injectée dans l'en-tête (le corps du chunk reste
                // du texte pur depuis la Tâche 1) — permet au LLM de citer un horodatage.
                var time = chunk.StartTime is { } start && chunk.EndTime is { } end
                    ? $", {start:hh\\:mm\\:ss}–{end:hh\\:mm\\:ss}"
                    : string.Empty;

                contextBuilder.AppendLine($"[Extrait {i}] Source: {source}{page}{section}{time}");
```

- [ ] **Étape 2 : Compiler pour vérifier l'absence d'erreur**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded.

- [ ] **Étape 3 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs"
git commit -m "fix(rag): I-21 - plage horaire video dans l'en-tete d'extrait du prompt"
```

---

### Task 3 : I-22 — Unifier le pipeline d'ingestion vidéo/audio

**Fichiers :**
- Modifier : `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionService.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/IngestDocumentCommand.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/IngestDocumentHandler.cs`
- Modifier : `Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`
- Supprimer : `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/VideoTextExtractor.cs`

**Interfaces :**
- Consomme : `IVideoProcessorService.ExtractAudioAsync(videoPath, outputAudioPath, ct) : Task<string>`,
  `ITranscriptionService.TranscribeAsync(audioPath, language, ct) : Task<TranscriptionResult>`
  (Domain, tous deux déjà enregistrés en Singleton dans
  `AIExperience.Rag.Infrastructure/DependencyInjection.cs`, `AddVideoTranscription`).
  `IIngestionService.IngestFromSegmentsAsync(segments, documentId, metadata, ct)` (déjà en place,
  Tâche 1 non requise comme pré-requis mais recommandée en premier).
- Produit : `IIngestionService.IngestVideoOrAudioAsync(filePath, documentId, metadata, language, ct)`
  — nouveau point d'entrée unique, utilisé par `IngestDocumentHandler` (upload documents) ET
  réutilisable par un futur refactor de `TranscribeVideoHandler` (non fait dans ce lot, hors
  périmètre I-22 tel que diagnostiqué).

> Pas de test unitaire pour `IngestVideoOrAudioAsync` : même constat que pour `TranscribeVideoHandler.Handle`
> (orchestration I/O à 6 dépendances, pas de mock dans ce projet). Vérification manuelle en Tâche 6.

- [ ] **Étape 1 : Ajouter la méthode à l'interface `IIngestionService`**

Dans `IIngestionService.cs`, ajouter avant `Task DeleteAsync(...)` :

```csharp
    /// <summary>
    /// Ingère un fichier vidéo ou audio en un seul pipeline cohérent : extraction audio (si vidéo)
    /// → transcription Whisper → chunking temporel (<see cref="ITemporalChunker"/>) → embedding →
    /// stockage pgvector. I-22 : point d'entrée unique quel que soit le point d'upload (documents ou
    /// vidéo), pour ne plus dépendre du chemin d'entrée pour la qualité d'ingestion (timestamps,
    /// langue paramétrable).
    /// </summary>
    /// <param name="filePath">Chemin du fichier vidéo ou audio.</param>
    /// <param name="documentId">Identifiant du document déjà créé en base.</param>
    /// <param name="metadata">Métadonnées du document ; le champ <c>Language</c> transmis est
    /// remplacé par la langue effective retournée par la transcription.</param>
    /// <param name="language">Code langue ISO à utiliser pour guider la transcription Whisper (ex. "fr").</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task IngestVideoOrAudioAsync(
        string filePath,
        Guid documentId,
        DocumentMetadata metadata,
        string language,
        CancellationToken ct = default);
```

- [ ] **Étape 2 : Implémenter dans `IngestionService`**

Ajouter les dépendances au constructeur primaire et l'implémentation. Remplacer la déclaration de
classe :

```csharp
public sealed class IngestionService(
    ICompositeTextExtractor compositeTextExtractor,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService,
    ITemporalChunker temporalChunker,
    ITextChunker textChunker,
    ILanguageDetectionService languageDetectionService) : IIngestionService
```

par :

```csharp
public sealed class IngestionService(
    ICompositeTextExtractor compositeTextExtractor,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService,
    ITemporalChunker temporalChunker,
    ITextChunker textChunker,
    ILanguageDetectionService languageDetectionService,
    IVideoProcessorService videoProcessorService,
    ITranscriptionService transcriptionService) : IIngestionService
```

Ajouter l'import en tête de fichier (`AIExperience.Rag.Application/Services/IngestionService.cs`) :

```csharp
using AIExperience.Rag.Domain.Interfaces.Services.Video;
```

Ajouter la constante et la méthode (avant `DeleteAsync`) :

```csharp
    /// <summary>Extensions vidéo nécessitant une extraction audio FFmpeg préalable.</summary>
    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".webm", ".avi", ".mov"];

    /// <inheritdoc/>
    public async Task IngestVideoOrAudioAsync(
        string filePath,
        Guid documentId,
        DocumentMetadata metadata,
        string language,
        CancellationToken ct = default)
    {
        var isVideo = VideoExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
        var tempAudioPath = isVideo ? Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav") : null;

        try
        {
            // 1. Extraction audio (uniquement pour les fichiers vidéo — les fichiers audio purs sont utilisés tels quels).
            var audioPath = isVideo
                ? await videoProcessorService.ExtractAudioAsync(filePath, tempAudioPath!, ct)
                : filePath;

            // 2. Transcription Whisper avec langue paramétrable (corrige la langue figée "fr" de l'ancien VideoTextExtractor).
            var result = await transcriptionService.TranscribeAsync(audioPath, language, ct);

            // 3. Langue effective persistée sur le document (même logique que IngestAsync pour I-6).
            var document = await documentRepository.GetByIdAsync(documentId, ct);
            if (document is not null)
            {
                document.SetDetectedLanguage(result.Language);
                await documentRepository.UpdateAsync(document, ct);
            }

            // 4. Chunking temporel + embedding + stockage : réutilise le pipeline segments existant,
            // qui préserve StartTime/EndTime par chunk (contrairement à l'ancien chemin CompositeTextExtractor).
            await IngestFromSegmentsAsync(result.Segments, documentId, metadata with { Language = result.Language }, ct);
        }
        finally
        {
            if (tempAudioPath is not null && File.Exists(tempAudioPath))
                File.Delete(tempAudioPath);
        }
    }
```

- [ ] **Étape 3 : Ajouter le champ `Language` à `IngestDocumentCommand`**

Dans `IngestDocumentCommand.cs`, ajouter :

```csharp
    /// <summary>Code langue ISO pour guider la transcription Whisper si le fichier est une vidéo/audio (défaut "fr").</summary>
    public string Language { get; init; } = "fr";
```

- [ ] **Étape 4 : Brancher `IngestDocumentHandler` sur le bon pipeline selon l'extension**

Dans `IngestDocumentHandler.cs`, ajouter la constante en tête de classe et modifier l'appel :

```csharp
public sealed class IngestDocumentHandler(
    IIngestionService ingestionService,
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ILogger<IngestDocumentHandler> logger) : IRequestHandler<IngestDocumentCommand, IngestDocumentResponse>
{
    /// <summary>Extensions vidéo/audio routées vers le pipeline segments (I-22) plutôt que vers l'extraction texte générique.</summary>
    private static readonly string[] VideoOrAudioExtensions =
        [".mp4", ".mkv", ".webm", ".avi", ".mov", ".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    public async Task<IngestDocumentResponse> Handle(IngestDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document introuvable après création : {request.DocumentId}");

        try
        {
            var extension = Path.GetExtension(request.FilePath).ToLowerInvariant();
            if (VideoOrAudioExtensions.Contains(extension))
            {
                // I-22 : un seul pipeline vidéo/audio, identique à POST /api/video/transcribe (timestamps, langue paramétrable).
                await ingestionService.IngestVideoOrAudioAsync(
                    request.FilePath, request.DocumentId, request.DocumentMetadata, request.Language, cancellationToken);
            }
            else
            {
                await ingestionService.IngestAsync(request.FilePath, request.DocumentId, request.DocumentMetadata, ct: cancellationToken);
            }
            document.MarkAsCompleted();
        }
```

Le reste de la méthode (`catch` blocks, persistance finale) est inchangé.

- [ ] **Étape 5 : Exposer le paramètre `language` sur l'upload**

Dans `DocumentsController.cs`, modifier la signature de `Upload` :

```csharp
    [HttpPost]
    public async Task<ActionResult<DocumentResponse>> Upload(
        IFormFile file,
        [FromQuery] ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        [FromQuery] Guid? replaceDocumentId = null,
        [FromQuery] string language = "fr")
```

et transmettre `language` à la commande d'ingestion :

```csharp
        await sender.Send(new IngestDocumentCommand
        {
            DocumentId = uploadResponse.DocumentId,
            FilePath = tempFile.Path,
            DocumentMetadata = new DocumentMetadata { Title = file.FileName },
            Language = language
        });
```

- [ ] **Étape 6 : Supprimer le chemin d'ingestion redondant `VideoTextExtractor`**

Supprimer le fichier `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/VideoTextExtractor.cs`.

Dans `AIExperience.Rag.Application/DependencyInjection.cs`, dans `AddTextExtractors`, supprimer la
ligne (et son commentaire) :

```csharp
            // VideoTextExtractor dépend de IVideoProcessorService + ITranscriptionService (Infrastructure Singletons)
            services.AddSingleton<ITextExtractor, VideoTextExtractor>();
```

(Les extensions vidéo/audio ne passent plus jamais par `CompositeTextExtractor` : elles sont
interceptées avant, dans `IngestDocumentHandler`, Étape 4.)

- [ ] **Étape 7 : Compiler et lancer la suite de tests complète**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded (vérifie qu'aucune référence résiduelle à `VideoTextExtractor` ne subsiste).

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: PASS (tous les tests existants, y compris ceux des Tâches 1 et 4).

- [ ] **Étape 8 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionService.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/Document/Command/IngestDocumentCommand.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/Document/Command/IngestDocumentHandler.cs" \
        "Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs"
git rm "Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/VideoTextExtractor.cs"
git commit -m "fix(rag): I-22 - pipeline d'ingestion video/audio unifie (upload documents = upload video)"
```

---

### Task 4 : I-10 — Chunking temporel plus dense, overlap, scission de segment surdimensionné

**Fichiers :**
- Modifier : `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITemporalChunker.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs`
- Test : `Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs`

**Interfaces :**
- Consomme : identique à la Tâche 1.
- Produit : `ITemporalChunker.ChunkSegments(segments, maxCharsPerChunk = 1400)` — la valeur par
  défaut passe de 800 à 1400 (cible ~1200-1500 caractères utiles, cf. plan §Lot 2-bis point 31 ;
  1400 choisi comme point médian).

- [ ] **Étape 1 : Étendre les tests (ils doivent d'abord échouer)**

Ajouter ces tests à `Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs` (dans la classe
`TemporalChunkerTests`, à la suite des tests existants) :

```csharp
    [Fact]
    public void ChunkSegments_DefaultSize_GroupsSegmentsBeyondOldEightHundredLimit()
    {
        // I-10 : la cible par défaut passe de 800 à ~1400 caractères utiles.
        var segments = new[]
        {
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(0),  End = TimeSpan.FromSeconds(10), Text = new string('A', 500) },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(20), Text = new string('B', 500) }
        };

        var result = _sut.ChunkSegments(segments); // taille par défaut, non spécifiée

        // 500 + 1 + 500 = 1001 <= nouvelle cible (~1400) : un seul chunk (aurait été 2 avec l'ancienne limite de 800).
        result.Should().HaveCount(1);
    }

    [Fact]
    public void ChunkSegments_OversizedSegment_IsSplitOnWordBoundaries_WithInterpolatedTimestamps()
    {
        var longText = string.Join(" ", Enumerable.Repeat("mot", 100)); // 399 caractères
        var segment = new TranscriptionSegment
        {
            Start = TimeSpan.FromSeconds(0),
            End = TimeSpan.FromSeconds(100),
            Text = longText
        };

        var result = _sut.ChunkSegments([segment], maxCharsPerChunk: 100);

        result.Count.Should().BeGreaterThan(1);
        result.Should().OnlyContain(c => c.Content.Length <= 100);
        result[0].StartTime.Should().Be(TimeSpan.FromSeconds(0));
        result[^1].EndTime!.Value.TotalSeconds.Should().BeApproximately(100, 1);

        for (var i = 1; i < result.Count; i++)
            result[i].StartTime.Should().BeOnOrAfter(result[i - 1].StartTime!.Value);
    }

    [Fact]
    public void ChunkSegments_OverlapsLastSegment_BetweenConsecutiveChunks()
    {
        var a = new string('A', 60);
        var b = new string('B', 60);
        var c = new string('C', 60);
        var d = new string('D', 60);

        var segments = new[]
        {
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(0),  End = TimeSpan.FromSeconds(5),  Text = a },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(5),  End = TimeSpan.FromSeconds(10), Text = b },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(15), Text = c },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(15), End = TimeSpan.FromSeconds(20), Text = d }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 130);

        result.Should().HaveCount(3);
        result[0].Content.Should().Be($"{a} {b}");
        // Chunk 2 reprend B (overlap du chunk précédent) avant son propre contenu C.
        result[1].Content.Should().Be($"{b} {c}");
        result[2].Content.Should().Be($"{c} {d}");
    }
```

- [ ] **Étape 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter TemporalChunkerTests`
Expected: FAIL sur les 3 nouveaux tests (pas d'overlap, pas de scission, taille par défaut encore à 800).

- [ ] **Étape 3 : Mettre à jour la valeur par défaut dans l'interface**

Dans `ITemporalChunker.cs`, remplacer :

```csharp
    IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 800);
```

par :

```csharp
    IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 1400);
```

- [ ] **Étape 4 : Réécrire `TemporalChunker.ChunkSegments` avec overlap et scission**

Remplacer le contenu de `TemporalChunker.cs` (obtenu à l'issue de la Tâche 1) par :

```csharp
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Découpe les segments Whisper en chunks en respectant les frontières naturelles de segments.
/// I-21 : contenu embeddé = texte pur (pas de timestamp inline), timing porté par StartTime/EndTime.
/// I-10 : cible par défaut ~1400 caractères utiles, overlap d'un segment entre chunks consécutifs
/// (évite de perdre le contexte à la frontière), et scission d'un segment isolé surdimensionné
/// (rare, orateur sans pause) avec timestamps interpolés proportionnellement au texte.
/// </summary>
public sealed class TemporalChunker : ITemporalChunker
{
    /// <inheritdoc/>
    public IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 1400)
    {
        if (segments.Count == 0)
            return [];

        // I-10 : un segment isolé plus long que la cible est scindé en sous-segments avant chunking.
        var expanded = segments.SelectMany(s => SplitOversizedSegment(s, maxCharsPerChunk)).ToList();

        var chunks = new List<TextChunk>();
        var buffer = new List<TranscriptionSegment>();
        var bufferLength = 0;

        foreach (var segment in expanded)
        {
            var text = segment.Text.Trim();

            // Si le buffer est non vide et que l'ajout dépasserait la limite : flush.
            if (buffer.Count > 0 && bufferLength + text.Length + 1 > maxCharsPerChunk)
            {
                chunks.Add(BuildChunk(buffer));

                // I-10 : overlap — le chunk suivant repart avec le dernier segment du buffer
                // précédent, pour ne pas perdre le contexte à la frontière entre deux chunks.
                var overlapSegment = buffer[^1];
                buffer.Clear();
                buffer.Add(overlapSegment);
                bufferLength = overlapSegment.Text.Trim().Length + 1;
            }

            buffer.Add(segment);
            bufferLength += text.Length + 1;
        }

        if (buffer.Count > 0)
            chunks.Add(BuildChunk(buffer));

        return chunks;
    }

    /// <summary>Crée un <see cref="TextChunk"/> depuis un buffer de segments : texte pur, sans balisage temporel.</summary>
    private static TextChunk BuildChunk(List<TranscriptionSegment> buffer)
    {
        var content = string.Join(" ", buffer.Select(s => s.Text.Trim()));

        return new TextChunk
        {
            Content = content,
            StartTime = buffer[0].Start,
            EndTime = buffer[^1].End
        };
    }

    /// <summary>
    /// Scinde un segment dont le texte dépasse <paramref name="maxCharsPerChunk"/> en plusieurs
    /// sous-segments sur frontière de mot, avec des timestamps Start/End interpolés
    /// proportionnellement à la position du texte dans le segment d'origine.
    /// Cas limite (I-10) : un orateur qui parle longtemps sans pause produit un unique segment
    /// Whisper anormalement long.
    /// </summary>
    private static IEnumerable<TranscriptionSegment> SplitOversizedSegment(
        TranscriptionSegment segment, int maxCharsPerChunk)
    {
        var text = segment.Text.Trim();
        if (text.Length <= maxCharsPerChunk)
        {
            yield return segment with { Text = text };
            yield break;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxCharsPerChunk)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) parts.Add(current.ToString());

        // Interpolation linéaire des timestamps selon la position de chaque partie dans le texte total.
        var totalDuration = segment.End - segment.Start;
        var totalLength = text.Length;
        var offset = 0;

        foreach (var part in parts)
        {
            var partStart = segment.Start + totalDuration * ((double)offset / totalLength);
            offset += part.Length + 1; // +1 pour l'espace consommé entre les mots
            var partEnd = segment.Start + totalDuration * (Math.Min(offset, totalLength) / (double)totalLength);

            yield return new TranscriptionSegment { Start = partStart, End = partEnd, Text = part };
        }
    }
}
```

- [ ] **Étape 5 : Lancer la suite complète des tests**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter TemporalChunkerTests`
Expected: PASS (8/8 : les 5 tests de la Tâche 1 + les 3 nouveaux).

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: PASS (aucune régression sur `RecursiveChunkerTests` ou le reste de la suite).

- [ ] **Étape 6 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITemporalChunker.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs" \
        "Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs"
git commit -m "fix(rag): I-10 - chunking temporel plus dense, overlap et scission de segment surdimensionne"
```

---

### Task 5 : Montée du modèle Whisper `small` → `medium`

**Fichiers :**
- Modifier : `Step 3/src/Back/AIExperience.Web.Api/appsettings.json:40`

**Interfaces :** aucune — changement de configuration pur, aucun code touché.

> ⚠️ **Pré-requis manuel** : `ggml-medium.bin` (~1.5 Go) doit être téléchargé par Geoffrey depuis
> [Hugging Face ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) et placé sur
> disque avant que ce changement ne prenne effet. Sans le fichier, `WhisperTranscriptionService`
> lève une `FileNotFoundException` explicite au premier appel (comportement voulu, I-3) plutôt que
> d'échouer silencieusement.

- [ ] **Étape 1 : Mettre à jour le chemin du modèle**

Dans `Step 3/src/Back/AIExperience.Web.Api/appsettings.json`, remplacer :

```json
    "ModelPath": "C:\\Users\\geoff\\Downloads\\ggml-small.bin",
```

par :

```json
    "ModelPath": "C:\\Users\\geoff\\Downloads\\ggml-medium.bin",
```

- [ ] **Étape 2 : Vérifier manuellement**

Démarrer l'API (`dotnet run --project "Step 3/src/Back/AIExperience.Web.Api"`) et transcrire un
petit extrait vidéo/audio via `/scalar/v1` ou la page Vidéo — confirmer l'absence de
`FileNotFoundException` dans les logs (= le fichier `ggml-medium.bin` est bien présent au chemin
configuré).

- [ ] **Étape 3 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Web.Api/appsettings.json"
git commit -m "chore(whisper): montee du modele ggml-small vers ggml-medium"
```

---

### Task 6 (manuelle, hors code) : Ré-ingestion des vidéos existantes + vérification avant/après

Les Tâches 1 et 4 changent le texte effectivement embeddé pour les vidéos (suppression des
timestamps inline, nouvelles frontières de chunk) : les vecteurs déjà stockés pour les vidéos du
corpus actuel sont désormais incohérents avec les nouvelles requêtes.

- [ ] **Étape 1 : Identifier les documents vidéo/audio existants**

Via `GET /api/documents`, repérer les documents dont `fileName` a une extension vidéo/audio
(`.mp4`, `.mkv`, `.webm`, `.avi`, `.mov`, `.wav`, `.mp3`, `.m4a`, `.ogg`, `.flac`).

- [ ] **Étape 2 : Supprimer puis ré-uploader chaque vidéo**

Pour chaque document identifié : `DELETE /api/documents/{id}`, puis ré-upload du même fichier via
`POST /api/documents` (page Documents du front, ou Scalar) — ce chemin passe désormais par le
pipeline unifié de la Tâche 3.

- [ ] **Étape 3 : Vérification avant/après (mini R-13, même règle que les lots précédents)**

Constituer ~5 questions « golden » sur une vidéo de référence du corpus. Pour chacune, poser la
question via `/chat` (mode RAG) et comparer :
- Citations retournées : le texte affiché dans les extraits ne doit plus contenir de balisage
  `[HH:MM:SS → HH:MM:SS]`, et le panneau Sources doit toujours afficher `startTimeSeconds`/
  `endTimeSeconds` correctement.
- Pertinence : les scores des chunks vidéo pertinents doivent se démarquer plus nettement du bruit
  qu'avant ce lot (effet attendu, cf. diagnostic I-21).
