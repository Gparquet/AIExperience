namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Point d'entrée unique pour l'extraction de texte : sélectionne automatiquement
/// l'extracteur approprié selon le format du fichier.
/// </summary>
public interface ICompositeTextExtractor
{
    /// <summary>
    /// Extrait le texte du fichier spécifié (forme aplatie, sans pagination).
    /// </summary>
    Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Extrait le texte page par page.
    /// Si l'extracteur sous-jacent implémente <see cref="IPageAwareTextExtractor"/>,
    /// la numérotation des pages est préservée ; sinon, retourne une seule page (numéro 1).
    /// Permet au chunker de propager <see cref="Models.TextChunk.PageNumber"/> dans chaque fragment produit.
    /// </summary>
    Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken);
}
