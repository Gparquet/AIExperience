using AIExperience.Rag.Application.Services.TextExtractor;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIExperience.Tests;

/// <summary>
/// Tests unitaires pour la pagination PDF.
/// Vérifie que :
///   1. <see cref="PdfTextExtractor"/> implémente <see cref="IPageAwareTextExtractor"/>
///      pour exposer une extraction page par page.
///   2. <see cref="CompositeTextExtractor.ExtractPagesAsync"/> délègue à l'extracteur
///      page-aware lorsqu'il est disponible.
///   3. <see cref="CompositeTextExtractor.ExtractPagesAsync"/> retourne une liste
///      d'une seule entrée (page 1) lorsque l'extracteur n'est pas page-aware.
/// </summary>
public sealed class PdfPaginationTests
{
    #region Contrat d'interface

    [Fact]
    public void PdfTextExtractor_ImplementsIPageAwareTextExtractor()
    {
        // PdfTextExtractor doit implémenter IPageAwareTextExtractor pour exposer l'extraction page par page.
        var extractor = new PdfTextExtractor();
        extractor.Should().BeAssignableTo<IPageAwareTextExtractor>(
            "PdfTextExtractor doit implémenter IPageAwareTextExtractor pour propager les PageNumber");
    }

    #endregion

    #region CompositeTextExtractor — ExtractPagesAsync

    [Fact]
    public async Task ExtractPagesAsync_PageAwareExtractor_DelegatesAndReturnsPages()
    {
        // Arrange : extracteur fake qui implémente IPageAwareTextExtractor
        // et retourne 3 pages numérotées.
        var fakePagedExtractor = new FakePagedExtractor(".multi", [
            (1, "Contenu page 1"),
            (2, "Contenu page 2"),
            (3, "Contenu page 3")
        ]);

        var sut = new CompositeTextExtractor(
            [fakePagedExtractor],
            NullLogger<CompositeTextExtractor>.Instance);

        // Act
        var pages = await sut.ExtractPagesAsync("fichier.multi", CancellationToken.None);

        // Assert
        pages.Should().HaveCount(3);
        pages[0].PageNumber.Should().Be(1);
        pages[1].PageNumber.Should().Be(2);
        pages[2].PageNumber.Should().Be(3);
        pages[0].Text.Should().Be("Contenu page 1");
    }

    [Fact]
    public async Task ExtractPagesAsync_NonPageAwareExtractor_ReturnsWrappedSinglePage()
    {
        // Arrange : extracteur simple (non page-aware) — doit être wrappé en page 1
        var fakePlainExtractor = new FakePlainExtractor(".plain", "Texte extrait brut");
        var sut = new CompositeTextExtractor(
            [fakePlainExtractor],
            NullLogger<CompositeTextExtractor>.Instance);

        // Act
        var pages = await sut.ExtractPagesAsync("fichier.plain", CancellationToken.None);

        // Assert — un seul élément avec PageNumber=1 et le texte de l'extracteur
        pages.Should().HaveCount(1,
            "un extracteur non page-aware doit être wrappé en une seule page");
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Be("Texte extrait brut");
    }

    [Fact]
    public async Task ExtractPagesAsync_NoExtractorFound_ThrowsNotSupportedException()
    {
        // Comportement hérité de ExtractTextAsync : si aucun extracteur ne gère
        // le format, ExtractPagesAsync doit aussi lever NotSupportedException.
        var sut = new CompositeTextExtractor(
            [new FakePlainExtractor(".pdf", "")],
            NullLogger<CompositeTextExtractor>.Instance);

        var act = async () => await sut.ExtractPagesAsync("fichier.xyz", CancellationToken.None);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    #endregion

    #region Fakes

    /// <summary>Extracteur simple (non page-aware) — implémente uniquement ITextExtractor.</summary>
    private sealed class FakePlainExtractor(string extension, string content) : ITextExtractor
    {
        public bool CanHandle(string filePath) =>
            filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        public Task<string> ExtractTextAsync(string filePath, CancellationToken ct) =>
            Task.FromResult(content);
    }

    /// <summary>Extracteur page-aware — implémente IPageAwareTextExtractor.</summary>
    private sealed class FakePagedExtractor(
        string extension,
        IReadOnlyList<(int PageNumber, string Text)> pages) : IPageAwareTextExtractor
    {
        public bool CanHandle(string filePath) =>
            filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

        public Task<string> ExtractTextAsync(string filePath, CancellationToken ct) =>
            Task.FromResult(string.Join("\n\n", pages.Select(p => p.Text)));

        public Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
            string filePath, CancellationToken ct) =>
            Task.FromResult(pages);
    }

    #endregion
}
