using System.Text;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Implémentation de <see cref="IRerankerService"/> basée sur un appel LLM **batché**
/// (correctif R-18/R-5' : remplace l'ancienne version à N appels séquentiels — 1 par chunk — par
/// 1 seul appel listant tous les extraits candidats). Le LLM attribue un score de pertinence (0-10)
/// à chaque extrait numéroté ; ce score sémantique corrige les faux positifs du cosinus sans
/// réintroduire la latence catastrophique (~N × durée d'un appel LLM) que le batching supprime.
/// En cas d'erreur globale ou d'extrait absent de la réponse, le score cosinus original est
/// conservé pour ce(s) chunk(s) (dégradation gracieuse).
/// </summary>
public sealed class LlmRerankerService(
    IChatClient chatClient,
    ILogger<LlmRerankerService> logger) : IRerankerService
{
    // Troncature par extrait plus courte qu'en mode séquentiel (800 car.) : plusieurs extraits
    // partagent désormais le même prompt, il faut rester dans la fenêtre de contexte du modèle local.
    private const int MaxCharsPerChunkInBatch = 400;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> RerankAsync(
        string question,
        IReadOnlyList<(DocumentChunk Chunk, double Score)> chunks,
        int topK,
        CancellationToken ct = default)
    {
        if (chunks.Count == 0)
            return [];

        var prompt = RagPrompts.RerankerBatch
            .Replace("{question}", question)
            .Replace("{chunks}", BuildNumberedChunksBlock(chunks));

        IReadOnlyDictionary<int, double> parsedScores;
        try
        {
            var response = await chatClient.GetResponseAsync(
                prompt,
                // Temperature = 0 pour des scores déterministes ; MaxOutputTokens dimensionné
                // sur le nombre d'extraits (une ligne "N:score" ≈ quelques tokens chacune).
                new ChatOptions { MaxOutputTokens = Math.Clamp(chunks.Count * 10 + 50, 100, 1000), Temperature = 0 },
                ct);

            parsedScores = BatchedRerankResponseParser.Parse(response.Text);
        }
        catch (Exception ex)
        {
            // Dégradation gracieuse à l'échelle du lot : l'appel unique a échoué, on préserve
            // l'ordre et les scores cosinus d'origine plutôt que de faire échouer le pipeline.
            logger.LogWarning(ex, "Reranker batché : échec de l'appel LLM. Fallback sur les scores cosinus.");
            return chunks.OrderByDescending(c => c.Score).Take(topK).ToList();
        }

        var missingIndexes = new List<int>();
        var scored = new List<(DocumentChunk Chunk, double Score)>(chunks.Count);

        foreach (var (chunk, originalScore, index) in chunks.Select((c, i) => (c.Chunk, c.Score, i + 1)))
        {
            if (parsedScores.TryGetValue(index, out var llmScore))
                scored.Add((chunk, llmScore));
            else
            {
                missingIndexes.Add(index);
                scored.Add((chunk, originalScore));
            }
        }

        if (missingIndexes.Count > 0)
            logger.LogWarning(
                "Reranker batché : {Count} extrait(s) absent(s) de la réponse LLM (indices {Indexes}). Fallback sur le score cosinus pour ceux-ci.",
                missingIndexes.Count, string.Join(",", missingIndexes));

        return scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Construit le bloc "[Extrait N]\n{contenu tronqué}" pour chaque chunk, dans l'ordre reçu.
    /// La numérotation (1-based) est celle attendue par <see cref="BatchedRerankResponseParser"/>.
    /// </summary>
    private static string BuildNumberedChunksBlock(IReadOnlyList<(DocumentChunk Chunk, double Score)> chunks)
    {
        var builder = new StringBuilder();

        foreach (var (chunk, _, index) in chunks.Select((c, i) => (c.Chunk, c.Score, i + 1)))
        {
            builder.AppendLine($"[Extrait {index}]");
            builder.AppendLine(chunk.Content[..Math.Min(MaxCharsPerChunkInBatch, chunk.Content.Length)]);
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
