using AIExperience.Rag.Application.Services;
using AIExperience.Rag.Domain.Models.Video;
using FluentAssertions;

namespace AIExperience.Tests;

public sealed class TemporalChunkerTests
{
    private readonly TemporalChunker _sut = new();

    [Fact]
    public void ChunkSegments_ReturnsEmpty_WhenNoSegments()
    {
        var result = _sut.ChunkSegments([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkSegments_SingleSegment_ProducesOneChunk_WithPlainTextContent()
    {
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(5),
                Text = "Bonjour tout le monde."
            }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 800);

        result.Should().HaveCount(1);
        result[0].StartTime.Should().Be(TimeSpan.FromSeconds(0));
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(5));
        // I-21 : le contenu embeddé ne doit plus contenir de balisage temporel inline.
        result[0].Content.Should().Be("Bonjour tout le monde.");
        result[0].Content.Should().NotContain("[");
        result[0].Content.Should().NotContain("→");
    }

    [Fact]
    public void ChunkSegments_GroupsSmallSegments_IntoSingleChunk_JoinedBySpace()
    {
        var segments = new[]
        {
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(2), Text = "Un." },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(2), End = TimeSpan.FromSeconds(4), Text = "Deux." },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(4), End = TimeSpan.FromSeconds(6), Text = "Trois." }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 800);

        result.Should().HaveCount(1);
        result[0].StartTime.Should().Be(TimeSpan.FromSeconds(0));
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(6));
        result[0].Content.Should().Be("Un. Deux. Trois.");
    }

    [Fact]
    public void ChunkSegments_SplitsIntoMultipleChunks_WhenOverMaxSize()
    {
        // 3 segments de 40 caractères, maxCharsPerChunk = 80 : deux segments accolés (81 avec
        // l'espace de jonction) dépassent déjà la limite, donc chaque segment forme son propre chunk.
        var segments = Enumerable.Range(0, 3).Select(i => new TranscriptionSegment
        {
            Start = TimeSpan.FromSeconds(i * 10),
            End = TimeSpan.FromSeconds(i * 10 + 9),
            Text = new string('A', 40)
        }).ToArray();

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 80);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void ChunkSegments_NeverMergesBeyondMaxSize()
    {
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(10),
                Text = new string('X', 45)
            },
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(10),
                End = TimeSpan.FromSeconds(20),
                Text = "Fin."
            }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 50);

        result.Should().HaveCount(2);
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(10));
        // I-10 : avec overlap, chunk[1] reprend le dernier segment de chunk[0] pour préserver le contexte.
        result[1].StartTime.Should().Be(TimeSpan.FromSeconds(0));
        result[1].Content.Should().Contain("Fin.");
        // Vérifier que les chunks ne dépassent pas la limite.
        result.Should().AllSatisfy(c => c.Content.Length.Should().BeLessThanOrEqualTo(50));
    }

    [Fact]
    public void ChunkSegments_DefaultSize_GroupsSegmentsBeyondOldEightHundredLimit()
    {
        // I-10 : la cible par défaut passe de 800 à ~1400 caractères utiles.
        var segments = new[]
        {
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(0),  End = TimeSpan.FromSeconds(10), Text = new string('A', 500) },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(20), Text = new string('B', 500) }
        };

        var result = _sut.ChunkSegments(segments); // taille par défaut, non spécifiée

        // 500 + 1 + 500 = 1001 <= nouvelle cible (~1400) : un seul chunk (aurait été 2 avec l'ancienne limite de 800).
        result.Should().HaveCount(1);
    }

    [Fact]
    public void ChunkSegments_OversizedSegment_IsSplitOnWordBoundaries_WithInterpolatedTimestamps()
    {
        var longText = string.Join(" ", Enumerable.Repeat("mot", 100)); // 399 caractères
        var segment = new TranscriptionSegment
        {
            Start = TimeSpan.FromSeconds(0),
            End = TimeSpan.FromSeconds(100),
            Text = longText
        };

        var result = _sut.ChunkSegments([segment], maxCharsPerChunk: 100);

        result.Count.Should().BeGreaterThan(1);
        // Régression : l'overlap ne doit jamais faire dépasser un chunk de la cible, y compris
        // quand les sous-segments issus de la scission sont eux-mêmes proches de maxCharsPerChunk.
        result.Should().AllSatisfy(c => c.Content.Length.Should().BeLessThanOrEqualTo(100));
        result[0].StartTime.Should().Be(TimeSpan.FromSeconds(0));
        result[^1].EndTime!.Value.TotalSeconds.Should().BeApproximately(100, 1);

        for (var i = 1; i < result.Count; i++)
            result[i].StartTime!.Value.Should().BeGreaterThanOrEqualTo(result[i - 1].StartTime!.Value);
    }

    [Fact]
    public void ChunkSegments_OverlapsLastSegment_BetweenConsecutiveChunks()
    {
        var a = new string('A', 60);
        var b = new string('B', 60);
        var c = new string('C', 60);
        var d = new string('D', 60);

        var segments = new[]
        {
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(0),  End = TimeSpan.FromSeconds(5),  Text = a },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(5),  End = TimeSpan.FromSeconds(10), Text = b },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(15), Text = c },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(15), End = TimeSpan.FromSeconds(20), Text = d }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 130);

        result.Should().HaveCount(3);
        result[0].Content.Should().Be($"{a} {b}");
        // Chunk 2 reprend B (overlap du chunk précédent) avant son propre contenu C.
        result[1].Content.Should().Be($"{b} {c}");
        result[2].Content.Should().Be($"{c} {d}");
    }

    [Fact]
    public void ChunkSegments_DropsOverlap_WhenOverlapSegmentAloneIsAlreadyNearTarget()
    {
        // Cas limite (segment Whisper anormalement long) : le segment d'overlap occupe déjà la
        // quasi-totalité de la cible. Le conserver en tête du chunk suivant, combiné au segment
        // courant, ferait dépasser la cible jusqu'à ~2x — l'overlap doit donc être abandonné.
        var overlapCandidate = new string('A', 90);
        var next = new string('B', 90);

        var segments = new[]
        {
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(5), Text = "Court." },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(10), Text = overlapCandidate },
            new TranscriptionSegment { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(15), Text = next }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 100);

        result.Should().AllSatisfy(c => c.Content.Length.Should().BeLessThanOrEqualTo(100));
        // Le segment "next" doit se retrouver seul dans son chunk (overlap abandonné), pas
        // accolé à overlapCandidate (ce qui aurait produit un chunk de 90+1+90=181 caractères).
        result.Should().Contain(c => c.Content == next);
    }

}
