using AIExperience.Rag.Domain.Interfaces.Services;
using System.Text;
using UglyToad.PdfPig;

namespace AIExperience.Rag.Application.Services.TextExtractor;

public sealed class PdfTextExtractor : ITextExtractor
{
    public bool CanHandle(string filePath) =>
       filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        using (var pdf = PdfDocument.Open(filePath))
        {
            foreach (var page in pdf.GetPages())
            {
                sb.AppendLine(page.Text);
            }
        }

        return Task.FromResult(sb.ToString());
    }
}
