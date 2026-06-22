# Chunking Temporel Vidéo — Plan d'Implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer `RecursiveChunker` par un chunker temporel qui découpe les transcriptions Whisper en respectant les frontières de segments, et propage `StartTime`/`EndTime` jusqu'aux citations RAG et au DTO API.

**Architecture:** Nouveau `ITemporalChunker` (Domain) implémenté par `TemporalChunker` (Application). `IngestionService` reçoit une nouvelle méthode `IngestFromSegmentsAsync` qui utilise ce chunker. `PgVectorStoreService` persiste et lit les colonnes temporelles via SQL brut. Les timestamps remontent jusqu'aux `CitationResponse` du DTO API.

**Tech Stack:** .NET 10, C#, Whisper.net (segments déjà produits), Npgsql + pgvector (SQL brut), EF Core 10 (config uniquement), xUnit + FluentAssertions.

## Global Constraints

- Toutes les nouvelles propriétés sont `nullable` — rétrocompatibilité totale avec les chunks PDF/HTML existants.
- Les timestamps sont stockés en `double precision` (secondes) en base de données, exposés comme `TimeSpan?` en C#.
- Pas de modification de `RecursiveChunker` — il reste inchangé pour PDF/HTML.
- SQL brut dans `PgVectorStoreService` — ne jamais ajouter de migration EF Core (le schéma est géré par `init.sql` + scripts ALTER).
- `PgVectorStoreService` lit les colonnes par ordinal : ajouter les nouvelles colonnes **à la fin** du SELECT pour ne pas décaler les ordinaux existants.
- Langue des commentaires : français. Commentaires XML (`///`) sur toutes les propriétés et méthodes publiques.
- Projet de test : `Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj`.

---

## Cartographie des fichiers

| Fichier | Action | Tâche |
|---------|--------|-------|
| `scripts/migrate-temporal-chunks.sql` | Créer | T1 |
| `Domain/Models/TextChunk.cs` | Modifier | T1 |
| `Domain/Entities/DocumentChunk.cs` | Modifier | T1 |
| `Domain/Entities/Citation.cs` | Modifier | T1 |
| `Domain/Interfaces/Services/ITemporalChunker.cs` | Créer | T2 |
| `Application/Services/TemporalChunker.cs` | Créer | T2 |
| `Tests/AIExperience.Tests.csproj` | Créer | T2 |
| `Tests/TemporalChunkerTests.cs` | Créer | T2 |
| `Domain/Interfaces/Services/IIngestionService.cs` | Modifier | T3 |
| `Application/Services/IngestionService.cs` | Modifier | T3 |
| `Application/DependencyInjection.cs` | Modifier | T3 |
| `Infrastructure/Persistence/Configuration/DocumentChunkConfiguration.cs` | Modifier | T4 |
| `Infrastructure/VectorStore/PgVectorStoreService.cs` | Modifier | T4 |
| `Infrastructure/AI/Rag/RagPipelineService.cs` | Modifier | T5 |
| `Application/Video/Command/TranscribeVideoHandler.cs` | Modifier | T5 |
| `Web.Api/DTOs/ChatDtos.cs` | Modifier | T5 |

---

### Task 1 : Domain models + script de migration

**Files:**
- Créer : `Step 3/scripts/migrate-temporal-chunks.sql`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Domain/Models/TextChunk.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Domain/Entities/DocumentChunk.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Domain/Entities/Citation.cs`

**Interfaces:**
- Produit : `TextChunk` avec `TimeSpan? StartTime` et `TimeSpan? EndTime`
- Produit : `DocumentChunk.Create(...)` avec paramètres optionnels `TimeSpan? startTime = null, TimeSpan? endTime = null`
- Produit : `Citation.Create(...)` avec paramètres optionnels `TimeSpan? startTime = null, TimeSpan? endTime = null`

- [ ] **Étape 1 : Créer le script de migration BDD**

Créer `Step 3/scripts/migrate-temporal-chunks.sql` :

```sql
-- Migration : ajout des timestamps temporels sur les chunks vidéo
-- Colonnes nullable — aucun chunk existant n'est impacté.
ALTER TABLE document_chunks
    ADD COLUMN IF NOT EXISTS start_time_seconds double precision NULL,
    ADD COLUMN IF NOT EXISTS end_time_seconds double precision NULL;
```

- [ ] **Étape 2 : Exécuter le script sur la base locale**

```powershell
# Depuis le répertoire du projet
docker exec -i $(docker ps -q --filter "name=postgres") psql -U postgres -d ragdocumentchat -f /dev/stdin < "Step 3/scripts/migrate-temporal-chunks.sql"
```

Résultat attendu : `ALTER TABLE` (pas d'erreur). Vérifier :
```sql
SELECT column_name, data_type FROM information_schema.columns
WHERE table_name = 'document_chunks' AND column_name IN ('start_time_seconds','end_time_seconds');
```
Doit retourner 2 lignes.

- [ ] **Étape 3 : Modifier `TextChunk.cs`**

Contenu complet du fichier :

```csharp
namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Représente un fragment de texte extrait lors du découpage (chunking) d'un document.
/// </summary>
public sealed record TextChunk
{
    /// <summary>Contenu textuel du chunk.</summary>
    public required string Content { get; init; }

