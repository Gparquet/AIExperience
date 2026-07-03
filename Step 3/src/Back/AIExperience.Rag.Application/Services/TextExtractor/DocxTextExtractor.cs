using System.Text;
using AIExperience.Rag.Domain.Interfaces.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les documents Word (.docx) via DocumentFormat.OpenXml.
/// Les styles Word Heading1-Heading4 sont convertis en préfixes Markdown "#"-"####" :
/// RecursiveChunker détecte déjà cette syntaxe pour peupler TextChunk.SectionTitle,
/// évitant toute modification du chunker.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException($"Document Word sans corps de texte : {filePath}");

        var sb = new StringBuilder();
        foreach (var paragraph in body.Elements<Paragraph>())
        {
            var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var headingLevel = GetHeadingLevel(paragraph);
            if (headingLevel > 0)
                sb.AppendLine($"{new string('#', headingLevel)} {text}");
            else
                sb.AppendLine(text);
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>Retourne 1 à 4 pour les styles Heading1-Heading4, 0 pour un paragraphe normal.</summary>
    private static int GetHeadingLevel(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return styleId switch
        {
            "Heading1" => 1,
            "Heading2" => 2,
            "Heading3" => 3,
            "Heading4" => 4,
            _ => 0
        };
    }
}
