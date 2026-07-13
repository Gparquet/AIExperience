using System.Diagnostics;
using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Ré-embed le corpus existant avec le rôle <see cref="EmbeddingTaskType.Document"/>
/// pour le rendre compatible avec des requêtes désormais préfixées "search_query: ".
/// Réutilise le contenu déjà chunké : aucune ré-extraction des fichiers sources.
/// </summary>
public sealed class ReembedCorpusHandler(
    IVectorStoreService vectorStoreService,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository) : ICommandHandler<ReembedCorpusCommand, ReembedCorpusResult>
{
    public async Task<ReembedCorpusResult> HandleAsync(ReembedCorpusCommand request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        var chunks = await vectorStoreService.GetAllChunksAsync(cancellationToken);
        if (chunks.Count == 0)
            return new ReembedCorpusResult { ChunksReembedded = 0, DurationMs = sw.ElapsedMilliseconds };

        // Réutilise le sous-batching 96 + résilience Polly déjà en place pour l'ingestion normale.
        var embeddings = await embeddingService.EmbedBatchAsync(
            chunks.Select(c => c.Content), EmbeddingTaskType.Document, cancellationToken);

        if (embeddings.Count != chunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk lors de la ré-ingestion : {embeddings.Count} vecteurs retournés " +
                $"pour {chunks.Count} chunks.");

        // La langue est résolue par document (pas globale au batch, un ré-embed traverse tout le
        // corpus) : cache pour éviter un aller-retour DB par chunk (constat I-6 du plan Lot 2).
        var languageByDocumentId = new Dictionary<Guid, string>();
        var items = new List<(DocumentChunk Chunk, float[] Embedding, string Language)>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (!languageByDocumentId.TryGetValue(chunk.DocumentId, out var language))
            {
                var document = await documentRepository.GetByIdAsync(chunk.DocumentId, cancellationToken);
                language = document?.Metadata.Language ?? "fr";
                languageByDocumentId[chunk.DocumentId] = language;
            }
            items.Add((chunk, embeddings[i], language));
        }

        // Même id, même contenu → UpsertAsync (ON CONFLICT sur "id") ne fait que remplacer l'embedding.
        await vectorStoreService.UpsertBatchAsync(items, cancellationToken);

        sw.Stop();
        return new ReembedCorpusResult { ChunksReembedded = chunks.Count, DurationMs = sw.ElapsedMilliseconds };
    }
}
