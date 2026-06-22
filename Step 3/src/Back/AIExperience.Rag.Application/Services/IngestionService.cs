using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Implémentation de <see cref="IIngestionService"/>.
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
        // 1. Parsing du fichier en texte brut
        var rawText = await compositeTextExtractor.ExtractTextAsync(filePath, ct);

        // 2. Chunking selon la stratégie choisie
        var chunker = CreateChunker(strategy);
        var textChunks = chunker.Chunk(rawText);

        // 3. Embedding + stockage batch dans pgvector (1 transaction pour tous les chunks)
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), ct);

        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));
            return (chunk, embeddings[i]);
        }).ToList<(DocumentChunk, float[])>();

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
        var chunker = CreateChunker(ChunkingStrategy.Recursive);
        var textChunks = chunker.Chunk(text);

        // 2. Embedding + stockage batch dans pgvector (1 transaction pour tous les chunks)
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), ct);

        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));
            return (chunk, embeddings[i]);
        }).ToList<(DocumentChunk, float[])>();

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
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), ct);

        // Stockage batch : 1 transaction pour tous les chunks (vs N commits auto-isolés)
        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                startTime: tc.StartTime,
                endTime: tc.EndTime);
            return (chunk, embeddings[i]);
        }).ToList<(DocumentChunk, float[])>();

        await vectorStoreService.UpsertBatchAsync(items, ct);
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
