using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les fichiers texte brut (.txt) et Markdown (.md).
/// Lecture native, sans transformation : le Markdown est laissé tel quel car
/// <c>RecursiveChunker</c> détecte déjà les titres "#" à "####" pour peupler SectionTitle.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
        => File.ReadAllTextAsync(filePath, cancellationToken);
}
