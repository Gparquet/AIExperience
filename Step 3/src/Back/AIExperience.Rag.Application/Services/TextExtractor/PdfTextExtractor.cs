using AIExperience.Rag.Domain.Interfaces.Services;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur de texte pour les fichiers PDF via la bibliothèque PdfPig.
/// Implémente <see cref="IPageAwareTextExtractor"/> pour préserver les numéros de page,
/// ce qui permet au chunker de propager <see cref="Domain.Models.TextChunk.PageNumber"/> dans chaque fragment.
/// </summary>
public sealed class PdfTextExtractor : IPageAwareTextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
       filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    /// <remarks>
    /// Délègue à <see cref="ExtractPagesAsync"/> pour éviter la duplication de code.
    /// Concatène toutes les pages en une seule chaîne pour la compatibilité avec
    /// les consommateurs qui n'utilisent pas encore la pagination.
    /// </remarks>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var pages = await ExtractPagesAsync(filePath, cancellationToken);
        var sb = new StringBuilder();
        foreach (var (_, text) in pages)
        {
            sb.AppendLine(text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Extrait le texte **page par page** afin que le chunker puisse propager
    /// <see cref="Domain.Models.TextChunk.PageNumber"/> dans chaque fragment.
    /// </remarks>
    public Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, string)>();
        using var pdf = PdfDocument.Open(filePath);

        foreach (var page in pdf.GetPages())
        {
            var pageText = ExtractPageText(page);
            if (!string.IsNullOrWhiteSpace(pageText))
                result.Add((page.Number, pageText));
        }

        return Task.FromResult<IReadOnlyList<(int, string)>>(result);
    }

    /// <summary>
    /// Extrait le texte brut d'une seule page PDF en regroupant les mots en lignes
    /// selon leur position Y sur la page.
    /// Méthode privée partagée par <see cref="ExtractTextAsync"/> et <see cref="ExtractPagesAsync"/>.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0) return string.Empty;

        var lines = GroupWordsIntoLines(words, lineHeightThreshold: 5.0);
        var sb = new StringBuilder();
        foreach (var line in lines.OrderByDescending(l => l.avgY))
        {
            var lineText = string.Join(" ",
                line.words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
            sb.AppendLine(lineText);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Regroupe les mots d'une page en lignes en se basant sur la position Y des mots
    /// (les mots dont la coordonnée Y est proche appartiennent à la même ligne).
    /// </summary>
    private static List<(double avgY, List<Word> words)> GroupWordsIntoLines(
        List<Word> words,
        double lineHeightThreshold)
    {
        var lines = new List<(double avgY, List<Word> words)>();

        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Centroid.Y))
        {
            double wordY = word.BoundingBox.Centroid.Y;
            bool added = false;

            for (int i = 0; i < lines.Count; i++)
            {
                if (Math.Abs(lines[i].avgY - wordY) <= lineHeightThreshold)
                {
                    lines[i].words.Add(word);
                    added = true;
                    break;
                }
            }

            if (!added)
                lines.Add((wordY, new List<Word> { word }));
        }

        return lines;
    }
}
