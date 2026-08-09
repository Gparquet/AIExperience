using AIExperience.Rag.Application.Common.Cqrs;

namespace AIExperience.Rag.Application.Conversation.Command;

public sealed record DeleteAllSessionsCommand : ICommand<int>
{
    public required string UserId { get; init; }
}
