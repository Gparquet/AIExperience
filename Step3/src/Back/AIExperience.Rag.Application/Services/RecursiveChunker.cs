using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using System.Text;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Chunker récursif hiérarchique : découpe d'abord par paragraphes, puis par phrases
/// si les paragraphes dépassent la taille maximale. Recommandé pour les documents structurés (PDF, GBCP).
/// découpage véritablement récursif (paragraphe → phrase → mot).
/// propagation de <see cref="TextChunk.SectionTitle"/> et <see cref="TextChunk.PageNumber"/>.
/// overlap sur frontière de mot/phrase (pas de coupure en milieu de mot).
/// </summary>
public sealed class RecursiveChunker : ITextChunker
{
    /// <summary>Taille maximale d'un chunk en caractères.</summary>
    private const int MaxChunkSize = 800;

    /// <summary>Taille de la fenêtre de recouvrement entre chunks consécutifs.</summary>
    private const int OverlapSize = 100;

    /// <summary>
    /// Séparateurs hiérarchiques pour le découpage récursif, du plus sémantique au plus fin.
    /// L'ordre ". " avant "\n" favorise la coupure sur fin de phrase plutôt que sur saut de ligne visuel,
    /// ce qui produit des fragments plus sémantiques et moins dépendants du formatage source.
    /// </summary>
    private static readonly string[] SentenceSeparators =
        [". ", "! ", "? ", "; ", "\n", " "];

    #region ITextChunker.Chunk

    /// <inheritdoc/>
    public IReadOnlyList<TextChunk> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var result = new List<TextChunk>();
        // Séparateurs de paragraphes POSIX (\n\n) et Windows (\r\n\r\n)
        var rawParagraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

        string? currentSection = null;
        var buffer = new StringBuilder();

        foreach (var raw in rawParagraphs)
        {
            var paragraph = raw.Trim();
            if (string.IsNullOrWhiteSpace(paragraph)) continue;

            // Détection de titre Markdown → mise à jour de la section courante.
            var sectionTitle = ExtractSectionTitle(paragraph);
            if (sectionTitle is not null)
            {
                // On vide le buffer avant de changer de section.
                if (buffer.Length > 0)
                {
                    result.Add(CreateChunk(buffer.ToString(), currentSection));
                    buffer.Clear();
                }
                currentSection = sectionTitle;
                continue;
            }

            // Paragraphe trop long → découpage récursif au niveau phrase/mot.
            if (paragraph.Length > MaxChunkSize)
            {
                if (buffer.Length > 0)
                {
                    result.Add(CreateChunk(buffer.ToString(), currentSection));
                    buffer.Clear();
                }
                foreach (var fragment in SplitAtLevel(paragraph, 0))
                    result.Add(CreateChunk(fragment, currentSection));
                continue;
            }

            // Accumulation normale : on remplit le buffer jusqu'à MaxChunkSize.
            if (buffer.Length > 0 && buffer.Length + 2 + paragraph.Length > MaxChunkSize)
            {
                result.Add(CreateChunk(buffer.ToString(), currentSection));
                // Overlap calculé sur la frontière de phrase ou de mot la plus proche.
                var overlap = GetWordBoundaryOverlap(buffer.ToString());
                buffer.Clear();
                if (!string.IsNullOrWhiteSpace(overlap)) buffer.Append(overlap);
            }

            if (buffer.Length > 0) buffer.Append("\n\n");
            buffer.Append(paragraph);
        }

        if (buffer.Length > 0)
            result.Add(CreateChunk(buffer.ToString(), currentSection));

