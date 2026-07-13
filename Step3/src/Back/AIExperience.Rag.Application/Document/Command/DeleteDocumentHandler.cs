using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Document.Command;

public sealed class DeleteDocumentHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteDocumentCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null)
            return false;

        await documentRepository.DeleteAsync(request.DocumentId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
