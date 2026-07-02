using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Découpe un document texte en fragments (chunks) prêts à être vectorisés.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Découpe un texte brut en une liste de chunks.
    /// </summary>
    /// <param name="text">Texte complet à découper.</param>
    /// <returns>Liste ordonnée de chunks prêts à être vectorisés.</returns>
    IReadOnlyList<TextChunk> Chunk(string text);

    /// <summary>
    /// Découpe une liste de pages en chunks en préservant le numéro de page source.
    /// Chaque chunk produit porte le <see cref="TextChunk.PageNumber"/> de la page d'origine.
    /// </summary>
    /// <param name="pages">
    /// Pages numérotées du document, chacune comme un couple (numéro de page, texte).
    /// </param>
    /// <returns>Liste ordonnée de chunks avec <see cref="TextChunk.PageNumber"/> renseigné.</returns>
    IReadOnlyList<TextChunk> ChunkPages(IReadOnlyList<(int PageNumber, string Text)> pages);
}
