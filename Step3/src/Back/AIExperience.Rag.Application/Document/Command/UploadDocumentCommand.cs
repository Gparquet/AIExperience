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

    /// <summary>Chemin du fichier déjà reçu, utilisé par le handler pour calculer le hash de contenu (détection de doublon) puis conservé comme référence de travail pour l'ingestion en arrière-plan.</summary>
    public required string FilePath { get; init; }

    /// <summary>Identifiant du document existant à remplacer, si l'utilisateur a confirmé le remplacement d'un doublon.</summary>
    public Guid? ReplaceDocumentId { get; init; }

    /// <summary>Identifiant à imposer au document créé, si le contrôleur l'a déjà généré pour nommer le fichier de travail avant même cet appel.</summary>
    public Guid? Id { get; init; }
}
