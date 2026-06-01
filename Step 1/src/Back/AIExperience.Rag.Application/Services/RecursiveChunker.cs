using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Chunker récursif hiérarchique : découpe d'abord par paragraphes, puis par phrases
/// si les paragraphes dépassent la taille maximale. Recommandé pour les documents structurés (PDF, GBCP).
/// </summary>
public sealed class RecursiveChunker : ITextChunker
{
    private const int MaxChunkSize = 800;
    private const int OverlapSize = 100;

    /// <inheritdoc/>
    public IReadOnlyList<TextChunk> Chunk(string text)
    {
        var chunks = new List<TextChunk>();
        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

        var current = new System.Text.StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length + paragraph.Length > MaxChunkSize && current.Length > 0)
            {
                chunks.Add(new TextChunk { Content = current.ToString().Trim() });
                var overlap = current.ToString()[^Math.Min(OverlapSize, current.Length)..];
                current.Clear();
                current.Append(overlap);
            }
            current.AppendLine(paragraph);
        }

        if (current.Length > 0)
            chunks.Add(new TextChunk { Content = current.ToString().Trim() });

        return chunks;
    }
}