using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Interfaces.Services.AI;

public interface IAdaptiveQueryRouter
{
    /// <summary>
    /// Analyse la question via le LLM et retourne la stratégie RAG la plus adaptée.
    /// </summary>
    /// <param name="question">Question posée par l'utilisateur.</param>
    /// <param name="ct">Token d'annulation.</param>
    /// <returns>La <see cref="RagStrategy"/> déterminée par le LLM.</returns>

    Task<RagStrategy> GetRagStrategyAsync(string question, CancellationToken ct = default);
}
