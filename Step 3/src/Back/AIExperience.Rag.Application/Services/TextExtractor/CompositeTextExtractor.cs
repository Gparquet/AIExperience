using AIExperience.Rag.Domain.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Sélectionne l'extracteur de texte approprié selon l'extension du fichier
/// et délègue l'extraction à cet extracteur.
/// Lève une exception si aucun extracteur ne correspond, évitant des documents
/// "Completed" avec 0 chunk qui seraient silencieusement inutilisables.
/// </summary>
public sealed class CompositeTextExtractor : ICompositeTextExtractor
{
    private readonly IReadOnlyList<ITextExtractor> _textExtractors;
    private readonly ILogger<CompositeTextExtractor> _logger;

    public CompositeTextExtractor(
        IEnumerable<ITextExtractor> textExtractors,
        ILogger<CompositeTextExtractor> logger)
    {
        // Exclut une éventuelle référence circulaire (le composite lui-même dans la liste DI)
        _textExtractors = textExtractors
            .Where(e => e.GetType() != typeof(CompositeTextExtractor))
            .ToList();
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// Levée si aucun extracteur enregistré ne supporte l'extension du fichier.
    /// </exception>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var extractor = ResolveExtractor(filePath);
        return extractor.ExtractTextAsync(filePath, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Si l'extracteur résolu implémente <see cref="IPageAwareTextExtractor"/>, délègue
    /// l'extraction paginée ; sinon, retourne une unique entrée (page 1, texte complet).
    /// Préserve les numéros de page afin que le chunker puisse les propager dans chaque chunk.
    /// </remarks>
    public async Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var extractor = ResolveExtractor(filePath);

        // Si l'extracteur sait retourner des pages numérotées, on l'utilise directement.
        if (extractor is IPageAwareTextExtractor pagedExtractor)
            return await pagedExtractor.ExtractPagesAsync(filePath, cancellationToken);

        // Fallback : extracteur non paginé → wrap en page 1 unique.
        var text = await extractor.ExtractTextAsync(filePath, cancellationToken);
        return [(1, text)];
    }

    /// <summary>
    /// Résout l'extracteur adapté au format du fichier ou lève <see cref="NotSupportedException"/>.
    /// </summary>
    private ITextExtractor ResolveExtractor(string filePath)
    {
        var extractor = _textExtractors.FirstOrDefault(e => e.CanHandle(filePath));
        if (extractor is not null) return extractor;

        var extension = Path.GetExtension(filePath);
        _logger.LogError(
            "Aucun extracteur ne prend en charge l'extension '{Extension}' pour le fichier : {File}",
            extension, filePath);

        throw new NotSupportedException(
            $"Format non supporté : '{extension}'. " +
            "Formats pris en charge : PDF, HTML, DOCX, XLSX, CSV, PPTX, TXT, Markdown, JSON, vidéo/audio.");
    }
}