    /// <summary>Numéro de page source (si disponible, pour les PDF).</summary>
    public int? PageNumber { get; init; }

    /// <summary>Titre de la section source (si disponible).</summary>
    public string? SectionTitle { get; init; }

    /// <summary>Début du chunk dans la vidéo source (null pour les documents non-vidéo).</summary>
    public TimeSpan? StartTime { get; init; }

    /// <summary>Fin du chunk dans la vidéo source (null pour les documents non-vidéo).</summary>
    public TimeSpan? EndTime { get; init; }
}
```

- [ ] **Étape 4 : Modifier `DocumentChunk.cs`**

Ajouter les propriétés et mettre à jour `Create()` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente un fragment (chunk) d'un document, issu du découpage lors de l'ingestion.
/// Chaque chunk est vectorisé et stocké dans pgvector pour la recherche sémantique.
/// </summary>
public class DocumentChunk
{
    /// <summary>Identifiant unique du chunk.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Identifiant du document parent.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Contenu textuel brut du chunk.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Position ordinale du chunk dans le document (0-based).</summary>
    public int ChunkIndex { get; private set; }

    /// <summary>Numéro de page du document source où se trouve ce chunk.</summary>
    public int? PageNumber { get; private set; }

    /// <summary>Titre de la section ou du chapitre contenant ce chunk.</summary>
    public string? SectionTitle { get; private set; }

    /// <summary>Nombre de dimensions du vecteur d'embedding.</summary>
    public int EmbeddingDimensions { get; private set; }

    /// <summary>Date et heure de création du chunk (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Position de début dans la vidéo source (null pour les documents non-vidéo).
    /// Persisté en base comme <c>start_time_seconds</c> (double precision).
    /// </summary>
    public TimeSpan? StartTime { get; private set; }

    /// <summary>
    /// Position de fin dans la vidéo source (null pour les documents non-vidéo).
    /// Persisté en base comme <c>end_time_seconds</c> (double precision).
    /// </summary>
    public TimeSpan? EndTime { get; private set; }

    /// <summary>
    /// Nom du document source — non persisté, renseigné à la volée depuis le JOIN documents.
    /// </summary>
    [NotMapped]
    public string? DocumentName { get; private set; }

    /// <summary>Navigation vers le document parent.</summary>
    public Document Document { get; private set; } = null!;

    private DocumentChunk() { }

    /// <summary>
    /// Crée un nouveau chunk de document.
    /// </summary>
    public static DocumentChunk Create(
        Guid documentId,
        string content,
        int chunkIndex,
        int embeddingDimensions,
        int? pageNumber = null,
        string? sectionTitle = null,
        string? documentName = null,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        return new DocumentChunk
        {
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex,
            EmbeddingDimensions = embeddingDimensions,
            PageNumber = pageNumber,
            SectionTitle = sectionTitle,
            DocumentName = documentName,
            StartTime = startTime,
            EndTime = endTime
        };
    }
}
```

- [ ] **Étape 5 : Modifier `Citation.cs`**

Ajouter les propriétés `[NotMapped]` et mettre à jour `Create()` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente une citation d'une source utilisée pour générer une réponse RAG.
/// </summary>
public sealed record Citation
{
    /// <summary>Identifiant unique de la citation.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Identifiant du message assistant auquel cette citation est rattachée.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>Identifiant du document source.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Nom du document source (pour affichage).</summary>
    public string DocumentName { get; private set; } = string.Empty;

    /// <summary>Numéro de page du document où se trouve l'extrait (null si non applicable).</summary>
    public int? PageNumber { get; private set; }

    /// <summary>Extrait textuel du chunk ayant servi de contexte.</summary>
    public string Excerpt { get; private set; } = string.Empty;

    /// <summary>Score de similarité cosinus entre la question et ce chunk (entre 0 et 1).</summary>
    public double Score { get; private set; }

    /// <summary>Titre de la section du document source — non persisté, renseigné à la volée.</summary>
    [NotMapped]
    public string? SectionTitle { get; private set; }

    /// <summary>Position ordinale du chunk — non persisté, utile pour le débogage.</summary>
    [NotMapped]
    public int ChunkIndex { get; private set; }

    /// <summary>
    /// Début du chunk dans la vidéo source — non persisté, propagé depuis DocumentChunk.
    /// Null pour les documents non-vidéo.
    /// </summary>
    [NotMapped]
    public TimeSpan? StartTime { get; private set; }

    /// <summary>
    /// Fin du chunk dans la vidéo source — non persisté, propagé depuis DocumentChunk.
    /// Null pour les documents non-vidéo.
    /// </summary>
    [NotMapped]
    public TimeSpan? EndTime { get; private set; }

    /// <summary>Navigation vers le message parent.</summary>
    public ChatMessage Message { get; private set; } = null!;

    private Citation() { }

