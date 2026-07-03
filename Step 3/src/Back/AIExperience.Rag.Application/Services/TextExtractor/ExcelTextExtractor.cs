using System.Text;
using AIExperience.Rag.Domain.Interfaces.Services;
using ClosedXML.Excel;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les classeurs Excel (.xlsx, via ClosedXML) et les fichiers CSV
/// (parseur RFC 4180 minimal fait maison, pour éviter une dépendance CsvHelper supplémentaire).
/// Implémente <see cref="IPageAwareTextExtractor"/> : chaque feuille .xlsx devient une "page" ;
/// un .csv est traité comme une page unique.
/// </summary>
public sealed class ExcelTextExtractor : IPageAwareTextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
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
    public Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var csvText = ExtractCsv(filePath);
            IReadOnlyList<(int, string)> csvResult = string.IsNullOrWhiteSpace(csvText)
                ? []
                : [(1, csvText)];
            return Task.FromResult(csvResult);
        }

        var result = new List<(int, string)>();
        using var workbook = new XLWorkbook(filePath);

        int pageNumber = 1;
        foreach (var worksheet in workbook.Worksheets)
        {
            var sheetText = ExtractWorksheet(worksheet);
            if (!string.IsNullOrWhiteSpace(sheetText))
                result.Add((pageNumber, sheetText));
            pageNumber++;
        }

        return Task.FromResult<IReadOnlyList<(int, string)>>(result);
    }

    /// <summary>Convertit une feuille Excel en texte "# NomFeuille" + lignes "colonne: valeur".</summary>
    private static string ExtractWorksheet(IXLWorksheet worksheet)
    {
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null) return string.Empty;

        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count == 0) return string.Empty;

        int columnCount = usedRange.ColumnCount();
        var headers = Enumerable.Range(1, columnCount)
            .Select(col => rows[0].Cell(col).GetString())
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# {worksheet.Name}");
        sb.AppendLine();

        foreach (var row in rows.Skip(1))
        {
            var line = new StringBuilder();
            for (int col = 1; col <= columnCount; col++)
            {
                var value = row.Cell(col).GetString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (line.Length > 0) line.Append(", ");
                line.Append($"{headers[col - 1]}: {value}");
            }
            if (line.Length > 0) sb.AppendLine(line.ToString());
        }

        return sb.ToString().Trim();
    }

    /// <summary>Convertit un fichier CSV en texte "colonne: valeur" (1ère ligne = en-têtes).</summary>
    private static string ExtractCsv(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0) return string.Empty;

        var headers = ParseCsvLine(lines[0]);
        var sb = new StringBuilder();

        foreach (var rawLine in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var values = ParseCsvLine(rawLine);

            var line = new StringBuilder();
            for (int i = 0; i < headers.Count && i < values.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i])) continue;
                if (line.Length > 0) line.Append(", ");
                line.Append($"{headers[i]}: {values[i]}");
            }
            if (line.Length > 0) sb.AppendLine(line.ToString());
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Parseur CSV minimal conforme RFC 4180 : gère les champs entre guillemets contenant
    /// des virgules ou des guillemets échappés (""), sans dépendance externe (I-2 : évite CsvHelper).
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
