using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Découpe les segments Whisper en chunks en respectant les frontières naturelles de segments.
/// I-21 : contenu embeddé = texte pur (pas de timestamp inline), timing porté par StartTime/EndTime.
/// I-10 : cible par défaut ~1400 caractères utiles, overlap d'un segment entre chunks consécutifs
/// (évite de perdre le contexte à la frontière), et scission d'un segment isolé surdimensionné
/// (rare, orateur sans pause) avec timestamps interpolés proportionnellement au texte.
/// Garde-fou : l'overlap est abandonné pour un chunk si le segment conservé est déjà trop volumineux
/// pour laisser de la place au segment suivant (évite un chunk jusqu'à ~2x la cible).
/// </summary>
public sealed class TemporalChunker : ITemporalChunker
{
    /// <inheritdoc/>
    public IReadOnlyList<TextChunk> ChunkSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        int maxCharsPerChunk = 1400)
    {
        if (segments.Count == 0)
            return [];

        // I-10 : un segment isolé plus long que la cible est scindé en sous-segments avant chunking.
        var expanded = segments.SelectMany(s => SplitOversizedSegment(s, maxCharsPerChunk)).ToList();

        var chunks = new List<TextChunk>();
        var buffer = new List<TranscriptionSegment>();
        var bufferLength = 0;

        foreach (var segment in expanded)
        {
            var text = segment.Text.Trim();

            // Si le buffer est non vide et que l'ajout dépasserait la limite : flush.
            if (buffer.Count > 0 && bufferLength + text.Length + 1 > maxCharsPerChunk)
            {
                chunks.Add(BuildChunk(buffer));

                // I-10 : overlap — le chunk suivant repart avec le dernier segment du buffer
                // précédent, pour ne pas perdre le contexte à la frontière entre deux chunks.
                var overlapSegment = buffer[^1];
                var overlapText = overlapSegment.Text.Trim();

                // Cas limite : si l'overlap (souvent issu d'un segment surdimensionné scindé,
                // donc déjà proche de maxCharsPerChunk) combiné au segment courant dépasserait
                // réellement la cible, le conserver ferait dépasser le chunk suivant jusqu'à ~2x
                // la cible. On abandonne alors l'overlap pour ce chunk plutôt que de l'imposer.
                // (Taille réelle comparée, sans la marge de +1/élément utilisée par bufferLength.)
                buffer.Clear();
                if (overlapText.Length + 1 + text.Length <= maxCharsPerChunk)
                {
                    buffer.Add(overlapSegment);
                    bufferLength = overlapText.Length + 1;
                }
                else
                {
                    bufferLength = 0;
                }
            }

            buffer.Add(segment);
            bufferLength += text.Length + 1;
        }

        if (buffer.Count > 0)
            chunks.Add(BuildChunk(buffer));

        return chunks;
    }

    /// <summary>Crée un <see cref="TextChunk"/> depuis un buffer de segments : texte pur, sans balisage temporel.</summary>
    private static TextChunk BuildChunk(List<TranscriptionSegment> buffer)
    {
        var content = string.Join(" ", buffer.Select(s => s.Text.Trim()));

        return new TextChunk
        {
            Content = content,
            StartTime = buffer[0].Start,
            EndTime = buffer[^1].End
        };
    }

    /// <summary>
    /// Scinde un segment dont le texte dépasse <paramref name="maxCharsPerChunk"/> en plusieurs
    /// sous-segments sur frontière de mot, avec des timestamps Start/End interpolés
    /// proportionnellement à la position du texte dans le segment d'origine.
    /// Cas limite (I-10) : un orateur qui parle longtemps sans pause produit un unique segment
    /// Whisper anormalement long.
    /// </summary>
    private static IEnumerable<TranscriptionSegment> SplitOversizedSegment(
        TranscriptionSegment segment, int maxCharsPerChunk)
    {
        var text = segment.Text.Trim();
        if (text.Length <= maxCharsPerChunk)
        {
            yield return segment with { Text = text };
            yield break;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxCharsPerChunk)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) parts.Add(current.ToString());

        // Interpolation linéaire des timestamps selon la position de chaque partie dans le texte total.
        var totalDuration = segment.End - segment.Start;
        var totalLength = text.Length;
        var offset = 0;

        foreach (var part in parts)
        {
            var partStart = segment.Start + totalDuration * ((double)offset / totalLength);
            offset += part.Length + 1; // +1 pour l'espace consommé entre les mots
            var partEnd = segment.Start + totalDuration * (Math.Min(offset, totalLength) / (double)totalLength);

            yield return new TranscriptionSegment { Start = partStart, End = partEnd, Text = part };
        }
    }
}
