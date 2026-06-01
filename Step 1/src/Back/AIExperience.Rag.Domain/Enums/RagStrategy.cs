namespace AIExperience.Rag.Domain.Enums;

/// <summary>
/// Définit la stratégie de recherche utilisée par le pipeline RAG.
/// </summary>
public enum RagStrategy
{
    /// <summary>
    /// Recherche directe : embed la question et cherche les chunks les plus proches.
    /// Rapide, adapté aux questions simples et précises.
    /// </summary>
    Direct,

    /// <summary>
    /// HyDE (Hypothetical Document Embeddings) : le LLM génère d'abord un document hypothétique
    /// répondant à la question, puis son embedding est utilisé pour la recherche vectorielle.
    /// Améliore la précision sur les questions complexes.
    /// </summary>
    HyDE,

    /// <summary>
    /// RAG-Fusion : génère N reformulations de la question, effectue N recherches parallèles,
    /// puis fusionne les résultats via Reciprocal Rank Fusion (RRF).
    /// Adapté aux questions nécessitant une comparaison ou une synthèse.
    /// </summary>
    Fusion,

    /// <summary>
    /// Adaptatif : analyse automatiquement la complexité de la question et choisit
    /// la stratégie la plus appropriée (Direct, HyDE ou Fusion).
    /// </summary>
    Adaptive
}

