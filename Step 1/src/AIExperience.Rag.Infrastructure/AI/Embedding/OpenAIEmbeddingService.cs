using AIExperience.Rag.Domain.Interfaces.Services;
using Microsoft.Extensions.AI;

namespace AIExperience.Rag.Infrastructure.AI.Embedding;


/// <summary>
/// Implémentation de <see cref="IEmbeddingService"/> via Azure OpenAI (text-embedding-3-large).
/// Utilise <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> de Microsoft.Extensions.AI.
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;

    public OpenAIEmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        this.embeddingGenerator = embeddingGenerator;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await embeddingGenerator.GenerateAsync(text, cancellationToken: ct);
        return result.Vector.ToArray();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = await embeddingGenerator.GenerateAsync(texts, cancellationToken: ct);
        return results.Select(r => r.Vector.ToArray()).ToList();
    }
}
