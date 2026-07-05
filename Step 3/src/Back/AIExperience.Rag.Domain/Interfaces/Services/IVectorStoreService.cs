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
        /// Recherche lexicale PostgreSQL en sémantique OR : un chunk matche dès qu'il contient
        /// au moins un des mots-clés de la requête (contrairement à <see cref="SearchFullTextAsync"/>
        /// qui exige la présence de TOUS les mots via <c>plainto_tsquery</c>, trop strict pour une
        /// question en langage naturel qui ne partage souvent qu'un ou deux mots avec le chunk pertinent).
        /// Utilisée comme signal lexical complémentaire à la recherche vectorielle dans la fusion hybride RRF.
        /// </summary>
        /// <param name="query">Texte de la requête utilisateur.</param>
        /// <param name="topK">Nombre maximum de résultats.</param>
        /// <param name="documentIds">Filtre optionnel sur les documents à interroger.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        /// <returns>Liste de tuples (chunk, score ts_rank) triée par score décroissant.</returns>
        Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchLexicalAsync(
            string query,
            int topK = 10,
            Guid[]? documentIds = null,
            CancellationToken ct = default);

        /// <summary>
        /// Insère ou met à jour l'embedding d'un chunk dans pgvector.
        /// </summary>
        /// <param name="chunk">Chunk à indexer.</param>
        /// <param name="embedding">Vecteur d'embedding associé.</param>
        /// <param name="language">
        /// Code ISO 639-1 de la langue du document (ex. "fr") — détermine le dictionnaire Postgres
        /// utilisé pour indexer content_tsv (constat I-6 du plan Lot 2).
        /// </param>
        /// <param name="ct">Jeton d'annulation.</param>
        Task UpsertAsync(DocumentChunk chunk, float[] embedding, string language, CancellationToken ct = default);

        /// <summary>
        /// Persiste plusieurs chunks et leurs embeddings en une seule transaction PostgreSQL.
        /// Équivalent à N appels <see cref="UpsertAsync"/> mais avec un seul commit — ~5-10× plus rapide.
        /// La langue est portée par item (pas globale au batch) car un même appel peut mélanger
        /// des chunks de documents différents (ex. ré-ingestion corpus complet).
        /// </summary>
        /// <param name="items">Liste de tuples (chunk, embedding, langue ISO) à insérer.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        Task UpsertBatchAsync(
            IReadOnlyList<(DocumentChunk Chunk, float[] Embedding, string Language)> items,
            CancellationToken ct = default);

        /// <summary>
        /// Supprime tous les embeddings associés à un document (lors de la suppression du document).
        /// </summary>
        /// <param name="documentId">Identifiant du document dont supprimer les vecteurs.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default);

        /// <summary>
        /// Récupère tous les chunks du corpus (contenu + métadonnées, sans leur embedding).
        /// Utilisé pour la ré-ingestion (R-15) : ré-embed le contenu déjà extrait/chunké sans repasser
        /// par l'extraction de texte source.
        /// </summary>
        /// <param name="ct">Jeton d'annulation.</param>
        /// <returns>Tous les chunks du corpus, sans filtre.</returns>
        Task<IReadOnlyList<DocumentChunk>> GetAllChunksAsync(CancellationToken ct = default);
    }
}
