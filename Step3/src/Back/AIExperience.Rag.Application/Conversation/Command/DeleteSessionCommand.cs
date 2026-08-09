using AIExperience.Rag.Application.Common.Cqrs;

namespace AIExperience.Rag.Application.Conversation.Command;

public sealed record DeleteSessionCommand : ICommand<bool>
{
    public required Guid SessionId { get; init; }

    /// <summary>Utilisateur courant — la suppression n'est autorisée que sur ses propres sessions.</summary>
    public required string UserId { get; init; }
}
