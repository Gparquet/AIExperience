using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Rag.Domain.Interfaces.Repositories;

/// <summary>
/// Contrat d'accès aux données pour l'entité <see cref="OutboxMessage"/>.
/// </summary>
public interface IOutboxRepository
{
    /// <summary>
    /// Attache un message au contexte courant, sans l'écrire immédiatement en base — il ne sera
    /// persisté que lors du prochain appel à <c>IUnitOfWork.SaveChangesAsync</c>. Appelé dans le
    /// même handler que la création du document concerné, pour que les deux écritures se
    /// retrouvent dans la même transaction.
    /// </summary>
    /// <param name="message">Message à mettre en file.</param>
    void Add(OutboxMessage message);

    /// <summary>Récupère les messages pas encore traités, triés par ordre d'arrivée.</summary>
    /// <param name="maxCount">Nombre maximum de messages à retourner.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int maxCount, CancellationToken ct = default);

    /// <summary>Persiste l'état courant d'un message (marqué traité, ou tentative en échec enregistrée).</summary>
    /// <param name="message">Message dont l'état a changé.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task UpdateAsync(OutboxMessage message, CancellationToken ct = default);
}
