using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Domain.Interfaces.Services;

public interface ITextChunker
{
    /// <summary>
    /// Découpe un texte brut en une liste de chunks.
    /// </summary>
    /// <param name="text">Texte complet à découper.</param>
    /// <returns>Liste ordonnée de chunks prêts à être vectorisés.</returns>
    IReadOnlyList<TextChunk> Chunk(string text);
}
