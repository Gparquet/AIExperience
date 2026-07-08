using AIExperience.Rag.Application.Services.LanguageDetection;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="StopwordLanguageDetectionService"/> (constat I-6 du plan Lot 2).
/// Vérifie la détection fr/en/es/de/it par fréquence de mots vides et le repli sur la langue
/// par défaut pour un texte trop court ou trop ambigu.
/// </summary>
public sealed class StopwordLanguageDetectionServiceTests
{
    private readonly StopwordLanguageDetectionService _sut = new();

    [Fact]
    public void Detect_EmptyText_ReturnsDefaultLanguage()
    {
        _sut.Detect("").Should().Be("fr");
    }

    [Fact]
    public void Detect_TextTooShort_ReturnsDefaultLanguage()
    {
        _sut.Detect("Short text.").Should().Be("fr");
    }

    [Fact]
    public void Detect_FrenchText_ReturnsFr()
    {
        var text = "Le chat est sur la table et il regarde par la fenêtre. " +
                    "La maison est grande et le jardin est très beau avec des fleurs partout.";
        _sut.Detect(text).Should().Be("fr");
    }

    [Fact]
    public void Detect_EnglishText_ReturnsEn()
    {
        var text = "The cat is on the table and it looks out of the window. " +
                    "The house is big and the garden is very beautiful with flowers everywhere.";
        _sut.Detect(text).Should().Be("en");
    }

    [Fact]
    public void Detect_SpanishText_ReturnsEs()
    {
        var text = "El gato está en la mesa y mira por la ventana. " +
                    "La casa es grande y el jardín es muy bonito con flores por todas partes para que todo el mundo lo vea.";
        _sut.Detect(text).Should().Be("es");
    }

    [Fact]
    public void Detect_GermanText_ReturnsDe()
    {
        var text = "Die Katze ist auf dem Tisch und sie schaut aus dem Fenster. " +
                    "Das Haus ist groß und der Garten ist sehr schön mit vielen Blumen und das ist wirklich sehr schön für alle.";
        _sut.Detect(text).Should().Be("de");
    }

    [Fact]
    public void Detect_ItalianText_ReturnsIt()
    {
        var text = "Il gatto è sul tavolo e guarda fuori dalla finestra. " +
                    "La casa è grande e il giardino è molto bello con tanti fiori che si possono vedere da lontano.";
        _sut.Detect(text).Should().Be("it");
    }

    [Fact]
    public void Detect_TextWithNoRecognizableStopwords_FallsBackToDefaultLanguage()
    {
        var text = "Xk7 Zoltar 9000 Blipverse Quixotic Zephyrian Wobblesnout Frumious Blargtastic Nizzlewomp Quorvexian.";
        _sut.Detect(text).Should().Be("fr");
    }
}
