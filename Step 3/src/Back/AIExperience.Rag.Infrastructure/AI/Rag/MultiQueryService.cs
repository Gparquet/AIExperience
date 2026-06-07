using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Implémentation de <see cref="IMultiQueryService"/>.
/// Génère N reformulations de la question via le LLM pour maximiser la couverture sémantique
/// lors de la phase de récupération (stratégie RAG-Fusion).
/// Chaque reformulation couvre un angle différent, ce qui permet de trouver des chunks
/// que la formulation originale aurait manqués.
/// </summary>
public sealed class MultiQueryService(
    IChatClient chatClient,
    ILogger<MultiQueryService> logger) : IMultiQueryService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GenerateVariantsAsync(
        string question,
        int variantCount,
        CancellationToken ct = default)
    {
        var prompt = RagPrompts.MultiQuery
            .Replace("{count}", variantCount.ToString())
            .Replace("{question}", question);

        var response = await chatClient.GetResponseAsync(
            prompt,
            new ChatOptions { MaxOutputTokens = 300, Temperature = 0.5f },
            ct);

        // Le LLM retourne une reformulation par ligne
        var variants = response.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(variantCount)
            .ToList();

        if (variants.Count == 0)
        {
            // Fallback garanti : si le LLM retourne vide, on réutilise la question originale
            logger.LogWarning(
                "MultiQueryService n'a généré aucune variante pour la question '{Question}'. Fallback sur la question originale.",
                question);
            return [question];
        }

        return variants;
    }
}
