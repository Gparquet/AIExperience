using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Rag.Domain.Interfaces.Services.AI;

/// <summary>
/// Reclasse les chunks candidats selon leur pertinence réelle par rapport à la question.
/// Corrige les faux positifs produits par la similarité cosinus pure :
/// un chunk peut être géométriquement proche sans répondre à la question.
/// </summary>
public interface IRerankerService
{
    /// <summary>
    /// Évalue la pertinence de chaque chunk par rapport à la question via le LLM,
    /// puis retourne les <paramref name="topK"/> meilleurs dans l'ordre décroissant de pertinence.
    /// En cas d'erreur LLM sur un chunk, le score cosinus original est conservé comme fallback.
    /// </summary>
    /// <param name="question">La question de l'utilisateur.</param>
    /// <param name="chunks">Les chunks candidats issus de la recherche vectorielle.</param>
    /// <param name="topK">Nombre maximum de chunks à conserver après reclassement.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Sous-liste des chunks reclassés, du plus pertinent au moins pertinent.</returns>
    Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> RerankAsync(
        string question,
        IReadOnlyList<(DocumentChunk Chunk, double Score)> chunks,
        int topK,
        CancellationToken ct = default);
}