    /// <summary>Crée une nouvelle citation de source.</summary>
    public static Citation Create(
        Guid messageId,
        Guid documentId,
        string documentName,
        string excerpt,
        double score,
        int? pageNumber = null,
        string? sectionTitle = null,
        int chunkIndex = 0,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        return new Citation
        {
            MessageId = messageId,
            DocumentId = documentId,
            DocumentName = documentName,
            Excerpt = excerpt,
            Score = score,
            PageNumber = pageNumber,
            SectionTitle = sectionTitle,
            ChunkIndex = chunkIndex,
            StartTime = startTime,
            EndTime = endTime
        };
    }
}
```

- [ ] **Étape 6 : Compiler pour valider**

```powershell
dotnet build "Step 3/src/Back/AIExperience.slnx"
```

Résultat attendu : `Build succeeded. 0 Error(s)`.

- [ ] **Étape 7 : Commit**

```bash
git add "Step 3/scripts/migrate-temporal-chunks.sql" \
        "Step 3/src/Back/AIExperience.Rag.Domain/Models/TextChunk.cs" \
        "Step 3/src/Back/AIExperience.Rag.Domain/Entities/DocumentChunk.cs" \
        "Step 3/src/Back/AIExperience.Rag.Domain/Entities/Citation.cs"
git commit -m "feat(domain): ajout StartTime/EndTime sur TextChunk, DocumentChunk et Citation + script migration BDD"
```

---

### Task 2 : ITemporalChunker + TemporalChunker + tests unitaires

**Files:**
- Créer : `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITemporalChunker.cs`
- Créer : `Step 3/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs`
- Créer : `Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj`
- Créer : `Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs`

**Interfaces:**
- Consomme : `TranscriptionSegment { TimeSpan Start, TimeSpan End, string Text }` (Domain/Models/Video)
- Consomme : `TextChunk { string Content, TimeSpan? StartTime, TimeSpan? EndTime }` (Task 1)
- Produit : `ITemporalChunker.ChunkSegments(IReadOnlyList<TranscriptionSegment>, int maxCharsPerChunk = 800) : IReadOnlyList<TextChunk>`

- [ ] **Étape 1 : Créer `ITemporalChunker.cs`**

```csharp
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Découpe une liste de segments Whisper en chunks <see cref="TextChunk"/> enrichis de timestamps.
/// Chaque chunk regroupe des segments consécutifs jusqu'à la taille maximale,
/// sans jamais couper au milieu d'un segment.
/// </summary>
public interface ITemporalChunker
{
    /// <summary>
    /// Regroupe les segments en chunks de taille maximale <paramref name="maxCharsPerChunk"/>.
    /// Chaque chunk contient les timestamps de début et de fin de ses segments.
    /// </summary>
    /// <param name="segments">Liste ordonnée de segments Whisper.</param>
    /// <param name="maxCharsPerChunk">Taille maximale en caractères par chunk (défaut : 800).</param>
    /// <returns>Liste ordonnée de chunks avec timestamps.</returns>
    IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 800);
}
```

- [ ] **Étape 2 : Créer `TemporalChunker.cs`**

```csharp
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;
using System.Text;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Découpe les segments Whisper en chunks en respectant les frontières naturelles de segments.
/// Algorithme : accumulation de segments jusqu'à saturation de la taille max, puis création d'un chunk.
/// Le contenu inclut les timestamps inline : "[HH:MM:SS → HH:MM:SS] texte".
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
            // Ligne formatée : "[HH:MM:SS → HH:MM:SS] texte"
            var line = FormatLine(segment);

            // Si le buffer est non vide et que l'ajout dépasserait la limite : flush
            if (buffer.Count > 0 && bufferLength + line.Length > maxCharsPerChunk)
            {
                chunks.Add(BuildChunk(buffer));
                buffer.Clear();
                bufferLength = 0;
            }

            buffer.Add(segment);
            bufferLength += line.Length;
        }

        // Flush du dernier buffer
        if (buffer.Count > 0)
            chunks.Add(BuildChunk(buffer));

        return chunks;
    }

    /// <summary>Formate un segment en ligne avec timestamp : "[HH:MM:SS → HH:MM:SS] texte".</summary>
    private static string FormatLine(TranscriptionSegment segment)
        => $"[{segment.Start:hh\\:mm\\:ss} → {segment.End:hh\\:mm\\:ss}] {segment.Text.Trim()}\n";

    /// <summary>Crée un TextChunk depuis un buffer de segments.</summary>
    private static TextChunk BuildChunk(List<TranscriptionSegment> buffer)
    {
        var sb = new StringBuilder();
        foreach (var s in buffer)
            sb.Append(FormatLine(s));

        return new TextChunk
        {
            Content = sb.ToString().TrimEnd(),
            StartTime = buffer[0].Start,
            EndTime = buffer[^1].End
        };
    }
}
```

- [ ] **Étape 3 : Créer le projet de tests**

```powershell
dotnet new xunit -n AIExperience.Tests -o "Step 3/src/Back/AIExperience.Tests"
dotnet add "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" package FluentAssertions --version 6.12.0
dotnet add "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" reference "Step 3/src/Back/AIExperience.Rag.Domain/AIExperience.Rag.Domain.csproj"
dotnet add "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" reference "Step 3/src/Back/AIExperience.Rag.Application/AIExperience.Rag.Application.csproj"
dotnet sln "Step 3/src/Back/AIExperience.slnx" add "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj"
```

Supprimer le fichier de test par défaut généré par `dotnet new xunit` :
```powershell
Remove-Item "Step 3/src/Back/AIExperience.Tests/UnitTest1.cs"
```

- [ ] **Étape 4 : Écrire les tests en échec**

Créer `Step 3/src/Back/AIExperience.Tests/TemporalChunkerTests.cs` :

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
    public void ChunkSegments_SingleSegment_ProducesOneChunk()
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
        result[0].Content.Should().Contain("Bonjour tout le monde.");
        result[0].Content.Should().Contain("[00:00:00 → 00:00:05]");
    }

    [Fact]
    public void ChunkSegments_SplitsIntoMultipleChunks_WhenOverMaxSize()
    {
        // 3 segments de ~50 chars chacun avec maxCharsPerChunk = 80
        var segments = Enumerable.Range(0, 3).Select(i => new TranscriptionSegment
        {
            Start = TimeSpan.FromSeconds(i * 10),
            End = TimeSpan.FromSeconds(i * 10 + 9),
            Text = new string('A', 40) // ligne formatée ~60 chars
        }).ToArray();

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 80);

        // Chaque segment dépasse 80 chars une fois formaté avec le timestamp — 3 chunks
        result.Should().HaveCount(3);
    }

    [Fact]
    public void ChunkSegments_GroupsSmallSegments_IntoSingleChunk()
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
        result[0].Content.Should().Contain("Un.");
        result[0].Content.Should().Contain("Deux.");
        result[0].Content.Should().Contain("Trois.");
    }

    [Fact]
    public void ChunkSegments_NeverCutsSegmentInHalf()
    {
        // 2 segments : le 1er remplit presque le buffer, le 2nd doit être dans un chunk séparé
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(10),
                Text = new string('X', 70) // ~96 chars formatés (dépasse 80 si on ajoute le 2nd)
            },
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(10),
                End = TimeSpan.FromSeconds(20),
                Text = "Fin."
            }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 80);

        // Le 1er segment seul dépasse déjà 80 chars → chaque segment dans son propre chunk
        result.Should().HaveCount(2);
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(10));
        result[1].StartTime.Should().Be(TimeSpan.FromSeconds(10));
        result[1].Content.Should().Contain("Fin.");
    }

    [Fact]
    public void ChunkSegments_ContentContainsTimestampFormat()
    {
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(3662), // 01:01:02
                End = TimeSpan.FromSeconds(3665),   // 01:01:05
                Text = "Test timestamp."
            }
        };

        var result = _sut.ChunkSegments(segments);

        result[0].Content.Should().Contain("[01:01:02 → 01:01:05]");
    }
}
```

