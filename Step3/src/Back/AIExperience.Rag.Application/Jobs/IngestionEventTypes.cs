namespace AIExperience.Rag.Application.Jobs;

/// <summary>
/// Noms des événements écrits dans la table outbox par les commandes de création
/// (documents, transcription vidéo), et relus par le worker pour savoir quelle commande
/// exécuter une fois le job dépilé.
/// </summary>
public static class IngestionEventTypes
{
    /// <summary>Un document vient d'être créé et attend son ingestion (extraction → chunking → embedding → stockage).</summary>
    public const string DocumentIngestionRequested = "document-ingestion-requested";

    /// <summary>Une vidéo ou un fichier audio vient d'être reçu et attend sa transcription.</summary>
    public const string VideoTranscriptionRequested = "video-transcription-requested";
}
