namespace AIExperience.Rag.Domain.Enums;

/// <summary>
/// Étapes fines du pipeline d'ingestion, exposées à l'utilisateur pour lui montrer « où ça en est ».
/// Toutes les étapes ne s'appliquent pas à tous les types de documents : un PDF passe par
/// <see cref="ExtractingText"/>, une vidéo par <see cref="ExtractingAudio"/> puis <see cref="Transcribing"/>.
/// </summary>
public enum IngestionStage
{
    /// <summary>En file d'attente : le message outbox n'a pas encore été dépilé par le worker.</summary>
    Queued,
    /// <summary>Extraction de la piste audio d'une vidéo via FFmpeg (vidéo uniquement).</summary>
    ExtractingAudio,
    /// <summary>Transcription audio → texte via Whisper (vidéo/audio) — étape à pourcentage.</summary>
    Transcribing,
    /// <summary>Extraction du texte d'un document (PDF, etc.) — documents non vidéo.</summary>
    ExtractingText,
    /// <summary>Découpage du texte en chunks.</summary>
    Chunking,
    /// <summary>Vectorisation des chunks en sous-lots — étape à pourcentage.</summary>
    Embedding,
    /// <summary>Écriture des chunks + vecteurs dans pgvector.</summary>
    Storing,
    /// <summary>Pipeline terminé avec succès.</summary>
    Completed,
    /// <summary>Pipeline interrompu par une erreur.</summary>
    Failed
}