- [ ] **Étape 5 : Lancer les tests — vérifier qu'ils échouent**

```powershell
dotnet test "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --no-build -v minimal 2>&1
```

Résultat attendu : erreur de compilation car `TemporalChunker` n'existe pas encore.

- [ ] **Étape 6 : Compiler avec l'implémentation**

```powershell
dotnet build "Step 3/src/Back/AIExperience.slnx"
```

Résultat attendu : `Build succeeded. 0 Error(s)`.

- [ ] **Étape 7 : Lancer les tests — vérifier qu'ils passent**

```powershell
dotnet test "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" -v normal
```

Résultat attendu : `Passed! - Failed: 0, Passed: 6, Skipped: 0`.

- [ ] **Étape 8 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITemporalChunker.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs" \
        "Step 3/src/Back/AIExperience.Tests/" \
        "Step 3/src/Back/AIExperience.slnx"
git commit -m "feat(chunking): TemporalChunker — découpage par segments Whisper avec timestamps + tests unitaires"
```

---

### Task 3 : IIngestionService + IngestionService + DI

**Files:**
- Modifier : `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionService.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`

**Interfaces:**
- Consomme : `ITemporalChunker.ChunkSegments(...)` (Task 2)
- Consomme : `DocumentChunk.Create(..., startTime, endTime)` (Task 1)
- Produit : `IIngestionService.IngestFromSegmentsAsync(IReadOnlyList<TranscriptionSegment>, Guid, DocumentMetadata, CancellationToken)`

- [ ] **Étape 1 : Ajouter `IngestFromSegmentsAsync` dans `IIngestionService.cs`**

```csharp
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Service d'ingestion de documents dans le pipeline RAG.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Ingère un document : parsing → chunking → embedding → stockage dans pgvector.
    /// </summary>
    Task IngestAsync(
        string filePath,
        Guid documentId,
        DocumentMetadata metadata,
        ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        CancellationToken ct = default);

    /// <summary>
    /// Ingère un texte déjà extrait (ex: transcription vidéo) sans passer par le parsing de fichier.
    /// Pipeline : chunking par caractères → embedding → stockage pgvector.
    /// </summary>
    Task IngestTextAsync(
        string text,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Ingère une transcription vidéo depuis les segments Whisper bruts.
    /// Utilise le chunking temporel (<see cref="ITemporalChunker"/>) pour préserver les timestamps.
    /// Pipeline : chunking temporel → embedding → stockage pgvector (avec StartTime/EndTime).
    /// </summary>
    /// <param name="segments">Segments Whisper ordonnés avec timestamps.</param>
    /// <param name="documentId">Identifiant du document déjà créé en base.</param>
    /// <param name="metadata">Métadonnées du document.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task IngestFromSegmentsAsync(
        IReadOnlyList<TranscriptionSegment> segments,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Supprime tous les chunks et vecteurs associés à un document de pgvector.
    /// </summary>
    Task DeleteAsync(Guid documentId, CancellationToken ct = default);
}
```

- [ ] **Étape 2 : Implémenter `IngestFromSegmentsAsync` dans `IngestionService.cs`**

Remplacer le contenu complet de `IngestionService.cs` :

```csharp
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Orchestre le pipeline complet d'ingestion : parsing → chunking → embedding → stockage pgvector.
/// </summary>
public sealed class IngestionService(
    ICompositeTextExtractor compositeTextExtractor,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService,
    ITemporalChunker temporalChunker) : IIngestionService
{
    /// <inheritdoc/>
    public async Task IngestAsync(
        string filePath,
        Guid documentId,
        DocumentMetadata metadata,
        ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        CancellationToken ct = default)
    {
        var rawText = await compositeTextExtractor.ExtractTextAsync(filePath, ct);
        var chunker = CreateChunker(strategy);
        var textChunks = chunker.Chunk(rawText);
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), ct);

        for (int i = 0; i < textChunks.Count; i++)
        {
            var tc = textChunks[i];
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));

            await vectorStoreService.UpsertAsync(chunk, embeddings[i], ct);
        }
    }

    /// <inheritdoc/>
    public async Task IngestTextAsync(
        string text,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default)
    {
        var chunker = CreateChunker(ChunkingStrategy.Recursive);
        var textChunks = chunker.Chunk(text);
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), ct);

        for (int i = 0; i < textChunks.Count; i++)
        {
            var tc = textChunks[i];
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));

            await vectorStoreService.UpsertAsync(chunk, embeddings[i], ct);
        }
    }

    /// <inheritdoc/>
    public async Task IngestFromSegmentsAsync(
        IReadOnlyList<TranscriptionSegment> segments,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default)
    {
        // Chunking temporel : respecte les frontières des segments Whisper
        var textChunks = temporalChunker.ChunkSegments(segments);
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), ct);

        for (int i = 0; i < textChunks.Count; i++)
        {
            var tc = textChunks[i];
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                startTime: tc.StartTime,
                endTime: tc.EndTime);

            await vectorStoreService.UpsertAsync(chunk, embeddings[i], ct);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid documentId, CancellationToken ct = default)
        => await vectorStoreService.DeleteByDocumentIdAsync(documentId, ct);

    private static string CleanString(string? input) => input?.Replace("\0", string.Empty) ?? string.Empty;

    private static ITextChunker CreateChunker(ChunkingStrategy chunkingStrategy) => chunkingStrategy switch
    {
        ChunkingStrategy.Recursive => new RecursiveChunker(),
        _ => new RecursiveChunker()
    };
}
```

- [ ] **Étape 3 : Enregistrer `TemporalChunker` dans `DependencyInjection.cs`**

```csharp
using AIExperience.Rag.Application.Common.Behaviors;
using AIExperience.Rag.Application.Services;
using AIExperience.Rag.Application.Services.TextExtractor;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.Video;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AIExperience.Rag.Application
{
    /// <summary>Point d'entrée de la configuration du projet Application.</summary>
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            return services
                .ConfigureMediatR()
                .AddChunker()
                .AddTextExtractors()
                .AddIngestion();
        }

        public static IServiceCollection ConfigureMediatR(this IServiceCollection services)
        {
            var assembly = Assembly.GetExecutingAssembly();
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(assembly);
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            });
            services.AddValidatorsFromAssembly(assembly);
            return services;
        }

        public static IServiceCollection AddChunker(this IServiceCollection services)
        {
            services.AddScoped<ITextChunker, RecursiveChunker>();
            // Singleton : TemporalChunker est sans état — peut être réutilisé en concurrence
            services.AddSingleton<ITemporalChunker, TemporalChunker>();
            return services;
        }

        public static IServiceCollection AddIngestion(this IServiceCollection services)
        {
            return services.AddScoped<IIngestionService, IngestionService>();
        }

        public static IServiceCollection AddTextExtractors(this IServiceCollection services)
        {
            services.AddSingleton<ITextExtractor, PdfTextExtractor>();
            services.AddSingleton<ITextExtractor, HtmlTextExtractor>();
            services.AddSingleton<ITextExtractor, VideoTextExtractor>();
            services.AddSingleton<ICompositeTextExtractor, CompositeTextExtractor>();
            return services;
        }
    }
}
```

- [ ] **Étape 4 : Compiler**

```powershell
dotnet build "Step 3/src/Back/AIExperience.slnx"
```

Résultat attendu : `Build succeeded. 0 Error(s)`.

- [ ] **Étape 5 : Relancer les tests**

```powershell
dotnet test "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" -v minimal
```

Résultat attendu : `Passed! - Failed: 0, Passed: 6, Skipped: 0`.

- [ ] **Étape 6 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionService.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs" \
        "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs"
git commit -m "feat(ingestion): IngestFromSegmentsAsync — chunking temporel Whisper dans le pipeline RAG"
```

