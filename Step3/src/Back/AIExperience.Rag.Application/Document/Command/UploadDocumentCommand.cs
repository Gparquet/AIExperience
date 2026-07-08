using AIExperience.Rag.Domain.Enums;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

public sealed record UploadDocumentCommand : IRequest<UploadDocumentResponse>
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string UserId { get; init; }
    public required DocumentMetadata DocumentMetadata { get; init; }
    public ChunkingStrategy ChunkingStrategy { get; init; } = ChunkingStrategy.Recursive;

    /// <summary>Chemin du fichier temporaire déjà reçu, utilisé par le handler pour calculer le hash de contenu (détection de doublon).</summary>
    public required string FilePath { get; init; }

    /// <summary>Identifiant du document existant à remplacer, si l'utilisateur a confirmé le remplacement d'un doublon.</summary>
    public Guid? ReplaceDocumentId { get; init; }
}
