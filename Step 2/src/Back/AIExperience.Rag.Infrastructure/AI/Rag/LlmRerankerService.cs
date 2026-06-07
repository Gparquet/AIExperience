using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Implémentation de <see cref="IRerankerService"/> basée sur un appel LLM.
/// Pour chaque chunk candidat, le LLM attribue un score de pertinence (0-10) par rapport à la question.
/// Ce score sémantique est plus précis que la similarité cosinus, mais coûte N appels LLM supplémentaires.
/// En cas d'erreur sur un chunk, le score cosinus original est conservé (dégradation gracieuse).
/// </summary>
public sealed class LlmRerankerService(
    IChatClient chatClient,
    ILogger<LlmRerankerService> logger) : IRerankerService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> RerankAsync(
        string question,
        IReadOnlyList<(DocumentChunk Chunk, double Score)> chunks,
        int topK,
        CancellationToken ct = default)
    {
        var scored = new List<(DocumentChunk Chunk, double Score)>(chunks.Count);

        foreach (var (chunk, originalScore) in chunks)
        {
            var prompt = RagPrompts.Reranker
                .Replace("{question}", question)
                .Replace("{chunk}", chunk.Content[..Math.Min(800, chunk.Content.Length)]);

            try
            {
                var response = await chatClient.GetResponseAsync(
                    prompt,
                    // Temperature = 0 pour des scores déterministes et reproductibles
                    new ChatOptions { MaxOutputTokens = 5, Temperature = 0 },
                    ct);

                // Le LLM retourne un entier 0-10, normalisé en 0.0-1.0
                if (int.TryParse(response.Text.Trim(), out var score))
                    scored.Add((chunk, score / 10.0));
                else
                {
                    logger.LogWarning(
                        "Reranker : réponse non parsable '{Response}' pour le chunk {ChunkId}. Fallback sur le score cosinus.",
                        response.Text, chunk.Id);
                    scored.Add((chunk, originalScore));
                }
            }
            catch (Exception ex)
            {
                // Dégradation gracieuse : en cas d'erreur LLM, on préserve le score cosinus
                logger.LogWarning(ex, "Reranker : erreur lors de l'évaluation du chunk {ChunkId}. Fallback sur le score cosinus.", chunk.Id);
                scored.Add((chunk, originalScore));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
    }
}
