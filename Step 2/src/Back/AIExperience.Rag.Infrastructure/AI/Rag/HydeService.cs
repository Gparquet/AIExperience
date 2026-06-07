using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Implémentation de <see cref="IHydeService"/>.
/// Génère un document hypothétique via le LLM (HyDE = Hypothetical Document Embeddings).
/// L'embedding du document fictif est utilisé à la place de la question brute pour la recherche vectorielle,
/// car il est géométriquement plus proche des vrais chunks que la question courte de l'utilisateur.
/// </summary>
public sealed class HydeService(
    IChatClient chatClient,
    IOptions<RagOptions> options) : IHydeService
{
    /// <inheritdoc/>
    public async Task<string> GenerateHypotheticalDocAsync(string question, CancellationToken ct = default)
    {
        var length = options.Value.HyDE.HypotheticalDocLength;

        var prompt = RagPrompts.Hyde
            .Replace("{length}", length.ToString())
            .Replace("{question}", question);

        var response = await chatClient.GetResponseAsync(
            prompt,
            new ChatOptions { MaxOutputTokens = length + 50, Temperature = 0.3f },
            ct);

        return response.Text.Trim();
    }
}
