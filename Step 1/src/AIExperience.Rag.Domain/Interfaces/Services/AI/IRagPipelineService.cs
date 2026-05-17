using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Domain.Interfaces.Services.AI
{
    /// <summary>
    /// Orchestrateur principal du pipeline RAG.
    /// Reçoit une <see cref="RagQuery"/> et retourne une <see cref="RagResponse"/> avec la réponse et les citations.
    /// Sélectionne et exécute la stratégie RAG appropriée (Direct, HyDE, Fusion, Adaptive).
    /// </summary>
    public interface IRagPipelineService
    {
        /// <summary>
        /// Exécute le pipeline RAG complet pour une question donnée.
        /// </summary>
        /// <param name="query">Requête contenant la question, les documents cibles et la stratégie.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        /// <returns>Réponse générée avec citations et métriques d'exécution.</returns>
        Task<RagResponse> AskAsync(RagQuery query, CancellationToken ct = default);
    }
}
