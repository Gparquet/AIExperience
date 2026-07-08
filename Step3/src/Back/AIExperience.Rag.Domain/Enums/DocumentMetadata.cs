namespace AIExperience.Rag.Domain.Enums;

/// <summary>
/// Value object immuable représentant les métadonnées enrichies d'un document.
/// Extrait automatiquement lors du parsing ou renseigné manuellement lors de l'upload.
/// </summary>
public sealed record DocumentMetadata
{
    /// <summary>Titre du document (ex: "GBCP - Gestion Budgétaire et Comptabilité Publique").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Auteur ou organisme émetteur du document.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Nombre total de pages du document.</summary>
    public int PageCount { get; init; }

    /// <summary>Code de langue du document (ex: "fr", "en"). Par défaut : "fr".</summary>
    public string Language { get; init; } = "fr";

    /// <summary>Tags ou mots-clés associés au document pour le filtrage.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Instance vide par défaut, utilisée comme valeur initiale.</summary>
    public static DocumentMetadata Empty => new();

    /// <summary>
    /// Crée une nouvelle instance de <see cref="DocumentMetadata"/>.
    /// </summary>
    /// <param name="title">Titre du document.</param>
    /// <param name="author">Auteur du document.</param>
    /// <param name="pageCount">Nombre de pages.</param>
    /// <param name="language">Code de langue (ex: "fr").</param>
    /// <param name="tags">Tags associés au document.</param>
    public static DocumentMetadata Create(
        string title,
        string author = "",
        int pageCount = 0,
        string language = "fr",
        IEnumerable<string>? tags = null)
    {
        return new DocumentMetadata
        {
            Title = title,
            Author = author,
            PageCount = pageCount,
            Language = language,
            Tags = tags?.ToList().AsReadOnly() ?? (IReadOnlyList<string>)[]
        };
    }
}
