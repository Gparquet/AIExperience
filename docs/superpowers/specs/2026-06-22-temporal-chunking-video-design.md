# Spec — Chunking temporel pour l'ingestion vidéo (Step 3)

**Date :** 2026-06-22  
**Auteur :** Geoffrey  
**Statut :** Approuvé

---

## Contexte

La Step 3 implémente un pipeline d'ingestion vidéo : FFmpeg extrait la piste audio, Whisper.net transcrit en segments horodatés, puis le texte est découpé par `RecursiveChunker` (découpage par taille de caractères) et stocké dans pgvector.

**Problème :** `RecursiveChunker` ignore les timestamps Whisper. Les chunks RAG n'ont aucune notion de position temporelle dans la vidéo. Quand l'utilisateur demande "à quelle minute parle-t-on de X ?", le RAG est incapable de répondre avec un repère temporel.

---

## Objectif

Remplacer le chunking par caractères par un **chunking temporel** qui :
1. Respecte les frontières naturelles des segments Whisper
2. Stocke `StartTime`/`EndTime` dans chaque chunk en base de données
3. Propage ces timestamps jusqu'aux citations RAG et au DTO API

---

## Décisions de conception

| Décision | Choix retenu | Raison |
|----------|-------------|--------|
| Stratégie de chunking | Par segment Whisper (regroupement jusqu'à taille max) | Segments = frontières phonétiques naturelles, plus précis que fenêtre fixe ou détection de pause |
| Overlap | Désactivé pour les chunks temporels | Les timestamps deviendraient ambigus si un segment apparaît dans deux chunks avec des plages différentes |
| Persistance des timestamps | `double precision` (secondes) sur `document_chunks` | Simple, universel, pas de conversion lossy |
| Timestamps sur `Citation` | `[NotMapped]` — propagés à la volée depuis le chunk | Cohérent avec le pattern existant de `SectionTitle` et `ChunkIndex` |
| Texte nettoyé LLM vs segments | On ingère toujours depuis les segments bruts | Le nettoyage LLM supprime les timestamps — la position temporelle est plus précieuse que la propreté du texte pour le RAG |
| Rétrocompatibilité | Toutes nouvelles propriétés nullable | Les chunks PDF/HTML existants ne sont pas impactés |

---

## Architecture — fichiers impactés

### 1. Domain (`AIExperience.Rag.Domain`)

**`Models/TextChunk.cs`** — ajout :
```csharp
public TimeSpan? StartTime { get; init; }
public TimeSpan? EndTime { get; init; }
```

**`Entities/DocumentChunk.cs`** — ajout :
```csharp
public TimeSpan? StartTime { get; private set; }
public TimeSpan? EndTime { get; private set; }
```
Et paramètres optionnels `startTime`/`endTime` dans `DocumentChunk.Create(...)`.

**`Entities/Citation.cs`** — ajout en `[NotMapped]` :
```csharp
[NotMapped] public TimeSpan? StartTime { get; private set; }
[NotMapped] public TimeSpan? EndTime { get; private set; }
```
Et paramètres optionnels `startTime`/`endTime` dans `Citation.Create(...)`.

**`Interfaces/Services/ITemporalChunker.cs`** — nouvelle interface :
```csharp
public interface ITemporalChunker
{
    IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 800);
}
```

**`Interfaces/Services/IIngestionService.cs`** — ajout :
```csharp
Task IngestFromSegmentsAsync(
    IReadOnlyList<TranscriptionSegment> segments,
    Guid documentId,
    DocumentMetadata metadata,
    CancellationToken ct = default);
```

---

### 2. Application (`AIExperience.Rag.Application`)

**`Services/TemporalChunker.cs`** — nouvelle classe, implémente `ITemporalChunker` :

Algorithme :
1. Itérer les segments Whisper dans l'ordre
2. Accumuler les segments dans un buffer tant que `buffer.Length + segment.Text.Length ≤ maxCharsPerChunk`
3. À saturation ou fin : créer un `TextChunk` avec :
   - `Content` = texte avec timestamps inline (`[HH:MM:SS → HH:MM:SS] texte...`)
   - `StartTime` = `.Start` du premier segment du buffer
   - `EndTime` = `.End` du dernier segment du buffer
4. Recommencer buffer vide

**`Services/IngestionService.cs`** — implémentation de `IngestFromSegmentsAsync` :
1. `ITemporalChunker.ChunkSegments(segments)`
2. Embed batch
3. `DocumentChunk.Create(..., startTime: tc.StartTime, endTime: tc.EndTime)`
4. `IVectorStoreService.UpsertAsync(...)`

**`Video/Command/TranscribeVideoHandler.cs`** — dans `if (request.AutoIngest)` :
- Remplacer `_ingestionService.IngestTextAsync(textToIngest, ...)` par `_ingestionService.IngestFromSegmentsAsync(result.Segments, ...)`
- Aucune dépendance nouvelle dans le handler — `ITemporalChunker` est injecté dans `IngestionService`, pas dans le handler

---

### 3. Infrastructure (`AIExperience.Rag.Infrastructure`)

**`Persistence/Configuration/DocumentChunkConfiguration.cs`** — mapper les nouvelles colonnes :
```csharp
builder.Property(x => x.StartTime)
    .HasColumnName("start_time_seconds")
    .HasConversion(
        v => v.HasValue ? v.Value.TotalSeconds : (double?)null,
        v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);

builder.Property(x => x.EndTime)
    .HasColumnName("end_time_seconds")
    .HasConversion(
        v => v.HasValue ? v.Value.TotalSeconds : (double?)null,
        v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);
```

Note : `PgVectorStoreService` utilise du SQL brut (pas EF Core) pour les INSERT et les recherches vectorielles. Les colonnes `start_time_seconds`/`end_time_seconds` devront être lues et écrites manuellement dans le SQL brut de ce service. La configuration EF Core ci-dessus s'applique uniquement aux requêtes passant par `AppDbContext`.

**`VectorStore/PgVectorStoreService.cs`** — lire `start_time_seconds` et `end_time_seconds` dans la requête SQL de recherche, les affecter au `DocumentChunk` retourné.

**`AI/Rag/RagPipelineService.cs`** — lors de `Citation.Create(...)`, passer `startTime: chunk.StartTime` et `endTime: chunk.EndTime`.

**`DependencyInjection.cs`** (Application) — enregistrer le nouveau service :
```csharp
services.AddSingleton<ITemporalChunker, TemporalChunker>();
```

---

### 4. Web API (`AIExperience.Web.Api`)

**`DTOs/ChatDtos.cs`** — dans `CitationDto` :
```csharp
public double? StartTimeSeconds { get; init; }
public double? EndTimeSeconds { get; init; }
```

---

### 5. Base de données — script de migration

Script à exécuter manuellement (ne modifie pas `init.sql`) :

```sql
-- Migration : ajout des timestamps temporels sur les chunks vidéo
-- Colonnes nullable : aucun chunk existant n'est impacté
ALTER TABLE document_chunks
    ADD COLUMN IF NOT EXISTS start_time_seconds double precision NULL,
    ADD COLUMN IF NOT EXISTS end_time_seconds double precision NULL;
```

---

## Flux de données complet (après implémentation)

```
Vidéo MP4
  → FFmpegVideoProcessorService   : extraction WAV 16kHz mono
  → WhisperTranscriptionService   : TranscriptionResult { Segments: [{ Start, End, Text }...] }
  → TemporalChunker               : TextChunk[] { Content (avec timestamps), StartTime, EndTime }
  → OpenAIEmbeddingService        : float[][] embeddings
  → PgVectorStoreService          : INSERT document_chunks (content, start_time_seconds, end_time_seconds, embedding)
```

```
Question utilisateur
  → RagPipelineService            : recherche vectorielle pgvector
  → DocumentChunk[] { StartTime, EndTime }
  → Citation.Create(startTime, endTime)
  → CitationDto { StartTimeSeconds, EndTimeSeconds }
  → Front : affichage "[3 min 42 → 4 min 15]" ou lien cliquable
```

---

## Ce qui n'est PAS dans cette spec

- Modification de `RecursiveChunker` (inchangé pour PDF/HTML)
- Nettoyage LLM robuste pour longues vidéos (sujet distinct)
- Détection automatique de langue (sujet distinct)
- Lien cliquable vers la vidéo dans le front (hors scope — dépend de la disponibilité de la vidéo côté client)
