using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

public sealed class DeleteDocumentHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<DeleteDocumentCommand, bool>
{
    public async Task<bool> Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null)
            return false;

        await documentRepository.DeleteAsync(request.DocumentId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
