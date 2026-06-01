using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services.TextExtractor;

public sealed class HtmlTextExtractor : ITextExtractor
{
    public bool CanHandle(string filePath) =>
            filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
