using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.Video;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;
using Microsoft.Extensions.Options;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Implémentation de <see cref="IIngestionService"/>.
/// Orchestre le pipeline complet d'ingestion : parsing → chunking → embedding → stockage pgvector.
/// Le chunker est injecté par DI pour pouvoir être remplacé ou testé sans modifier ce service.
/// </summary>
public sealed class IngestionService(
    ICompositeTextExtractor compositeTextExtractor,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService,
    ITemporalChunker temporalChunker,
    ITextChunker textChunker,
    ILanguageDetectionService languageDetectionService,
    IVideoProcessorService videoProcessorService,
    ITranscriptionService transcriptionService,
    IIngestionProgressReporter progressReporter,
    IOptions<IngestionOptions> ingestionOptions) : IIngestionService
{
    /// <inheritdoc/>
    public async Task IngestAsync(
        string filePath,
        Guid documentId,
        DocumentMetadata metadata,
        ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        CancellationToken ct = default)
    {
        // 1. Extraction page par page, préserve PageNumber si l'extracteur le supporte.
        await progressReporter.EnterStageAsync(documentId, IngestionStage.ExtractingText, ct);
        var pages = await compositeTextExtractor.ExtractPagesAsync(filePath, ct);

        // 2. Chunking avec propagation du numéro de page
        await progressReporter.EnterStageAsync(documentId, IngestionStage.Chunking, ct);
        var textChunks = textChunker.ChunkPages(pages);

        // Garde-fou : 0 chunk = extraction vide → le document serait persisté sans contenu interrogeable.
        // Lever une exception plutôt que persister un document "Completed" sans contenu.
        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId}. " +
                "L'extraction de texte a retourné un contenu vide ou non découpable.");

        // 2bis. Détection de la langue du document (I-6) : utilisée pour indexer content_tsv
        // avec le bon dictionnaire Postgres, et persistée sur le document pour traçabilité.
        var sampleText = string.Join("\n", pages.Select(p => p.Text));
        var detectedLanguage = languageDetectionService.Detect(sampleText);

        var document = await documentRepository.GetByIdAsync(documentId, ct);
        if (document is not null)
        {
            document.SetDetectedLanguage(detectedLanguage);
            await documentRepository.UpdateAsync(document, ct);
        }

        // 3. Embedding en sous-lots (avec avancement) + stockage batch dans pgvector.
        var embeddings = await EmbedInBatchesAsync(documentId, textChunks.Select(c => c.Content).ToList(), ct);

        // Vérification de cohérence avant l'accès indexé embeddings[i].
        // Le service d'embedding DOIT retourner autant de vecteurs que de textes soumis.
        if (embeddings.Count != textChunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk : {embeddings.Count} vecteurs retournés " +
                $"pour {textChunks.Count} chunks (document {documentId}).");

        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));
            return (chunk, embeddings[i], detectedLanguage);
        }).ToList<(DocumentChunk, float[], string)>();

        await progressReporter.EnterStageAsync(documentId, IngestionStage.Storing, ct);
        await vectorStoreService.UpsertBatchAsync(items, ct);
    }

    /// <inheritdoc/>
    public async Task IngestTextAsync(
        string text,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default)
    {
        // 1. Chunking du texte brut (l'étape d'extraction est déjà faite — transcription)
        await progressReporter.EnterStageAsync(documentId, IngestionStage.Chunking, ct);
        var textChunks = textChunker.Chunk(text);

        // Garde-fou (même logique que IngestAsync) : texte vide ou non découpable.
        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId} (IngestTextAsync). " +
                "Le texte fourni est vide ou entièrement non découpable.");

        // 2. Embedding en sous-lots (avec avancement) + stockage batch dans pgvector.
        var embeddings = await EmbedInBatchesAsync(documentId, textChunks.Select(c => c.Content).ToList(), ct);

        // Vérification de cohérence avant l'accès indexé embeddings[i].
        if (embeddings.Count != textChunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk : {embeddings.Count} vecteurs retournés " +
                $"pour {textChunks.Count} chunks (IngestTextAsync, document {documentId}).");

        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));
            return (chunk, embeddings[i], metadata.Language);
        }).ToList<(DocumentChunk, float[], string)>();

        await progressReporter.EnterStageAsync(documentId, IngestionStage.Storing, ct);
        await vectorStoreService.UpsertBatchAsync(items, ct);
    }

    /// <inheritdoc/>
    public async Task IngestFromSegmentsAsync(
        IReadOnlyList<TranscriptionSegment> segments,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default)
    {
        // Chunking temporel : respecte les frontières des segments Whisper et préserve les timestamps
        await progressReporter.EnterStageAsync(documentId, IngestionStage.Chunking, ct);
        var textChunks = temporalChunker.ChunkSegments(segments);

        // Garde-fou : une vidéo silencieuse ou corrompue ne produit aucun segment, donc aucun chunk.
        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId} (IngestFromSegmentsAsync). " +
                "La transcription ne contient aucun segment (vidéo silencieuse ou corrompue ?).");

        var embeddings = await EmbedInBatchesAsync(documentId, textChunks.Select(c => c.Content).ToList(), ct);

        // Vérification de cohérence avant l'accès indexé embeddings[i].
        if (embeddings.Count != textChunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk : {embeddings.Count} vecteurs retournés " +
                $"pour {textChunks.Count} chunks (IngestFromSegmentsAsync, document {documentId}).");

        // Stockage batch : 1 transaction pour tous les chunks (vs N commits auto-isolés)
        // Langue déjà connue via Whisper (portée par les métadonnées transmises à l'appel).
        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                startTime: tc.StartTime,
                endTime: tc.EndTime);
            return (chunk, embeddings[i], metadata.Language);
        }).ToList<(DocumentChunk, float[], string)>();

        await progressReporter.EnterStageAsync(documentId, IngestionStage.Storing, ct);
        await vectorStoreService.UpsertBatchAsync(items, ct);
    }

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
            if (isVideo)
                await progressReporter.EnterStageAsync(documentId, IngestionStage.ExtractingAudio, ct);
            var audioPath = isVideo
                ? await videoProcessorService.ExtractAudioAsync(filePath, tempAudioPath!, ct)
                : filePath;

            // 2. Transcription Whisper avec langue paramétrable (corrige la langue figée "fr" de l'ancien VideoTextExtractor).
            await progressReporter.EnterStageAsync(documentId, IngestionStage.Transcribing, ct);
            var totalDuration = await videoProcessorService.TryGetMediaDurationAsync(audioPath, ct) ?? TimeSpan.Zero;
            var result = await transcriptionService.TranscribeAsync(audioPath, language,
                onSegment: seg => ReportTranscriptionProgress(documentId, seg, totalDuration), ct);

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

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid documentId, CancellationToken ct = default)
        => await vectorStoreService.DeleteByDocumentIdAsync(documentId, ct);

    private static string CleanString(string? input) => input?.Replace("\0", string.Empty) ?? string.Empty;

    /// <summary>
    /// Vectorise les chunks en sous-lots (taille configurable) en rapportant l'avancement « lot i/n »
    /// après chaque sous-lot. Retourne les vecteurs dans l'ordre des textes d'entrée.
    /// </summary>
    private async Task<IReadOnlyList<float[]>> EmbedInBatchesAsync(
        Guid documentId, IReadOnlyList<string> texts, CancellationToken ct)
    {
        await progressReporter.EnterStageAsync(documentId, IngestionStage.Embedding, ct);

        var batches = IngestionBatching.Split(texts.Count, ingestionOptions.Value.EmbeddingBatchSize);
        var result = new float[texts.Count][];

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var slice = new List<string>(batch.Count);
            for (var j = 0; j < batch.Count; j++)
                slice.Add(texts[batch.Start + j]);

            var vectors = await embeddingService.EmbedBatchAsync(slice, EmbeddingTaskType.Document, ct);
            for (var j = 0; j < batch.Count; j++)
                result[batch.Start + j] = vectors[j];

            var chunksDone = batch.Start + batch.Count;
            var percent = texts.Count == 0 ? 100 : (int)(100.0 * chunksDone / texts.Count);
            await progressReporter.ReportAsync(documentId, IngestionStage.Embedding, percent,
                new IngestionProgressCounters(
                    BatchIndex: i + 1, BatchCount: batches.Count,
                    ChunksDone: chunksDone, ChunksTotal: texts.Count), ct);
        }

        return result;
    }

    /// <summary>
    /// Convertit un segment Whisper en avancement de transcription : pourcentage basé sur la fin du
    /// segment rapportée à la durée totale (si connue), sinon <c>null</c> (indéterminé). Best-effort,
    /// synchrone côté appelant Whisper : on ne bloque pas la boucle de transcription.
    /// </summary>
    private void ReportTranscriptionProgress(Guid documentId, TranscriptionSegment segment, TimeSpan totalDuration)
    {
        int? percent = totalDuration > TimeSpan.Zero
            ? Math.Clamp((int)(100.0 * segment.End.TotalSeconds / totalDuration.TotalSeconds), 0, 99)
            : null;

        // Fire-and-forget contrôlé : le report est best-effort et ne doit pas ralentir Whisper.
        _ = progressReporter.ReportAsync(documentId, IngestionStage.Transcribing, percent,
            new IngestionProgressCounters(SegmentsDone: null, SegmentsTotal: null));
    }
}