---

### Task 4 : PgVectorStoreService + EF Core config

**Files:**
- Modifier : `Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/DocumentChunkConfiguration.cs`

**Interfaces:**
- Consomme : `DocumentChunk.StartTime`, `DocumentChunk.EndTime` (Task 1)
- Produit : colonnes `start_time_seconds` / `end_time_seconds` persistées et relues depuis Postgres

- [ ] **Étape 1 : Modifier `PgVectorStoreService.cs` — `UpsertAsync`**

Remplacer la méthode `UpsertAsync` :

```csharp
/// <inheritdoc/>
public async Task UpsertAsync(DocumentChunk chunk, float[] embedding, CancellationToken ct = default)
{
    var sql = """
        INSERT INTO document_chunks
            ("id", document_id, content, chunk_index, page_number, section_title,
             embedding_dimensions, embedding, created_at, start_time_seconds, end_time_seconds)
        VALUES
            (@id, @documentId, @content, @chunkIndex, @pageNumber, @sectionTitle,
             @embDims, @embedding::vector, NOW(), @startTimeSecs, @endTimeSecs)
        ON CONFLICT ("id") DO UPDATE SET
            content = EXCLUDED.content,
            embedding = EXCLUDED.embedding,
            start_time_seconds = EXCLUDED.start_time_seconds,
            end_time_seconds = EXCLUDED.end_time_seconds;
        """;

    await context.Database.ExecuteSqlRawAsync(sql,
        new NpgsqlParameter("id", chunk.Id),
        new NpgsqlParameter("documentId", chunk.DocumentId),
        new NpgsqlParameter("content", chunk.Content),
        new NpgsqlParameter("chunkIndex", chunk.ChunkIndex),
        new NpgsqlParameter("pageNumber", (object?)chunk.PageNumber ?? DBNull.Value),
        new NpgsqlParameter("sectionTitle", (object?)chunk.SectionTitle ?? DBNull.Value),
        new NpgsqlParameter("embDims", chunk.EmbeddingDimensions),
        new NpgsqlParameter("embedding", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Real) { Value = embedding },
        new NpgsqlParameter("startTimeSecs", (object?)(chunk.StartTime?.TotalSeconds) ?? DBNull.Value),
        new NpgsqlParameter("endTimeSecs", (object?)(chunk.EndTime?.TotalSeconds) ?? DBNull.Value));
}
```

