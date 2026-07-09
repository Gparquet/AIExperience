using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;
using System.Text.Json;

namespace AIExperience.Rag.Application.Video.Command;

/// <summary>
/// Handler pour <see cref="CreateVideoTranscriptionJobCommand"/> : persiste le document et met en
/// file son job de transcription dans la même transaction, sur le même principe que
/// <see cref="Document.Command.UploadDocumentHandler"/> pour les documents classiques — sans
/// détection de doublon, qui n'a jamais fait partie du parcours d'import vidéo.
/// </summary>
public sealed class CreateVideoTranscriptionJobHandler(
    IDocumentRepository documentRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateVideoTranscriptionJobCommand, CreateVideoTranscriptionJobResponse>
{
    /// <summary>UserId fixe en développement — doit correspondre au DefaultUserId de DocumentsController.</summary>
    private const string DevUserId = "1ea95468-3f27-4a6d-8fb3-25fdd1530023";

    public async Task<CreateVideoTranscriptionJobResponse> Handle(CreateVideoTranscriptionJobCommand request, CancellationToken cancellationToken)
    {
        var title = request.Title ?? Path.GetFileNameWithoutExtension(request.FileName);
        var metadata = DocumentMetadata.Create(title: title, language: request.Language);

        var document = Domain.Entities.Document.Create(
            request.FileName,
            request.ContentType,
            request.FileSizeBytes,
            DevUserId,
            metadata,
            id: request.Id,
            indexInRag: request.IndexInRag,
            cleanTranscriptionWithLlm: request.CleanWithLlm);

        document.SetFileReference(request.FileReference);
        await documentRepository.AddAsync(document, cancellationToken);

        // Le job de transcription est mis en file dans la même transaction que la création du
        // document : soit les deux sont enregistrés ensemble, soit aucun des deux ne l'est.
        var payload = JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = document.Id });
        outboxRepository.Add(OutboxMessage.Create(IngestionEventTypes.VideoTranscriptionRequested, payload));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateVideoTranscriptionJobResponse
        {
            DocumentId = document.Id,
            Status = document.Status,
            FileName = document.FileName,
            CreatedAt = document.CreatedAt
        };
    }
}
