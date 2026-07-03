using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Service de génération d'embeddings vectoriels
/// Utilisé aussi bien lors de l'ingestion (vectorisation des chunks) que lors de la recherche (vectorisation de la question).
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Génère le vecteur d'embedding pour un texte donné.
    /// </summary>
    /// <param name="text">Texte à vectoriser.</param>
    /// <param name="taskType">
    /// Rôle du texte (corpus indexé ou requête). Obligatoire : certains modèles (nomic-embed-text) sont
    /// asymétriques et appliquent un préfixe différent selon ce rôle (voir R-15) — un oubli silencieux
    /// dégrade fortement la pertinence de la recherche.
    /// </param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Vecteur de float de dimension 3072.</returns>
    Task<float[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken ct = default);

    /// <summary>
    /// Génère les vecteurs d'embedding pour un lot de textes en une seule requête.
    /// Plus efficace que plusieurs appels à <see cref="EmbedAsync"/> pour l'ingestion de chunks.
    /// </summary>
    /// <param name="texts">Textes à vectoriser.</param>
    /// <param name="taskType">Rôle des textes (corpus indexé ou requête) — voir <see cref="EmbedAsync"/>.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Liste de vecteurs dans le même ordre que les textes en entrée.</returns>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, EmbeddingTaskType taskType, CancellationToken ct = default);
}
