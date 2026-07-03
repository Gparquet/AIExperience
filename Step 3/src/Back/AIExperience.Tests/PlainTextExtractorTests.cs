using AIExperience.Rag.Application.Services.TextExtractor;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="PlainTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie la prise en charge des fichiers .txt et .md, lus tels quels (le chunker
/// gère déjà nativement les titres Markdown).
/// </summary>
public sealed class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _sut = new();

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("readme.md")]
    [InlineData("RAPPORT.TXT")]
    public void CanHandle_TxtOrMdFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("rapport.docx")]
    public void CanHandle_OtherFile_ReturnsFalse(string path)
    {
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTextAsync_TxtFile_ReturnsRawContent()
    {
        var path = CreateTempFile(".txt", "Contenu texte brut.\nDeuxième ligne.");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("Contenu texte brut.");
        result.Should().Contain("Deuxième ligne.");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_MarkdownFile_PreservesHeadingSyntax()
    {
        // Le "#" n'est PAS transformé ici : c'est RecursiveChunker qui le détecte en aval.
        var path = CreateTempFile(".md", "# Titre principal\n\nParagraphe de contenu.");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("# Titre principal");
        result.Should().Contain("Paragraphe de contenu.");

        File.Delete(path);
    }

    private static string CreateTempFile(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        return path;
    }
}
