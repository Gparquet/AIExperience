using AIExperience.Rag.Domain.Interfaces.Services;
using AngleSharp;
using AngleSharp.Dom;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extrait le texte visible d'un fichier HTML en supprimant balises, scripts, styles et navigation.
/// Utilise AngleSharp (parseur conforme WHATWG) pour garantir une extraction robuste.
/// Corrige le constat I-1 : l'ancienne implémentation levait NotImplementedException,
/// rendant l'upload de tout fichier .html impossible.
/// </summary>
public sealed class HtmlTextExtractor : ITextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".htm",  StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var html = await File.ReadAllTextAsync(filePath, cancellationToken);

        // AngleSharp parse le HTML selon la spécification WHATWG — plus robuste qu'une regex
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

        // Supprime les nœuds qui ne contribuent pas au texte lisible
        RemoveNonTextNodes(document);

        // TextContent concatène tout le texte visible ; on normalise les sauts de ligne multiples
        var rawText = document.Body?.TextContent ?? string.Empty;
        return NormalizeWhitespace(rawText);
    }

    /// <summary>
    /// Supprime les éléments dont le contenu ne doit pas apparaître dans l'extraction texte :
    /// scripts, styles, navigation, pied de page, noscript.
    /// Note : "head" est intentionnellement absent — document.Body?.TextContent ne lit que
    /// le contenu du &lt;body&gt;, donc &lt;head&gt; est déjà exclu sans suppression explicite.
    /// </summary>
    private static void RemoveNonTextNodes(IDocument document)
    {
        // Sélecteurs groupés : une seule traversée DOM au lieu de N traversées séquentielles.
        foreach (var node in document.QuerySelectorAll("script,style,nav,footer,noscript").ToList())
            node.Remove();
    }

    /// <summary>
    /// Remplace les suites de lignes vides (≥ 2) par un double saut de ligne pour éviter
    /// les grandes plages vides dans le texte final.
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        // Réduit plusieurs sauts de ligne consécutifs à un double saut
        var normalized = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }
}