- [ ] **Étape 2 : Modifier `SearchAsync` — ajouter les colonnes temporelles**

Remplacer la méthode `SearchAsync` (les nouvelles colonnes sont ajoutées **à la fin** du SELECT, aux ordinaux 10 et 11) :

```csharp
/// <inheritdoc/>
public async Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchAsync(
    float[] vector,
    int topK = 20,
    Guid[]? documentIds = null,
    double scoreThreshold = 0.3,
    CancellationToken ct = default)
{
    var vectorParam = new NpgsqlParameter("vector", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Real)
    {
        Value = vector
    };

    var documentFilter = documentIds?.Length > 0
        ? "AND dc.document_id = ANY(@docIds)"
        : string.Empty;

    // start_time_seconds et end_time_seconds ajoutés en fin de SELECT (ordinaux 10 et 11)
    // pour ne pas décaler les ordinaux existants.
    var sql = $"""
        SELECT dc.id, dc.document_id, dc.content, dc.chunk_index, dc.page_number,
               dc.section_title, dc.embedding_dimensions, dc.created_at,
               1 - (dc.embedding <=> @vector::vector) AS score,
               d.file_name,
               dc.start_time_seconds, dc.end_time_seconds
        FROM document_chunks dc
        JOIN documents d ON d.id = dc.document_id
        WHERE 1 - (dc.embedding <=> @vector::vector) >= @threshold
        {documentFilter}
        ORDER BY score DESC
        LIMIT @topK
        """;

    var parameters = new List<object> { vectorParam,
        new NpgsqlParameter("threshold", scoreThreshold),
        new NpgsqlParameter("topK", topK)
    };

    if (documentIds?.Length > 0)
        parameters.Add(new NpgsqlParameter("docIds", documentIds));

    var results = new List<(DocumentChunk, double)>();

    await using var command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = sql;
    foreach (var p in parameters) command.Parameters.Add(p);

    await context.Database.OpenConnectionAsync(ct);
    await using var reader = await command.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
        var chunk = DocumentChunk.Create(
            documentId: reader.GetGuid(1),
            content: reader.GetString(2),
            chunkIndex: reader.GetInt32(3),
            embeddingDimensions: reader.GetInt32(6),
            pageNumber: reader.IsDBNull(4) ? null : reader.GetInt32(4),
            sectionTitle: reader.IsDBNull(5) ? null : reader.GetString(5),
            documentName: reader.IsDBNull(9) ? null : reader.GetString(9),
            startTime: reader.IsDBNull(10) ? null : TimeSpan.FromSeconds(reader.GetDouble(10)),
            endTime: reader.IsDBNull(11) ? null : TimeSpan.FromSeconds(reader.GetDouble(11)));

        var score = reader.GetDouble(8);
        results.Add((chunk, score));
    }

    return results;
}
```

- [ ] **Étape 3 : Modifier `SearchFullTextAsync` — ajouter les colonnes temporelles**

Remplacer la méthode `SearchFullTextAsync` (même logique, colonnes en fin de SELECT) :

