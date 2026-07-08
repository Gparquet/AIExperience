namespace AIExperience.Rag.Domain.Interfaces.Services;

public interface IUnitOfWork
{
    /// <summary>
    /// Valide toutes les modifications en attente dans une seule transaction de base de données.
    /// </summary>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Nombre d'entités affectées.</returns>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
