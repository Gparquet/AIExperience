using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente un message échangé dans une session de conversation RAG.
/// Peut être un message utilisateur ou une réponse générée par le modèle.
/// </summary>
public sealed record ChatMessage
{
    /// <summary>Identifiant unique du message.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Identifiant de la session de conversation parente.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>Rôle de l'auteur du message (User, Assistant ou System).</summary>
    public MessageRole Role { get; private set; }

    /// <summary>Contenu textuel du message.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Nombre de tokens consommés pour ce message (input + output).</summary>
    public int TokensUsed { get; private set; }

    /// <summary>Stratégie RAG utilisée pour générer la réponse (null pour les messages utilisateur).</summary>
    public RagStrategy? StrategyUsed { get; private set; }

    /// <summary>Durée totale de génération de la réponse en millisecondes.</summary>
    public long DurationMs { get; private set; }

    /// <summary>Date et heure de création du message (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation vers la session parente.</summary>
    public ConversationSession Session { get; private set; } = null!;

    /// <summary>Citations des sources utilisées pour générer la réponse.</summary>
    public ICollection<Citation> Citations { get; private set; } = [];

    private ChatMessage() { }

    /// <summary>
    /// Crée un message de type utilisateur.
    /// </summary>
    /// <param name="sessionId">Identifiant de la session parente.</param>
    /// <param name="content">Contenu de la question posée par l'utilisateur.</param>
    public static ChatMessage CreateUserMessage(Guid sessionId, string content)
    {
        return new ChatMessage
        {
            SessionId = sessionId,
            Role = MessageRole.User,
            Content = content
        };
    }

    /// <summary>
    /// Crée un message de type assistant (réponse RAG générée).
    /// </summary>
    /// <param name="sessionId">Identifiant de la session parente.</param>
    /// <param name="content">Contenu de la réponse générée.</param>
    /// <param name="tokensUsed">Nombre de tokens consommés.</param>
    /// <param name="strategyUsed">Stratégie RAG utilisée.</param>
    /// <param name="durationMs">Durée de génération en millisecondes.</param>
    public static ChatMessage CreateAssistantMessage(
        Guid sessionId,
        string content,
        int tokensUsed,
        RagStrategy strategyUsed,
        long durationMs)
    {
        return new ChatMessage
        {
            SessionId = sessionId,
            Role = MessageRole.Assistant,
            Content = content,
            TokensUsed = tokensUsed,
            StrategyUsed = strategyUsed,
            DurationMs = durationMs
        };
    }
}
