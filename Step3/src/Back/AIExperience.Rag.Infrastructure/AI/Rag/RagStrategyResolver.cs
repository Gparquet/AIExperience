using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Infrastructure.Options;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Applique les flags <c>Enabled</c> de <see cref="RagOptions"/> à une stratégie déjà résolue
/// (constat R-17 du plan Lot 0-bis) : avant ce correctif, le routeur Adaptive pouvait sélectionner
/// HyDE ou Fusion même si l'opérateur les avait désactivés dans la configuration, rendant les flags
/// inopérants. Extrait en méthode statique pure (sans dépendance injectée) pour rester testable
/// sans mock, comme <see cref="BatchedRerankResponseParser"/>.
/// </summary>
public static class RagStrategyResolver
{
    /// <summary>
    /// Replie la stratégie sur <see cref="RagStrategy.Direct"/> si la technique correspondante est
    /// désactivée en configuration. Ne modifie pas les autres stratégies (Direct, DirectLlm, Adaptive
    /// déjà résolu vers Direct/HyDE/Fusion par l'appelant).
    /// </summary>
    /// <param name="resolvedStrategy">Stratégie déjà résolue (après routage Adaptive éventuel).</param>
    /// <param name="options">Options RAG courantes, contenant les flags HyDE/MultiQuery.</param>
    /// <returns>La stratégie à utiliser réellement, repliée sur Direct si nécessaire.</returns>
    public static RagStrategy ResolveFallback(RagStrategy resolvedStrategy, RagOptions options)
    {
        if (resolvedStrategy == RagStrategy.HyDE && !options.HyDE.Enabled)
            return RagStrategy.Direct;

        if (resolvedStrategy == RagStrategy.Fusion && !options.MultiQuery.Enabled)
            return RagStrategy.Direct;

        return resolvedStrategy;
    }
}
