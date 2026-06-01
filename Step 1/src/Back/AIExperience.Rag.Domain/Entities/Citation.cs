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
    public static Citation Create(
        Guid messageId,
        Guid documentId,
        string documentName,
        string excerpt,
        double score,
        int? pageNumber = null)
    {
        return new Citation
        {
            MessageId = messageId,
            DocumentId = documentId,
            DocumentName = documentName,
            Excerpt = excerpt,
            Score = score,
            PageNumber = pageNumber
        };
    }
}
