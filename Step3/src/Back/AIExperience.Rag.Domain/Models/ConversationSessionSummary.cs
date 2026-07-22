namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Projection légère d'une session de conversation pour l'affichage de la liste (sidebar).
/// Évite de charger tous les messages d'une session juste pour peupler la liste.
/// </summary>
/// <param name="Id">Identifiant de la session.</param>
/// <param name="Title">Titre généré depuis la première question.</param>
/// <param name="UpdatedAt">Date de dernière activité (UTC), sert au tri anté-chronologique.</param>
/// <param name="MessageCount">Nombre de messages dans la session.</param>
public sealed record ConversationSessionSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int MessageCount);