```csharp
/// <inheritdoc/>
public async Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchFullTextAsync(
    string query,
    int topK = 10,
    Guid[]? documentIds = null,
    CancellationToken ct = default)
{
    var documentFilter = documentIds?.Length > 0
        ? "AND dc.document_id = ANY(@docIds)"
        : string.Empty;

    var sql = $"""
        SELECT dc.id, dc.document_id, dc.content, dc.chunk_index, dc.page_number,
               dc.section_title, dc.embedding_dimensions, dc.created_at,
               ts_rank(to_tsvector('french', dc.content), plainto_tsquery('french', @query))::float8 AS score,
               d.file_name,
               dc.start_time_seconds, dc.end_time_seconds
        FROM document_chunks dc
        JOIN documents d ON d.id = dc.document_id
        WHERE to_tsvector('french', dc.content) @@ plainto_tsquery('french', @query)
        {documentFilter}
        ORDER BY score DESC
        LIMIT @topK
        """;

    var parameters = new List<object>
    {
        new NpgsqlParameter("query", query),
        new NpgsqlParameter("topK", topK)
    };

    if (documentIds?.Length > 0)
        parameters.Add(new NpgsqlParameter("docIds", documentIds));

    var results = new List<(DocumentChunk, double)>();

    await using var command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = sql;
    foreach (var p in parameters) command.Parameters.Add(p);

    await context.Database.OpenConnectionAsync(ct);
    await using var reader = await command.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
        var chunk = DocumentChunk.Create(
            documentId: reader.GetGuid(1),
            content: reader.GetString(2),
            chunkIndex: reader.GetInt32(3),
            embeddingDimensions: reader.GetInt32(6),
            pageNumber: reader.IsDBNull(4) ? null : reader.GetInt32(4),
            sectionTitle: reader.IsDBNull(5) ? null : reader.GetString(5),
            documentName: reader.IsDBNull(9) ? null : reader.GetString(9),
            startTime: reader.IsDBNull(10) ? null : TimeSpan.FromSeconds(reader.GetDouble(10)),
            endTime: reader.IsDBNull(11) ? null : TimeSpan.FromSeconds(reader.GetDouble(11)));

        results.Add((chunk, reader.GetDouble(8)));
    }

    return results;
}
```

- [ ] **Étape 4 : Modifier `DocumentChunkConfiguration.cs` — mapper les nouvelles colonnes**

Ajouter avant `builder.HasIndex(c => c.DocumentId)` :

```csharp
builder.Property(c => c.StartTime)
    .HasColumnName("start_time_seconds")
    .HasConversion(
        v => v.HasValue ? v.Value.TotalSeconds : (double?)null,
        v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);

builder.Property(c => c.EndTime)
    .HasColumnName("end_time_seconds")
    .HasConversion(
        v => v.HasValue ? v.Value.TotalSeconds : (double?)null,
        v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);
```

- [ ] **Étape 5 : Compiler**

```powershell
dotnet build "Step 3/src/Back/AIExperience.slnx"
```

Résultat attendu : `Build succeeded. 0 Error(s)`.

- [ ] **Étape 6 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs" \
        "Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/DocumentChunkConfiguration.cs"
git commit -m "feat(infra): persistance et lecture des timestamps temporels dans PgVectorStoreService"
```

---

### Task 5 : TranscribeVideoHandler + RagPipelineService + API DTO

**Files:**
- Modifier : `Step 3/src/Back/AIExperience.Rag.Application/Video/Command/TranscribeVideoHandler.cs`
- Modifier : `Step 3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs`
- Modifier : `Step 3/src/Back/AIExperience.Web.Api/DTOs/ChatDtos.cs`

**Interfaces:**
- Consomme : `IIngestionService.IngestFromSegmentsAsync(...)` (Task 3)
- Consomme : `Citation.Create(..., startTime, endTime)` (Task 1)
- Consomme : `DocumentChunk.StartTime`, `DocumentChunk.EndTime` relus depuis pgvector (Task 4)
- Produit : `CitationResponse` avec `double? StartTimeSeconds`, `double? EndTimeSeconds`

- [ ] **Étape 1 : Modifier `TranscribeVideoHandler.cs` — remplacer IngestTextAsync**

Dans la méthode `Handle(...)`, remplacer le bloc d'ingestion (lignes qui appellent `IngestTextAsync`) :

Remplacer :
```csharp
// Ingérer le texte transcrit (sans re-parsing de fichier)
await _ingestionService.IngestTextAsync(textToIngest, document.Id, metadata, cancellationToken);
```

Par :
```csharp
// Ingestion depuis les segments bruts Whisper pour préserver les timestamps temporels.
// Le texte nettoyé LLM (cleanedText) n'est pas utilisé ici : le nettoyage supprime les timestamps.
await _ingestionService.IngestFromSegmentsAsync(result.Segments, document.Id, metadata, cancellationToken);
```

Et supprimer la variable `textToIngest` qui n'est plus utilisée :
```csharp
// Supprimer cette ligne :
// var textToIngest = cleanedText ?? result.FullText;
```

- [ ] **Étape 2 : Modifier `RagPipelineService.cs` — propager les timestamps dans Citation.Create**

Il y a **trois** appels à `Citation.Create(...)` dans le fichier (dans `AskAsync`, `AskStreamAsync` et `AskFullTextAsync`). Tous doivent être mis à jour.

Remplacer chaque occurrence de :
```csharp
var citations = rankedChunks.Select(r => Citation.Create(
    Guid.Empty, r.Chunk.DocumentId,
    r.Chunk.DocumentName ?? r.Chunk.DocumentId.ToString(),
    r.Chunk.Content[..Math.Min(350, r.Chunk.Content.Length)],
    r.Score, r.Chunk.PageNumber,
    sectionTitle: r.Chunk.SectionTitle,
    chunkIndex: r.Chunk.ChunkIndex)).ToList();
