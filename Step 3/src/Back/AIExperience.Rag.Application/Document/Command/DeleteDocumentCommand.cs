using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

public sealed record DeleteDocumentCommand : IRequest<bool>
{
    public required Guid DocumentId { get; init; }
}
