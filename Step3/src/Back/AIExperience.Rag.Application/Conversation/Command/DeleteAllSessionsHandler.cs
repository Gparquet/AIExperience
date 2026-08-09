using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Conversation.Command;

public sealed class DeleteAllSessionsHandler(
    IConversationRepository conversationRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteAllSessionsCommand, int>
{
    public async Task<int> HandleAsync(DeleteAllSessionsCommand request, CancellationToken cancellationToken)
    {
        var deletedCount = await conversationRepository.DeleteAllSessionsAsync(request.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return deletedCount;
    }
}
