using MediatR;

namespace AIExperience.Rag.Application.Video.Command;

/// <summary>
/// Crée la ligne <c>Document</c> pour une vidéo ou un fichier audio déjà reçu sur disque, et met en
/// file le job de transcription qui sera exécuté en arrière-plan par le worker d'ingestion. Rapide
/// par construction — la transcription elle-même (potentiellement longue) est laissée au job.
/// </summary>
public sealed record CreateVideoTranscriptionJobCommand : IRequest<CreateVideoTranscriptionJobResponse>
{
    /// <summary>Nom du fichier original.</summary>
    public required string FileName { get; init; }

    /// <summary>Type MIME du fichier.</summary>
    public required string ContentType { get; init; }

    /// <summary>Taille du fichier en octets.</summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>Chemin, dans le répertoire de travail durable, où le fichier a déjà été écrit.</summary>
    public required string FileReference { get; init; }

    /// <summary>Code langue ISO pour guider la transcription Whisper.</summary>
    public string Language { get; init; } = "fr";

    /// <summary>Demande le nettoyage de la transcription par le LLM local pour l'affichage (n'affecte pas le contenu indexé).</summary>
    public bool CleanWithLlm { get; init; }

    /// <summary>Si faux, le document est transcrit mais pas indexé dans le RAG (simple transcription à la demande).</summary>
    public bool IndexInRag { get; init; } = true;

    /// <summary>Titre du document ; à défaut, le nom de fichier sans extension.</summary>
    public string? Title { get; init; }

    /// <summary>Identifiant à imposer au document, déjà généré par le contrôleur pour nommer le fichier de travail.</summary>
    public Guid? Id { get; init; }
}
