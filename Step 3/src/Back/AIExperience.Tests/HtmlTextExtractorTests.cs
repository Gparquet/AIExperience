using AIExperience.Rag.Application.Services.TextExtractor;
using FluentAssertions;
using System.IO;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="HtmlTextExtractor"/> (constat I-1 du plan Lot 0).
/// Vérifie que l'extracteur HTML extrait le texte visible en supprimant balises, scripts et styles.
/// </summary>
public sealed class HtmlTextExtractorTests
{
    private readonly HtmlTextExtractor _sut = new();

    // ── CanHandle ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("document.html")]
    [InlineData("page.htm")]
    [InlineData("DOCUMENT.HTML")]
    [InlineData("C:/docs/rapport.html")]
    public void CanHandle_HtmlFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("video.mp4")]
    [InlineData("texte.txt")]
    public void CanHandle_NonHtmlFile_ReturnsFalse(string path)
    {
        _sut.CanHandle(path).Should().BeFalse();
    }

    // ── ExtractTextAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractTextAsync_SimpleHtml_ReturnsVisibleText()
    {
        // Arrange — HTML minimal avec du texte visible
        var path = CreateTempHtml("<html><body><p>Bonjour le monde</p></body></html>");

        // Act
        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        // Assert
        result.Should().Contain("Bonjour le monde");

        // Cleanup
        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_RemovesScriptTags_ContentNotPresent()
    {
        // Arrange — script inline : le code JS ne doit pas apparaître dans le texte extrait
        var path = CreateTempHtml("""
            <html><body>
                <p>Contenu visible</p>
                <script>alert('code JS caché');</script>
            </body></html>
            """);

        // Act
        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        // Assert — le contenu visible est présent, le JS est absent
        result.Should().Contain("Contenu visible");
        result.Should().NotContain("alert");
        result.Should().NotContain("code JS caché");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_RemovesStyleTags_ContentNotPresent()
    {
        // Arrange — CSS inline : les règles de style ne doivent pas apparaître dans le texte
        var path = CreateTempHtml("""
            <html><head>
                <style>body { color: red; font-size: 14px; }</style>
            </head><body>
                <p>Texte de l'article</p>
            </body></html>
            """);

        // Act
        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        // Assert
        result.Should().Contain("Texte de l'article");
        result.Should().NotContain("color: red");
        result.Should().NotContain("font-size");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_HtmlWithMultipleParagraphs_ReturnsAllText()
    {
        // Arrange — plusieurs paragraphes doivent tous être extraits
        var path = CreateTempHtml("""
            <html><body>
                <h1>Titre principal</h1>
                <p>Premier paragraphe.</p>
                <p>Deuxième paragraphe.</p>
            </body></html>
            """);

        // Act
        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        // Assert — chaque section doit être présente
        result.Should().Contain("Titre principal");
        result.Should().Contain("Premier paragraphe.");
        result.Should().Contain("Deuxième paragraphe.");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyBody_ReturnsEmptyOrWhitespace()
    {
        // Arrange — body vide : ne doit pas planter
        var path = CreateTempHtml("<html><body></body></html>");

        // Act
        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        // Assert — pas d'exception, retour vide ou espace blanc uniquement
        result.Should().NotBeNull();
        result.Trim().Should().BeEmpty();

        File.Delete(path);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Écrit le HTML dans un fichier temporaire et retourne son chemin.</summary>
    private static string CreateTempHtml(string htmlContent)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.html");
        File.WriteAllText(path, htmlContent);
        return path;
    }
}
