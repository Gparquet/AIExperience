using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.Video;
using AIExperience.Rag.Domain.Models.Video;

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
    ITranscriptionService transcriptionService) : IIngestionService
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
        var pages = await compositeTextExtractor.ExtractPagesAsync(filePath, ct);

        // 2. Chunking avec propagation du numéro de page
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

        // 3. Embedding + stockage batch dans pgvector (1 transaction pour tous les chunks)
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);

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
        var textChunks = textChunker.Chunk(text);

        // Garde-fou (même logique que IngestAsync) : texte vide ou non découpable.
        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId} (IngestTextAsync). " +
                "Le texte fourni est vide ou entièrement non découpable.");

        // 2. Embedding + stockage batch dans pgvector (1 transaction pour tous les chunks)
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);

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
        var textChunks = temporalChunker.ChunkSegments(segments);

        // Garde-fou : une vidéo silencieuse ou corrompue ne produit aucun segment, donc aucun chunk.
        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId} (IngestFromSegmentsAsync). " +
                "La transcription ne contient aucun segment (vidéo silencieuse ou corrompue ?).");

        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);

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

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid documentId, CancellationToken ct = default)
        => await vectorStoreService.DeleteByDocumentIdAsync(documentId, ct);

    private static string CleanString(string? input) => input?.Replace("\0", string.Empty) ?? string.Empty;
}
