using AIExperience.Rag.Domain.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Application.Services.TextExtractor;

public sealed class CompositeTextExtractor : ICompositeTextExtractor
{
    private readonly IEnumerable<ITextExtractor> _textExtractors;
    private readonly ILogger<CompositeTextExtractor> _logger;

    public CompositeTextExtractor(IEnumerable<ITextExtractor> textExtractors, ILogger<CompositeTextExtractor> logger)
    {
        _textExtractors = textExtractors.Where(e => e.GetType() != typeof(CompositeTextExtractor)).ToList();
        _logger = logger;
    }

    public bool CanHandle(string filePath) => true;

    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var extractor = _textExtractors.FirstOrDefault(e => e.CanHandle(filePath));
        if (extractor is null)
        {
            _logger.LogError($"Aucun extracteur ne peut gérer le fichier : {filePath}");
        }

        return extractor is not null
            ? extractor.ExtractTextAsync(filePath, cancellationToken)
            : Task.FromResult(string.Empty);
    }
}