        return result;
    }

    #endregion

    #region ITextChunker.ChunkPages

    /// <inheritdoc/>
    /// <remarks>
    /// Découpe chaque page indépendamment, puis attache le numéro de page à chaque chunk.
    /// </remarks>
    public IReadOnlyList<TextChunk> ChunkPages(IReadOnlyList<(int PageNumber, string Text)> pages)
    {
        if (pages.Count == 0) return [];

        var allChunks = new List<TextChunk>();
        foreach (var (pageNumber, text) in pages)
        {
            var pageChunks = Chunk(text);
            foreach (var chunk in pageChunks)
                allChunks.Add(chunk with { PageNumber = pageNumber });
        }
        return allChunks;
    }

    #endregion

    #region Découpage récursif interne

    /// <summary>
    /// Découpe <paramref name="text"/> récursivement en utilisant les séparateurs
    /// de <see cref="SentenceSeparators"/> du niveau <paramref name="level"/> vers le bas.
    /// S'arrête dès que le fragment est ≤ MaxChunkSize.
    /// </summary>
    private IEnumerable<string> SplitAtLevel(string text, int level)
    {
        if (text.Length <= MaxChunkSize)
        {
            if (!string.IsNullOrWhiteSpace(text)) yield return text.Trim();
            yield break;
        }

        // Plus aucun séparateur disponible → tronquer à MaxChunkSize pour éviter
        // des chunks surdimensionnés (cas : URL longues, chaînes base64, hash).
        if (level >= SentenceSeparators.Length)
        {
            if (!string.IsNullOrWhiteSpace(text))
                yield return text[..MaxChunkSize].Trim();
            yield break;
        }

        var sep = SentenceSeparators[level];
        var parts = text.Split(sep, StringSplitOptions.RemoveEmptyEntries);

        // Ce séparateur ne découpe pas → on descend d'un niveau.
        if (parts.Length <= 1)
        {
            foreach (var sub in SplitAtLevel(text, level + 1))
                yield return sub;
            yield break;
        }

        var buffer = new StringBuilder();
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Fragment lui-même trop grand → descente récursive.
            if (trimmed.Length > MaxChunkSize)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                }
                foreach (var sub in SplitAtLevel(trimmed, level + 1))
                    yield return sub;
                continue;
            }

            // Accumulation : flush + overlap si dépassement.
            if (buffer.Length > 0 && buffer.Length + sep.Length + trimmed.Length > MaxChunkSize)
            {
                yield return buffer.ToString().Trim();
                // Overlap calculé sur la frontière de mot la plus proche.
                var overlap = GetWordBoundaryOverlap(buffer.ToString());
                buffer.Clear();
                if (!string.IsNullOrWhiteSpace(overlap)) buffer.Append(overlap);
            }

            if (buffer.Length > 0) buffer.Append(sep);
            buffer.Append(trimmed);
        }

        if (buffer.Length > 0 && !string.IsNullOrWhiteSpace(buffer.ToString()))
            yield return buffer.ToString().Trim();
    }

    #endregion

    #region Helpers

    /// <summary>Crée un <see cref="TextChunk"/> avec le contenu et la section courante.</summary>
    private static TextChunk CreateChunk(string content, string? sectionTitle) =>
        new() { Content = content.Trim(), SectionTitle = sectionTitle };

    /// <summary>
    /// Détecte un titre Markdown (H1 à H4) et retourne le texte du titre,
    /// ou <c>null</c> si la ligne n'est pas un titre.
    /// </summary>
    private static string? ExtractSectionTitle(string paragraph)
    {
        // On teste du plus précis (H4) au moins précis (H1) pour éviter les faux positifs.
        if (paragraph.StartsWith("#### ", StringComparison.Ordinal)) return paragraph[5..].Trim();
        if (paragraph.StartsWith("### ",  StringComparison.Ordinal)) return paragraph[4..].Trim();
        if (paragraph.StartsWith("## ",   StringComparison.Ordinal)) return paragraph[3..].Trim();
        if (paragraph.StartsWith("# ",    StringComparison.Ordinal)) return paragraph[2..].Trim();
        return null;
    }

    /// <summary>
    /// Calcule l'overlap en cherchant la frontière de phrase la plus proche
    /// dans la fenêtre de fin du texte, pour éviter toute coupure en milieu de mot.
    /// </summary>
    private static string GetWordBoundaryOverlap(string text)
    {
        if (text.Length <= OverlapSize) return text;

        // On examine la fenêtre de 2× l'overlap depuis la fin pour trouver la dernière frontière.
        var windowStart = Math.Max(0, text.Length - OverlapSize * 2);
        var window = text[windowStart..];

        // LastIndexOf trouve la frontière de phrase la plus proche de la fin,
        // ce qui garantit un overlap d'environ OverlapSize chars (et non 2×OverlapSize avec IndexOf).
        var sentenceCut = window.LastIndexOf(". ", StringComparison.Ordinal);
        if (sentenceCut >= 0 && sentenceCut < window.Length - 2)
            return window[(sentenceCut + 2)..];

        // Fallback : frontière de mot la plus proche de la fin
        var wordCut = window.LastIndexOf(' ');
        if (wordCut >= 0) return window[(wordCut + 1)..];

        // Dernier recours : tronquer sans égard aux mots
        return window[^Math.Min(OverlapSize, window.Length)..];
    }

    #endregion
}