using AIExperience.Rag.Application.Video.Command;
using MediatR;

namespace AIExperience.Rag.Application.Video
{
    /// <summary>
    /// Commande pour transcrire une vidéo et l'injecter dans le pipeline RAG.
    /// </summary>
    public sealed record TranscribeVideoCommand : IRequest<TranscribeVideoResponse>
    {
        /// <summary>Chemin vers le fichier vidéo ou audio uploadé</summary>
        public required string FilePath { get; init; }

        /// <summary>Langue de la transcription (défaut: "fr")</summary>
        public string Language { get; init; } = "fr";

        /// <summary>Nettoyer la transcription via LLM local (supprimer hésitations, structurer)</summary>
        public bool CleanWithLlm { get; init; } = true;

        /// <summary>Injecter automatiquement dans le pipeline d'ingestion RAG</summary>
        public bool AutoIngest { get; init; } = true;

        /// <summary>Titre du document (pour les métadonnées)</summary>
        public string? Title { get; init; }
    }
}
