using AIExperience.Rag.Application.Services.TextExtractor;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="CompositeTextExtractor"/> (constat I-3 du plan Lot 0).
/// Vérifie que l'extracteur composite lève une exception explicite si aucun extracteur
/// ne gère le format, au lieu de retourner silencieusement une chaîne vide.
/// </summary>
public sealed class CompositeTextExtractorTests
{
    // ── Aucun extracteur disponible ────────────────────────────────────────────

    [Fact]
    public async Task ExtractTextAsync_NoExtractorRegistered_ThrowsNotSupportedException()
    {
        // Arrange — liste d'extracteurs vide : aucun format supporté
        var sut = new CompositeTextExtractor([], NullLogger<CompositeTextExtractor>.Instance);

        // Act + Assert — doit lever NotSupportedException, pas retourner string.Empty
        var act = async () => await sut.ExtractTextAsync("document.pdf", CancellationToken.None);
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*pdf*"); // le message mentionne l'extension non supportée
    }

    [Fact]
    public async Task ExtractTextAsync_NoExtractorMatchingFormat_ThrowsNotSupportedException()
    {
        // Arrange — un extracteur PDF : il ne gère pas .docx
        var sut = new CompositeTextExtractor(
            [new PdfTextExtractor()],
            NullLogger<CompositeTextExtractor>.Instance);

        // Act + Assert — .docx non géré → exception
        var act = async () => await sut.ExtractTextAsync("rapport.docx", CancellationToken.None);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // ── Extracteur correspondant trouvé ───────────────────────────────────────

    [Fact]
    public async Task ExtractTextAsync_ExtractorFound_DelegatesExtractionCorrectly()
    {
        // Arrange — extracteur fictif qui reconnaît .test et retourne un texte fixe
        var fakeExtractor = new FakeTextExtractor(".test", "Contenu extrait avec succès");
        var sut = new CompositeTextExtractor(
            [fakeExtractor],
            NullLogger<CompositeTextExtractor>.Instance);

        // Act
        var result = await sut.ExtractTextAsync("fichier.test", CancellationToken.None);

        // Assert — le résultat provient bien de l'extracteur délégué
        result.Should().Be("Contenu extrait avec succès");
    }

    [Fact]
    public async Task ExtractTextAsync_MultipleExtractors_SelectsCorrectOne()
    {
        // Arrange — deux extracteurs : PDF et un fictif .test
        var fakeExtractor = new FakeTextExtractor(".test", "Résultat du fake");
        var sut = new CompositeTextExtractor(
            [new PdfTextExtractor(), fakeExtractor],
            NullLogger<CompositeTextExtractor>.Instance);

        // Act — doit sélectionner le FakeTextExtractor (pas le PDF)
        var result = await sut.ExtractTextAsync("fichier.test", CancellationToken.None);

        // Assert
        result.Should().Be("Résultat du fake");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracteur fictif configurable pour les tests — retourne toujours le même texte.
    /// </summary>
    private sealed class FakeTextExtractor(string extension, string content) : ITextExtractor
    {
        public bool CanHandle(string filePath)
            => filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

        public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult(content);
    }
}