```

Par :
```csharp
var citations = rankedChunks.Select(r => Citation.Create(
    Guid.Empty, r.Chunk.DocumentId,
    r.Chunk.DocumentName ?? r.Chunk.DocumentId.ToString(),
    r.Chunk.Content[..Math.Min(350, r.Chunk.Content.Length)],
    r.Score, r.Chunk.PageNumber,
    sectionTitle: r.Chunk.SectionTitle,
    chunkIndex: r.Chunk.ChunkIndex,
    startTime: r.Chunk.StartTime,
    endTime: r.Chunk.EndTime)).ToList();
```

Et dans `AskFullTextAsync`, remplacer de même :
```csharp
var citations = chunks.Select(r => Citation.Create(
    Guid.Empty, r.Chunk.DocumentId,
    r.Chunk.DocumentName ?? r.Chunk.DocumentId.ToString(),
    r.Chunk.Content[..Math.Min(350, r.Chunk.Content.Length)],
    r.Score, r.Chunk.PageNumber,
    sectionTitle: r.Chunk.SectionTitle,
    chunkIndex: r.Chunk.ChunkIndex)).ToList();
```

Par :
```csharp
var citations = chunks.Select(r => Citation.Create(
    Guid.Empty, r.Chunk.DocumentId,
    r.Chunk.DocumentName ?? r.Chunk.DocumentId.ToString(),
    r.Chunk.Content[..Math.Min(350, r.Chunk.Content.Length)],
    r.Score, r.Chunk.PageNumber,
    sectionTitle: r.Chunk.SectionTitle,
    chunkIndex: r.Chunk.ChunkIndex,
    startTime: r.Chunk.StartTime,
    endTime: r.Chunk.EndTime)).ToList();
```

- [ ] **Étape 3 : Modifier `ChatDtos.cs` — ajouter les timestamps à `CitationResponse`**

Remplacer le record `CitationResponse` :

```csharp
public record CitationResponse(
    string DocumentName,
    int? PageNumber,
    string Excerpt,
    double Score,
    /// <summary>Titre de la section du document source (null si non disponible).</summary>
    string? SectionTitle,
    /// <summary>Position ordinale du chunk dans le document (0-based).</summary>
    int ChunkIndex,
    /// <summary>Début du chunk dans la vidéo source en secondes (null pour les documents non-vidéo).</summary>
    double? StartTimeSeconds,
    /// <summary>Fin du chunk dans la vidéo source en secondes (null pour les documents non-vidéo).</summary>
    double? EndTimeSeconds);
```

- [ ] **Étape 4 : Mettre à jour le mapping Citation → CitationResponse dans `ChatController.cs`**

Il y a **deux** endroits dans `ChatController.cs` qui construisent un `CitationResponse`.

**Ligne 39** (dans `Ask()`) — remplacer :
```csharp
var citations = ragResponse.Citations
    .Select(c => new CitationResponse(c.DocumentName, c.PageNumber, c.Excerpt, c.Score, c.SectionTitle, c.ChunkIndex))
    .ToList();
```
Par :
```csharp
var citations = ragResponse.Citations
    .Select(c => new CitationResponse(c.DocumentName, c.PageNumber, c.Excerpt, c.Score, c.SectionTitle, c.ChunkIndex,
        c.StartTime?.TotalSeconds, c.EndTime?.TotalSeconds))
    .ToList();
```

**Ligne 95-97** (dans `BuildResponse()`) — remplacer :
```csharp
var citations = r.Citations
    .Select(c => new CitationResponse(c.DocumentName, c.PageNumber, c.Excerpt, c.Score, c.SectionTitle, c.ChunkIndex))
    .ToList();
```
Par :
```csharp
var citations = r.Citations
    .Select(c => new CitationResponse(c.DocumentName, c.PageNumber, c.Excerpt, c.Score, c.SectionTitle, c.ChunkIndex,
        c.StartTime?.TotalSeconds, c.EndTime?.TotalSeconds))
    .ToList();
```

- [ ] **Étape 5 : Compiler**

```powershell
dotnet build "Step 3/src/Back/AIExperience.slnx"
```

Résultat attendu : `Build succeeded. 0 Error(s)`.

- [ ] **Étape 6 : Relancer les tests**

```powershell
dotnet test "Step 3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" -v minimal
```

Résultat attendu : `Passed! - Failed: 0, Passed: 6, Skipped: 0`.

- [ ] **Étape 7 : Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Video/Command/TranscribeVideoHandler.cs" \
        "Step 3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs" \
        "Step 3/src/Back/AIExperience.Web.Api/DTOs/ChatDtos.cs" \
        "Step 3/src/Back/AIExperience.Web.Api/Controllers/ChatController.cs"
git commit -m "feat(api): propagation des timestamps vidéo jusqu'aux citations RAG et au DTO API"
```
