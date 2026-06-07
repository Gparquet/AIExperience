using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Implémentation de <see cref="IIngestionService"/>.
/// Orchestre le pipeline complet d'ingestion : parsing → chunking → embedding → stockage pgvector.
/// </summary>
public sealed class IngestionService(
    ICompositeTextExtractor compositeTextExtractor,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService) : IIngestionService
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

        // 3. Embedding + stockage dans pgvector pour chaque chunk
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), ct);

        for (int i = 0; i < textChunks.Count; i++)
        {
            var tc = textChunks[i];
            // La dimension est déduite dynamiquement depuis le vecteur généré
            var embeddingDimensions = embeddings[i].Length;

            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddingDimensions,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle)
            );

            await vectorStoreService.UpsertAsync(chunk, embeddings[i], ct);
        }
    }

    string CleanString(string input) => input?.Replace("\0", string.Empty);

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid documentId, CancellationToken ct = default)
        => await vectorStoreService.DeleteByDocumentIdAsync(documentId, ct);

    private ITextChunker CreateChunker(ChunkingStrategy chunkingStrategy) => chunkingStrategy switch
    {
        ChunkingStrategy.Recursive => new RecursiveChunker(),
        _ => new RecursiveChunker()
    };
}
