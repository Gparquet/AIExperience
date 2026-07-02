namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Extend <see cref="ITextExtractor"/> avec une extraction **page par page**.
/// Implémentée par les extracteurs qui peuvent préserver la structure paginée
/// du document source (ex. : <c>PdfTextExtractor</c>).
///
/// Le numéro de page se perdait lorsque le PDF était aplati en une seule chaîne avant le chunking.
/// Cette interface permet à l'extracteur PDF de retourner une liste de couples (numéro de page, texte),
/// que le chunker propagera ensuite dans chaque <see cref="Models.TextChunk"/>.
/// </summary>
public interface IPageAwareTextExtractor : ITextExtractor
{
    /// <summary>
    /// Extrait le texte du fichier spécifié page par page.
    /// </summary>
    /// <param name="filePath">Chemin vers le fichier à extraire.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>
    /// Liste ordonnée de tuples <c>(PageNumber, Text)</c>, un par page physique.
    /// <c>PageNumber</c> commence à 1.
    /// </returns>
    Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken);
}
