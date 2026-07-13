using AIExperience.Rag.Application.Common.Cqrs;

namespace AIExperience.Rag.Application.Video.Command;

/// <summary>
/// Exécute la transcription d'un document vidéo/audio déjà créé via
/// <see cref="CreateVideoTranscriptionJobCommand"/> : extraction audio (si nécessaire),
/// transcription Whisper, nettoyage LLM optionnel, indexation RAG optionnelle. Ne transporte que
/// l'identifiant : tout le reste (fichier, langue, options) est relu depuis la ligne <c>Document</c>,
/// pour que ce traitement soit rejouable à l'identique après une reprise.
/// </summary>
public sealed record ProcessVideoTranscriptionJobCommand : ICommand<ProcessVideoTranscriptionJobResponse>
{
    /// <summary>Identifiant du document déjà créé en base de données.</summary>
    public required Guid DocumentId { get; init; }
}
