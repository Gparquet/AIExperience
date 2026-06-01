using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Rag.Domain.Interfaces.Repositories;

/// <summary>
/// Contrat d'accès aux données pour l'entité <see cref="Document"/>.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>Récupère un document par son identifiant unique.</summary>
    /// <param name="id">Identifiant du document.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Récupère la liste paginée des documents d'un utilisateur.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="page">Numéro de page (1-based).</param>
    /// <param name="pageSize">Nombre d'éléments par page.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<(IEnumerable<Document> Items, int TotalCount)> GetByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Récupère les documents en attente d'ingestion, triés par date de création.</summary>
    /// <param name="maxCount">Nombre maximum de documents à retourner.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<IEnumerable<Document>> GetPendingDocumentsAsync(int maxCount = 10, CancellationToken ct = default);

    /// <summary>Ajoute un nouveau document en base de données.</summary>
    /// <param name="document">Document à persister.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task AddAsync(Document document, CancellationToken ct = default);

    /// <summary>Met à jour un document existant.</summary>
    /// <param name="document">Document avec les modifications à appliquer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task UpdateAsync(Document document, CancellationToken ct = default);

    /// <summary>Supprime un document et ses chunks associés.</summary>
    /// <param name="id">Identifiant du document à supprimer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
