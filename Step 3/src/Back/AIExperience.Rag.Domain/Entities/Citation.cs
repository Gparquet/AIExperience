using System.ComponentModel.DataAnnotations.Schema;

namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente une citation d'une source utilisée pour générer une réponse RAG.
/// Permet de traçer et afficher les références document/page ayant servi au modèle.
/// </summary>
public sealed record Citation
{
    /// <summary>Identifiant unique de la citation.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Identifiant du message assistant auquel cette citation est rattachée.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>Identifiant du document source.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Nom du document source (pour affichage).</summary>
    public string DocumentName { get; private set; } = string.Empty;

    /// <summary>Numéro de page du document où se trouve l'extrait (null si non applicable).</summary>
    public int? PageNumber { get; private set; }

    /// <summary>Extrait textuel du chunk ayant servi de contexte.</summary>
    public string Excerpt { get; private set; } = string.Empty;

    /// <summary>Score de similarité cosinus entre la question et ce chunk (entre 0 et 1).</summary>
    public double Score { get; private set; }

    /// <summary>
    /// Titre de la section du document source — non persisté, renseigné à la volée depuis le chunk.
    /// </summary>
    [NotMapped]
    public string? SectionTitle { get; private set; }

    /// <summary>
    /// Position ordinale du chunk dans le document — non persisté, utile pour le débogage et l'affichage.
    /// </summary>
    [NotMapped]
    public int ChunkIndex { get; private set; }

    /// <summary>
    /// Début du chunk dans la vidéo source — non persisté, propagé depuis DocumentChunk.
    /// Null pour les documents non-vidéo.
    /// </summary>
    [NotMapped]
    public TimeSpan? StartTime { get; private set; }

    /// <summary>
    /// Fin du chunk dans la vidéo source — non persisté, propagé depuis DocumentChunk.
    /// Null pour les documents non-vidéo.
    /// </summary>
    [NotMapped]
    public TimeSpan? EndTime { get; private set; }

    /// <summary>Navigation vers le message parent.</summary>
    public ChatMessage Message { get; private set; } = null!;

    private Citation() { }

    /// <summary>
    /// Crée une nouvelle citation de source.
    /// </summary>
    /// <param name="messageId">Identifiant du message auquel rattacher la citation.</param>
    /// <param name="documentId">Identifiant du document source.</param>
    /// <param name="documentName">Nom affichable du document.</param>
    /// <param name="excerpt">Extrait textuel utilisé comme contexte.</param>
    /// <param name="score">Score de similarité cosinus.</param>
    /// <param name="pageNumber">Numéro de page (optionnel).</param>
    /// <param name="sectionTitle">Titre de la section source (optionnel, non persisté).</param>
    /// <param name="chunkIndex">Position ordinale du chunk (non persisté).</param>
    /// <param name="startTime">Début du chunk dans la vidéo (optionnel, non persisté).</param>
    /// <param name="endTime">Fin du chunk dans la vidéo (optionnel, non persisté).</param>
    public static Citation Create(
        Guid messageId,
        Guid documentId,
        string documentName,
        string excerpt,
        double score,
        int? pageNumber = null,
        string? sectionTitle = null,
        int chunkIndex = 0,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        return new Citation
        {
            MessageId = messageId,
            DocumentId = documentId,
            DocumentName = documentName,
            Excerpt = excerpt,
            Score = score,
            PageNumber = pageNumber,
            SectionTitle = sectionTitle,
            ChunkIndex = chunkIndex,
            StartTime = startTime,
            EndTime = endTime
        };
    }
}
