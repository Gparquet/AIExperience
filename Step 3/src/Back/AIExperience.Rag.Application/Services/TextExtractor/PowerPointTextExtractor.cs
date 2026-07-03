using System.Text;
using AIExperience.Rag.Domain.Interfaces.Services;
using DocumentFormat.OpenXml.Packaging;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les présentations PowerPoint (.pptx) via DocumentFormat.OpenXml.
/// Implémente <see cref="IPageAwareTextExtractor"/> : chaque slide devient une "page",
/// préfixée "# Slide N" pour que RecursiveChunker la reconnaisse comme une section.
/// </summary>
public sealed class PowerPointTextExtractor : IPageAwareTextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    /// <remarks>
    /// Délègue à <see cref="ExtractPagesAsync"/> pour éviter la duplication de code.
    /// Concatène toutes les slides en une seule chaîne pour la compatibilité avec
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
    /// Extrait le texte **slide par slide** afin que le chunker puisse propager
    /// <see cref="Domain.Models.TextChunk.PageNumber"/> dans chaque fragment (1 page = 1 slide).
    /// </remarks>
    public Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, string)>();
        using var doc = PresentationDocument.Open(filePath, false);

        var presentationPart = doc.PresentationPart
            ?? throw new InvalidOperationException($"Présentation PowerPoint sans PresentationPart : {filePath}");
        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<P.SlideId>().ToList() ?? [];

        for (int i = 0; i < slideIds.Count; i++)
        {
            var relId = slideIds[i].RelationshipId!.Value!;
            var slidePart = (SlidePart)presentationPart.GetPartById(relId);
            var slideText = string.Join(" ", slidePart.Slide.Descendants<D.Text>().Select(t => t.Text)).Trim();
            if (string.IsNullOrWhiteSpace(slideText)) continue;

            result.Add((i + 1, $"# Slide {i + 1}\n\n{slideText}"));
        }

        return Task.FromResult<IReadOnlyList<(int, string)>>(result);
    }
}
