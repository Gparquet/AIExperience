using AIExperience.Rag.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente un document uploadé par un utilisateur dans le système RAG.
/// Contient les métadonnées du fichier ainsi que son statut d'ingestion.
/// </summary>
public class Document
{
    /// <summary>Identifiant unique du document.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Nom du fichier original (ex: GBCP_2024.pdf).</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Type MIME du fichier (ex: application/pdf).</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>Taille du fichier en octets.</summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>Empreinte SHA-256 (hex, 64 caractères) du contenu du fichier, utilisée pour la détection de doublon.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>Identifiant de l'utilisateur propriétaire du document.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Statut actuel du pipeline d'ingestion.</summary>
    public IngestionStatus Status { get; private set; } = IngestionStatus.Pending;

    /// <summary>Stratégie de chunking utilisée lors de l'ingestion.</summary>
    public ChunkingStrategy ChunkingStrategy { get; private set; } = ChunkingStrategy.Recursive;

    /// <summary>Métadonnées enrichies du document (titre, auteur, nb pages...).</summary>
    public DocumentMetadata Metadata { get; private set; } = DocumentMetadata.Empty;

    /// <summary>Référence au fichier physique dans le stockage (chemin ou clé blob).</summary>
    public string? FileReference { get; private set; }

    /// <summary>Message d'erreur en cas d'échec de l'ingestion.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Transcription brute produite par Whisper (documents vidéo/audio uniquement) ; conservée pour être consultée après coup, une fois le traitement terminé.</summary>
    public string? RawTranscription { get; private set; }

    /// <summary>Version de la transcription nettoyée par le LLM local, si l'utilisateur a activé cette option lors de l'import ; absente sinon.</summary>
    public string? CleanedTranscription { get; private set; }

    /// <summary>Si vrai (défaut), le contenu est chunké/embeddé et devient interrogeable dans le RAG. À faux pour une simple transcription à la demande, sans indexation.</summary>
    public bool IndexInRag { get; private set; } = true;

    /// <summary>Demande, pour un document vidéo/audio, que la transcription brute soit également nettoyée par le LLM local pour l'affichage (n'affecte pas le texte réellement indexé).</summary>
    public bool CleanTranscriptionWithLlm { get; private set; }

    /// <summary>Date et heure de création du document (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Date et heure de la dernière mise à jour (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Collection des chunks vectorisés générés lors de l'ingestion.</summary>
    public ICollection<DocumentChunk> Chunks { get; private set; } = [];

    private Document() { }

    /// <summary>
    /// Crée une nouvelle instance de <see cref="Document"/>.
    /// </summary>
    /// <param name="fileName">Nom du fichier original.</param>
    /// <param name="contentType">Type MIME du fichier.</param>
    /// <param name="fileSizeBytes">Taille du fichier en octets.</param>
    /// <param name="userId">Identifiant de l'utilisateur propriétaire.</param>
    /// <param name="metadata">Métadonnées enrichies du document.</param>
    /// <param name="chunkingStrategy">Stratégie de découpage à appliquer lors de l'ingestion.</param>
    /// <param name="contentHash">Empreinte SHA-256 du contenu du fichier (détection de doublon).</param>
    /// <param name="id">
    /// Identifiant à imposer au document, si l'appelant doit le connaître avant même l'insertion
    /// en base (par exemple pour nommer un fichier de travail sur disque avant de créer la ligne
    /// correspondante). Laissé à <c>null</c>, un nouvel identifiant est généré normalement.
    /// </param>
    /// <param name="indexInRag">Si faux, le document ne sera pas chunké/embeddé (simple transcription à la demande, sans indexation).</param>
    /// <param name="cleanTranscriptionWithLlm">Pour un document vidéo/audio, demande le nettoyage LLM de la transcription pour l'affichage.</param>
    public static Document Create(
        string fileName,
        string contentType,
        long fileSizeBytes,
        string userId,
        DocumentMetadata metadata,
        ChunkingStrategy chunkingStrategy = ChunkingStrategy.Recursive,
        string contentHash = "",
        Guid? id = null,
        bool indexInRag = true,
        bool cleanTranscriptionWithLlm = false)
    {
        var document = new Document
        {
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            UserId = userId,
            Metadata = metadata,
            ChunkingStrategy = chunkingStrategy,
            ContentHash = contentHash,
            IndexInRag = indexInRag,
            CleanTranscriptionWithLlm = cleanTranscriptionWithLlm
        };

        if (id is not null)
            document.Id = id.Value;

        return document;
    }

    /// <summary>
    /// Définit la référence de stockage physique du fichier.
    /// </summary>
    /// <param name="fileReference">Chemin ou clé blob du fichier stocké.</param>
    public void SetFileReference(string fileReference)
    {
        FileReference = fileReference;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Met à jour la langue détectée du document (constat I-6 du plan Lot 2) : appelée après
    /// détection automatique sur le texte extrait, pour que <c>content_tsv</c> soit indexé
    /// avec le bon dictionnaire Postgres au lieu de "french" figé.
    /// </summary>
    /// <param name="language">Code ISO 639-1 détecté (ex. "fr", "en").</param>
    public void SetDetectedLanguage(string language)
    {
        Metadata = Metadata with { Language = language };
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Passe le statut du document à <see cref="IngestionStatus.Processing"/>.
    /// </summary>
    public void MarkAsProcessing()
    {
        Status = IngestionStatus.Processing;
        ErrorMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Passe le statut du document à <see cref="IngestionStatus.Completed"/>.
    /// </summary>
    public void MarkAsCompleted()
    {
        Status = IngestionStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Passe le statut du document à <see cref="IngestionStatus.Failed"/> et enregistre le message d'erreur.
    /// </summary>
    /// <param name="errorMessage">Description de l'erreur survenue.</param>
    public void MarkAsFailed(string errorMessage)
    {
        Status = IngestionStatus.Failed;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Réinitialise le document à <see cref="IngestionStatus.Pending"/> pour une nouvelle tentative d'ingestion.
    /// </summary>
    public void ResetToPending()
    {
        Status = IngestionStatus.Pending;
        ErrorMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Enregistre le texte transcrit une fois la transcription terminée, pour qu'il reste
    /// consultable a posteriori même si la réponse HTTP initiale n'attend pas la fin du traitement.
    /// </summary>
    /// <param name="rawTranscription">Texte brut produit par la transcription.</param>
    /// <param name="cleanedTranscription">Version nettoyée par le LLM, ou <c>null</c> si l'option n'a pas été demandée.</param>
    public void SetTranscription(string rawTranscription, string? cleanedTranscription)
    {
        RawTranscription = rawTranscription;
        CleanedTranscription = cleanedTranscription;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}