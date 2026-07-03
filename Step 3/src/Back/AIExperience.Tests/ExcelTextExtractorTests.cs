using AIExperience.Rag.Application.Services.TextExtractor;
using ClosedXML.Excel;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="ExcelTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'extraction .xlsx (pagination par feuille, format "colonne: valeur")
/// et .csv (parseur RFC 4180 minimal, sans dépendance CsvHelper).
/// </summary>
public sealed class ExcelTextExtractorTests
{
    private readonly ExcelTextExtractor _sut = new();

    [Theory]
    [InlineData("ventes.xlsx")]
    [InlineData("export.csv")]
    [InlineData("VENTES.XLSX")]
    public void CanHandle_XlsxOrCsvFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_OtherFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractPagesAsync_SingleSheet_ReturnsOnePage()
    {
        var path = CreateTempXlsx(wb =>
        {
            var ws = wb.Worksheets.Add("Ventes");
            ws.Cell(1, 1).Value = "Produit";
            ws.Cell(1, 2).Value = "Prix";
            ws.Cell(2, 1).Value = "Stylo";
            ws.Cell(2, 2).Value = "1.5";
        });

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(1);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Contain("# Ventes");
        pages[0].Text.Should().Contain("Produit: Stylo");
        pages[0].Text.Should().Contain("Prix: 1.5");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_MultipleSheets_ReturnsOnePagePerSheet()
    {
        var path = CreateTempXlsx(wb =>
        {
            var ws1 = wb.Worksheets.Add("Feuille1");
            ws1.Cell(1, 1).Value = "Colonne";
            ws1.Cell(2, 1).Value = "A";

            var ws2 = wb.Worksheets.Add("Feuille2");
            ws2.Cell(1, 1).Value = "Colonne";
            ws2.Cell(2, 1).Value = "B";
        });

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(2);
        pages[0].Text.Should().Contain("Feuille1");
        pages[1].Text.Should().Contain("Feuille2");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_Csv_ReturnsSinglePageWithKeyValueLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");
        File.WriteAllText(path, "Produit,Prix\nStylo,1.5\nCahier,2.0");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(1);
        pages[0].Text.Should().Contain("Produit: Stylo");
        pages[0].Text.Should().Contain("Prix: 1.5");
        pages[0].Text.Should().Contain("Produit: Cahier");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_CsvWithQuotedCommaField_ParsesCorrectly()
    {
        // Champ entre guillemets contenant une virgule : ne doit pas être coupé en deux colonnes.
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");
        File.WriteAllText(path, "Nom,Adresse\nDupont,\"12 rue de la Paix, Paris\"");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages[0].Text.Should().Contain("Adresse: 12 rue de la Paix, Paris");

        File.Delete(path);
    }

    private static string CreateTempXlsx(Action<XLWorkbook> configure)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xlsx");
        using var workbook = new XLWorkbook();
        configure(workbook);
        workbook.SaveAs(path);
        return path;
    }
}
