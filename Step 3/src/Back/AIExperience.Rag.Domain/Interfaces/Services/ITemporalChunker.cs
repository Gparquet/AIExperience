using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Découpe une liste de segments Whisper en chunks <see cref="TextChunk"/> enrichis de timestamps.
/// Chaque chunk regroupe des segments consécutifs jusqu'à la taille maximale,
/// sans jamais couper au milieu d'un segment.
/// </summary>
public interface ITemporalChunker
{
    /// <summary>
    /// Regroupe les segments en chunks de taille maximale <paramref name="maxCharsPerChunk"/>.
    /// Chaque chunk contient les timestamps de début et de fin de ses segments.
    /// </summary>
    /// <param name="segments">Liste ordonnée de segments Whisper.</param>
    /// <param name="maxCharsPerChunk">Taille maximale en caractères par chunk (défaut : 800).</param>
    /// <returns>Liste ordonnée de chunks avec timestamps.</returns>
    IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 800);
}
