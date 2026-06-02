using AIExperience.Rag.Domain.Interfaces.Services;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AIExperience.Rag.Application.Services.TextExtractor;

public sealed class PdfTextExtractor : ITextExtractor
{
    public bool CanHandle(string filePath) =>
       filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(filePath);

        foreach (var page in pdf.GetPages())
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
                continue;

            var lines = GroupWordsIntoLines(words, lineHeightThreshold: 5.0);

            foreach (var line in lines.OrderByDescending(l => l.avgY))
            {
                var lineText = string.Join(" ", line.words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
                sb.AppendLine(lineText);
            }

            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    private static List<(double avgY, List<Word> words)> GroupWordsIntoLines(List<Word> words, double lineHeightThreshold)
    {
        var lines = new List<(double avgY, List<Word> words)>();

        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Centroid.Y))
        {
            double wordY = word.BoundingBox.Centroid.Y;
            bool added = false;

            for (int i = 0; i < lines.Count; i++)
            {
                if (Math.Abs(lines[i].avgY - wordY) <= lineHeightThreshold)
                {
                    lines[i].words.Add(word);
                    added = true;
                    break;
                }
            }

            if (!added)
                lines.Add((wordY, new List<Word> { word }));
        }

        return lines;
    }
}
