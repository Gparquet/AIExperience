namespace AIExperience.Rag.Domain.Interfaces.Services.AI;

/// <summary>
/// Génère plusieurs reformulations sémantiquement équivalentes d'une question utilisateur.
/// Utilisé par la stratégie RAG-Fusion : chaque reformulation donne lieu à une recherche vectorielle
/// indépendante, et les résultats sont fusionnés via Reciprocal Rank Fusion (RRF).
/// </summary>
public interface IMultiQueryService
{
    /// <summary>
    /// Génère <paramref name="variantCount"/> reformulations différentes de la question.
    /// Chaque variante couvre un angle sémantique distinct pour maximiser la couverture lors de la recherche.
    /// En cas d'échec LLM, retourne au minimum la question originale (fallback garanti).
    /// </summary>
    /// <param name="question">La question originale de l'utilisateur.</param>
    /// <param name="variantCount">Nombre de reformulations à générer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Liste des reformulations générées (au moins un élément).</returns>
    Task<IReadOnlyList<string>> GenerateVariantsAsync(
        string question,
        int variantCount,
        CancellationToken ct = default);
}
