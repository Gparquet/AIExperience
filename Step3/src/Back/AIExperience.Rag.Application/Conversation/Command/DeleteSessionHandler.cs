using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Conversation.Command;

public sealed class DeleteSessionHandler(
    IConversationRepository conversationRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteSessionCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteSessionCommand request, CancellationToken cancellationToken)
    {
        // Isolation par utilisateur : une session inexistante OU appartenant à un autre utilisateur
        // n'est pas supprimée — même règle que l'endpoint de lecture GetSession.
        var session = await conversationRepository.GetSessionByIdAsync(request.SessionId, cancellationToken);
        if (session is null || session.UserId != request.UserId)
            return false;

        await conversationRepository.DeleteSessionAsync(request.SessionId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
