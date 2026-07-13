using AIExperience.Rag.Application.Common.Cqrs;

namespace AIExperience.Rag.Application.Document.Command;

public sealed record DeleteDocumentCommand : ICommand<bool>
{
    public required Guid DocumentId { get; init; }
}
