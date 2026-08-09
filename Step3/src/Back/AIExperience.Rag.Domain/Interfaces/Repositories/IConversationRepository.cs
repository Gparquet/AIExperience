using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Domain.Interfaces.Repositories;

/// <summary>
/// Contrat d'accès aux données pour les entités <see cref="ConversationSession"/> et <see cref="ChatMessage"/>.
/// </summary>
public interface IConversationRepository
{
    /// <summary>Récupère une session de conversation par son identifiant.</summary>
    /// <param name="sessionId">Identifiant de la session.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<ConversationSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Récupère toutes les sessions d'un utilisateur, triées par date de dernière activité.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<IEnumerable<ConversationSession>> GetSessionsByUserIdAsync(string userId, CancellationToken ct = default);

    /// <summary>Récupère les résumés des sessions d'un utilisateur (sans charger les messages), triés par activité récente.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<IReadOnlyList<ConversationSessionSummary>> GetSessionSummariesAsync(string userId, CancellationToken ct = default);

    /// <summary>Récupère les N derniers messages d'une session, triés par date croissante.</summary>
    /// <param name="sessionId">Identifiant de la session.</param>
    /// <param name="maxTurns">Nombre maximum de messages à retourner.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<IEnumerable<ChatMessage>> GetMessagesAsync(Guid sessionId, int maxTurns = 20, CancellationToken ct = default);

    /// <summary>Persiste une nouvelle session de conversation.</summary>
    /// <param name="session">Session à créer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task AddSessionAsync(ConversationSession session, CancellationToken ct = default);

    /// <summary>Ajoute un message à une session existante.</summary>
    /// <param name="message">Message à persister.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task AddMessageAsync(ChatMessage message, CancellationToken ct = default);

    /// <summary>Met à jour une session existante (titre, date de mise à jour).</summary>
    /// <param name="session">Session avec les modifications à appliquer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task UpdateSessionAsync(ConversationSession session, CancellationToken ct = default);

    /// <summary>Supprime une session (les messages et citations associés sont supprimés en cascade côté base).</summary>
    /// <param name="sessionId">Identifiant de la session à supprimer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns><c>false</c> si la session n'existe pas.</returns>
    Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Supprime toutes les sessions d'un utilisateur (cascade sur messages et citations).</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Nombre de sessions supprimées.</returns>
    Task<int> DeleteAllSessionsAsync(string userId, CancellationToken ct = default);
}
