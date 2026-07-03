using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Polly;

namespace AIExperience.Rag.Infrastructure.AI.Embedding;

/// <summary>
/// Implémentation de <see cref="IEmbeddingService"/> via le générateur d'embeddings Microsoft.Extensions.AI.
/// Compatible avec OpenAI, AzureOpenAI, Ollama et tout provider branché dans la DI.
///
/// Utilise un sous-batching de 96 textes par appel pour respecter les limites des providers
/// et éviter les rejets ou timeouts sur les gros documents.
/// La résilience est assurée via un <see cref="ResiliencePipeline"/> injectable : retry exponentiel
/// par défaut en production, <see cref="ResiliencePipeline.Empty"/> dans les tests.
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbeddingService
{
    /// <summary>
    /// Nombre maximal de textes par appel au provider.
    /// OpenAI accepte jusqu'à 2048, mais 96 est un compromis sûr pour Ollama et LM Studio.
    /// </summary>
    internal const int BatchSize = 96;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly AiProviderOptions _aiProviderOptions;

    /// <summary>
    /// Initialise le service avec un générateur d'embeddings et un pipeline de résilience optionnel.
    /// </summary>
    /// <param name="embeddingGenerator">Générateur d'embeddings injecté par la DI.</param>
    /// <param name="aiProviderOptions">Options du provider IA (contrôle notamment <see cref="AiProviderOptions.EmbeddingTaskPrefixes"/>).</param>
    /// <param name="resiliencePipeline">
    /// Pipeline de résilience Polly (retry, backoff). Defaults à <see cref="ResiliencePipeline.Empty"/>
    /// quand omis (pratique pour les tests unitaires sans retry).
    /// En production, injecter un pipeline configuré via <c>AddResiliencePipeline</c> dans la DI.
    /// </param>
    public OpenAIEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<AiProviderOptions> aiProviderOptions,
        ResiliencePipeline? resiliencePipeline = null)
    {
        _embeddingGenerator = embeddingGenerator;
        _aiProviderOptions = aiProviderOptions.Value;
        _resiliencePipeline = resiliencePipeline ?? ResiliencePipeline.Empty;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken ct = default)
    {
        var prefixedText = ApplyTaskPrefix(text, taskType);
        var result = await _resiliencePipeline.ExecuteAsync(
            async token => await _embeddingGenerator.GenerateAsync(prefixedText, cancellationToken: token),
            ct);
        return result.Vector.ToArray();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Divise la liste en sous-lots de <see cref="BatchSize"/> textes maximum pour éviter
    /// les rejets (400/413) et timeouts des providers locaux. Les résultats sont concaténés dans l'ordre d'entrée.
    /// Chaque appel est wrappé dans le <see cref="ResiliencePipeline"/>
    /// (retry exponentiel + jitter sur erreurs transitoires 429/503).
    /// </remarks>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        EmbeddingTaskType taskType,
        CancellationToken ct = default)
    {
        var all = texts.Select(t => ApplyTaskPrefix(t, taskType)).ToList();
        if (all.Count == 0) return [];

        var result = new List<float[]>(all.Count);

        // LINQ Chunk découpe la liste en sous-listes de BatchSize éléments.
        foreach (var batch in all.Chunk(BatchSize))
        {
            var batchArray = batch; // capture pour la lambda Polly
            var embeddings = await _resiliencePipeline.ExecuteAsync(
                async token => await _embeddingGenerator.GenerateAsync(batchArray, cancellationToken: token),
                ct);
            result.AddRange(embeddings.Select(e => e.Vector.ToArray()));
        }

        return result;
    }

    /// <summary>
    /// Préfixe le texte selon son rôle (corpus vs requête) si <see cref="AiProviderOptions.EmbeddingTaskPrefixes"/>
    /// est activé (R-15). Sans ce préfixe, un modèle asymétrique comme nomic-embed-text-v1.5 discrimine
    /// mal les chunks pertinents des chunks hors-sujet.
    /// </summary>
    private string ApplyTaskPrefix(string text, EmbeddingTaskType taskType)
    {
        if (!_aiProviderOptions.EmbeddingTaskPrefixes) return text;

        var prefix = taskType == EmbeddingTaskType.Document ? "search_document: " : "search_query: ";
        return prefix + text;
    }
}
