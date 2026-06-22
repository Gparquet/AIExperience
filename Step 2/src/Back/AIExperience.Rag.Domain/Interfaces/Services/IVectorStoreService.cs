using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Rag.Domain.Interfaces.Services
{
    /// <summary>
    /// Abstraction du magasin vectoriel (pgvector).
    /// Permet de stocker, rechercher et supprimer les embeddings de chunks de documents.
    /// </summary>
    public interface IVectorStoreService
    {
        /// <summary>
        /// Effectue une recherche par similarité cosinus dans pgvector.
        /// </summary>
        /// <param name="vector">Vecteur de requête (embedding de la question ou du document hypothétique HyDE).</param>
        /// <param name="topK">Nombre maximum de résultats à retourner avant reranking.</param>
        /// <param name="documentIds">Filtre optionnel sur les documents à interroger.</param>
        /// <param name="scoreThreshold">Score de similarité minimum (entre 0 et 1).</param>
        /// <param name="ct">Jeton d'annulation.</param>
        /// <returns>Liste de tuples (chunk, score) triée par score décroissant.</returns>
        Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchAsync(
            float[] vector,
            int topK = 20,
            Guid[]? documentIds = null,
            double scoreThreshold = 0.75,
            CancellationToken ct = default);

        /// <summary>
        /// Effectue une recherche full-text PostgreSQL (<c>plainto_tsquery</c>) sans embedding.
        /// Utilisé en mode "sans LLM" pour comparer l'approche classique versus sémantique.
        /// </summary>
        /// <param name="query">Texte de la requête utilisateur.</param>
        /// <param name="topK">Nombre maximum de résultats.</param>
        /// <param name="documentIds">Filtre optionnel sur les documents à interroger.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        /// <returns>Liste de tuples (chunk, score ts_rank) triée par score décroissant.</returns>
        Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchFullTextAsync(
            string query,
            int topK = 10,
            Guid[]? documentIds = null,
            CancellationToken ct = default);

        /// <summary>
        /// Insère ou met à jour l'embedding d'un chunk dans pgvector.
        /// </summary>
        /// <param name="chunk">Chunk à indexer.</param>
        /// <param name="embedding">Vecteur d'embedding associé.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        Task UpsertAsync(DocumentChunk chunk, float[] embedding, CancellationToken ct = default);

        /// <summary>
        /// Supprime tous les embeddings associés à un document (lors de la suppression du document).
        /// </summary>
        /// <param name="documentId">Identifiant du document dont supprimer les vecteurs.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
    }
}
