namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente une session de conversation entre un utilisateur et le système RAG.
/// Regroupe l'historique des messages échangés.
/// </summary>
public sealed record ConversationSession
{
    /// <summary>Identifiant unique de la session.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Identifiant de l'utilisateur propriétaire de la session.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Titre descriptif de la session (généré à partir de la première question).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Date et heure de création de la session (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Date et heure du dernier message reçu ou envoyé (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Collection ordonnée des messages de la conversation.</summary>
    public ICollection<ChatMessage> Messages { get; private set; } = [];

    private ConversationSession() { }

    /// <summary>
    /// Crée une nouvelle session de conversation.
    /// </summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="title">Titre initial de la session.</param>
    public static ConversationSession Create(string userId, string title)
    {
        return new ConversationSession
        {
            UserId = userId,
            Title = title
        };
    }

    /// <summary>
    /// Met à jour le titre de la session.
    /// </summary>
    /// <param name="title">Nouveau titre.</param>
    public void UpdateTitle(string title)
    {
        Title = title;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Met à jour la date de dernière activité de la session.
    /// </summary>
    public void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
