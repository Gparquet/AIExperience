using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;
using System.Text;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Découpe les segments Whisper en chunks en respectant les frontières naturelles de segments.
/// Algorithme : accumulation de segments jusqu'à saturation de la taille max, puis création d'un chunk.
/// Le contenu inclut les timestamps inline : "[HH:MM:SS → HH:MM:SS] texte".
/// </summary>
public sealed class TemporalChunker : ITemporalChunker
{
    /// <inheritdoc/>
    public IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 800)
    {
        if (segments.Count == 0)
            return [];

        var chunks = new List<TextChunk>();
        var buffer = new List<TranscriptionSegment>();
        var bufferLength = 0;

        foreach (var segment in segments)
        {
            // Ligne formatée : "[HH:MM:SS → HH:MM:SS] texte"
            var line = FormatLine(segment);

            // Si le buffer est non vide et que l'ajout dépasserait la limite : flush
            if (buffer.Count > 0 && bufferLength + line.Length > maxCharsPerChunk)
            {
                chunks.Add(BuildChunk(buffer));
                buffer.Clear();
                bufferLength = 0;
            }

            buffer.Add(segment);
            bufferLength += line.Length;
        }

        // Flush du dernier buffer
        if (buffer.Count > 0)
            chunks.Add(BuildChunk(buffer));

        return chunks;
    }

    /// <summary>Formate un segment en ligne avec timestamp : "[HH:MM:SS → HH:MM:SS] texte".</summary>
    private static string FormatLine(TranscriptionSegment segment)
        => $"[{segment.Start:hh\\:mm\\:ss} → {segment.End:hh\\:mm\\:ss}] {segment.Text.Trim()}\n";

    /// <summary>Crée un TextChunk depuis un buffer de segments.</summary>
    private static TextChunk BuildChunk(List<TranscriptionSegment> buffer)
    {
        var sb = new StringBuilder();
        foreach (var s in buffer)
            sb.Append(FormatLine(s));

        return new TextChunk
        {
            Content = sb.ToString().TrimEnd(),
            StartTime = buffer[0].Start,
            EndTime = buffer[^1].End
        };
    }
}
