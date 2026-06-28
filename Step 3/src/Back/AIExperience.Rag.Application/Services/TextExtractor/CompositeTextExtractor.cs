using AIExperience.Rag.Domain.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Sélectionne l'extracteur de texte approprié selon l'extension du fichier
/// et délègue l'extraction à cet extracteur.
/// Corrige le constat I-3 : l'ancienne implémentation retournait silencieusement
/// <c>string.Empty</c> si aucun extracteur ne correspondait, produisant des documents
/// "Completed" avec 0 chunk — un échec invisible.
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
    public bool CanHandle(string filePath) => true;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// Levée si aucun extracteur enregistré ne supporte l'extension du fichier.
    /// Remplace l'ancien retour silencieux de <c>string.Empty</c> qui produisait
    /// des documents "Completed" sans aucun chunk interrogeable.
    /// </exception>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var extractor = _textExtractors.FirstOrDefault(e => e.CanHandle(filePath));

        if (extractor is null)
        {
            var extension = Path.GetExtension(filePath);
            _logger.LogError(
                "Aucun extracteur ne prend en charge l'extension '{Extension}' pour le fichier : {File}",
                extension, filePath);

            // Exception explicite : un fichier non géré doit marquer le document Failed,
            // pas produire silencieusement un document Completed vide.
            // Note : ITextExtractor n'expose pas de liste d'extensions (design existant) ;
            // un message précis serait possible si l'interface était enrichie d'une propriété
            // SupportedExtensions — à envisager dans le Lot 1.
            throw new NotSupportedException(
                $"Format non supporté : '{extension}'. " +
                "Vérifiez que le fichier correspond à un format pris en charge (PDF, HTML, vidéo/audio).");
        }

        return extractor.ExtractTextAsync(filePath, cancellationToken);
    }
}
