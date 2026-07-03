using AIExperience.Rag.Application.Services.TextExtractor;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="PowerPointTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'extraction texte par slide (pagination) via DocumentFormat.OpenXml.
/// </summary>
public sealed class PowerPointTextExtractorTests
{
    private readonly PowerPointTextExtractor _sut = new();

    [Theory]
    [InlineData("presentation.pptx")]
    [InlineData("PRESENTATION.PPTX")]
    public void CanHandle_PptxFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonPptxFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractPagesAsync_MultipleSlides_ReturnsOnePagePerSlide()
    {
        var path = CreateTempPptx("Bienvenue", "Plan de la présentation");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(2);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Contain("Bienvenue");
        pages[1].PageNumber.Should().Be(2);
        pages[1].Text.Should().Contain("Plan de la présentation");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_SlideText_PrefixedWithSlideHeading()
    {
        var path = CreateTempPptx("Contenu de test");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages[0].Text.Should().Contain("# Slide 1");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_MultipleSlides_ConcatenatesAllSlides()
    {
        var path = CreateTempPptx("Première slide", "Deuxième slide");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("Première slide");
        result.Should().Contain("Deuxième slide");

        File.Delete(path);
    }

    /// <summary>
    /// Vérifie que les slides sans texte sont ignorées et que les numéros de page
    /// conservent l'index de la slide d'origine (pas de renumérotation).
    /// Ex. : si la slide 2 est vide, la slide 3 aura PageNumber=3, pas 2.
    /// Cela aligne le comportement PowerPoint avec PdfTextExtractor (gestion des pages blanches).
    /// </summary>
    [Fact]
    public async Task ExtractPagesAsync_EmptySlideInMiddle_SkipsEmptySlideRetainsOriginalPageNumbers()
    {
        var path = CreateTempPptx("Contenu slide 1", null, "Contenu slide 3");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        // Slide 2 est vide → doit être ignorée.
        pages.Should().HaveCount(2);

        // Slide 1 et 3 conservent leurs numéros d'origine (pas renumérotation à 1, 2).
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Contain("Contenu slide 1");

        pages[1].PageNumber.Should().Be(3);
        pages[1].Text.Should().Contain("Contenu slide 3");

        File.Delete(path);
    }

    /// <summary>
    /// Construit un .pptx minimal en mémoire : une slide par texte fourni, sans master/layout/theme
    /// (non nécessaires pour un round-trip via DocumentFormat.OpenXml, seulement pour ouvrir dans PowerPoint).
    /// Accepte null ou chaînes vides pour créer des slides sans texte (testant le branch skip).
    /// </summary>
    private static string CreateTempPptx(params string?[] slideTexts)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pptx");
        using (var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();
            var slideIdList = new P.SlideIdList();
            presentationPart.Presentation.Append(slideIdList);

            uint slideId = 256;
            foreach (var text in slideTexts)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();

                // Construit une P.ShapeTree avec les éléments obligatoires de schéma OOXML :
                // NonVisualGroupShapeProperties + GroupShapeProperties (avant toute P.Shape).
                var shapeTree = new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties());

                // Ajoute une shape avec texte si le texte n'est pas vide.
                if (!string.IsNullOrWhiteSpace(text))
                {
                    shapeTree.Append(
                        new P.Shape(
                            new P.NonVisualShapeProperties(
                                new P.NonVisualDrawingProperties { Id = 2, Name = "TextBox" },
                                new P.NonVisualShapeDrawingProperties(),
                                new P.ApplicationNonVisualDrawingProperties()),
                            new P.ShapeProperties(),
                            new P.TextBody(
                                new D.BodyProperties(),
                                new D.ListStyle(),
                                new D.Paragraph(new D.Run(new D.Text(text))))));
                }

                slidePart.Slide = new P.Slide(
                    new P.CommonSlideData(shapeTree));
                slideIdList.Append(new P.SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }

            presentationPart.Presentation.Save();
        }
        return path;
    }
}
