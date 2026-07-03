using System.Diagnostics;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Ré-embed le corpus existant avec le rôle <see cref="EmbeddingTaskType.Document"/>
/// pour le rendre compatible avec des requêtes désormais préfixées "search_query: ".
/// Réutilise le contenu déjà chunké : aucune ré-extraction des fichiers sources.
/// </summary>
public sealed class ReembedCorpusHandler(
    IVectorStoreService vectorStoreService,
    IEmbeddingService embeddingService) : IRequestHandler<ReembedCorpusCommand, ReembedCorpusResult>
{
    public async Task<ReembedCorpusResult> Handle(ReembedCorpusCommand request, CancellationToken cancellationToken)
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

        // Même id, même contenu → UpsertAsync (ON CONFLICT sur "id") ne fait que remplacer l'embedding.
        var items = chunks.Select((chunk, i) => (chunk, embeddings[i])).ToList();
        await vectorStoreService.UpsertBatchAsync(items, cancellationToken);

        sw.Stop();
        return new ReembedCorpusResult { ChunksReembedded = chunks.Count, DurationMs = sw.ElapsedMilliseconds };
    }
}
