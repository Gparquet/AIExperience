using AIExperience.Rag.Application.Services.TextExtractor;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="DocxTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'extraction du texte et la propagation des titres Word (Heading1-4)
/// en syntaxe Markdown "#", reprise nativement par RecursiveChunker.
/// </summary>
public sealed class DocxTextExtractorTests
{
    private readonly DocxTextExtractor _sut = new();

    [Theory]
    [InlineData("rapport.docx")]
    [InlineData("RAPPORT.DOCX")]
    public void CanHandle_DocxFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonDocxFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTextAsync_PlainParagraphs_ReturnsText()
    {
        var path = CreateTempDocx(
            (null, "Premier paragraphe."),
            (null, "Deuxième paragraphe."));

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("Premier paragraphe.");
        result.Should().Contain("Deuxième paragraphe.");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_Heading1_PrefixedWithSingleHash()
    {
        var path = CreateTempDocx(("Heading1", "Introduction"));

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("# Introduction");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_Heading3_PrefixedWithTripleHash()
    {
        var path = CreateTempDocx(("Heading3", "Sous-section"));

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("### Sous-section");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyDocument_ReturnsEmptyOrWhitespace()
    {
        var path = CreateTempDocx();

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Trim().Should().BeEmpty();

        File.Delete(path);
    }

    /// <summary>Construit un .docx minimal en mémoire pour les tests, sans fixture binaire.</summary>
    private static string CreateTempDocx(params (string? Style, string Text)[] paragraphs)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            foreach (var (style, text) in paragraphs)
            {
                var paragraph = new Paragraph();
                if (style is not null)
                    paragraph.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = style });
                paragraph.Append(new Run(new Text(text)));
                body.Append(paragraph);
            }

            mainPart.Document.Save();
        }
        return path;
    }
}
