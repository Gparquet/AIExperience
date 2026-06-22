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
    public void ChunkSegments_SingleSegment_ProducesOneChunk()
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
        result[0].Content.Should().Contain("Bonjour tout le monde.");
        result[0].Content.Should().Contain("[00:00:00 → 00:00:05]");
    }

    [Fact]
    public void ChunkSegments_SplitsIntoMultipleChunks_WhenOverMaxSize()
    {
        // 3 segments de ~50 chars chacun avec maxCharsPerChunk = 80
        var segments = Enumerable.Range(0, 3).Select(i => new TranscriptionSegment
        {
            Start = TimeSpan.FromSeconds(i * 10),
            End = TimeSpan.FromSeconds(i * 10 + 9),
            Text = new string('A', 40) // ligne formatée ~60 chars
        }).ToArray();

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 80);

        // Chaque segment dépasse 80 chars une fois formaté avec le timestamp — 3 chunks
        result.Should().HaveCount(3);
    }

    [Fact]
    public void ChunkSegments_GroupsSmallSegments_IntoSingleChunk()
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
        result[0].Content.Should().Contain("Un.");
        result[0].Content.Should().Contain("Deux.");
        result[0].Content.Should().Contain("Trois.");
    }

    [Fact]
    public void ChunkSegments_NeverCutsSegmentInHalf()
    {
        // 2 segments : le 1er remplit presque le buffer, le 2nd doit être dans un chunk séparé
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(10),
                Text = new string('X', 70) // ~96 chars formatés (dépasse 80 si on ajoute le 2nd)
            },
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(10),
                End = TimeSpan.FromSeconds(20),
                Text = "Fin."
            }
        };

        var result = _sut.ChunkSegments(segments, maxCharsPerChunk: 80);

        // Le 1er segment seul dépasse déjà 80 chars → chaque segment dans son propre chunk
        result.Should().HaveCount(2);
        result[0].EndTime.Should().Be(TimeSpan.FromSeconds(10));
        result[1].StartTime.Should().Be(TimeSpan.FromSeconds(10));
        result[1].Content.Should().Contain("Fin.");
    }

    [Fact]
    public void ChunkSegments_ContentContainsTimestampFormat()
    {
        var segments = new[]
        {
            new TranscriptionSegment
            {
                Start = TimeSpan.FromSeconds(3662), // 01:01:02
                End = TimeSpan.FromSeconds(3665),   // 01:01:05
                Text = "Test timestamp."
            }
        };

        var result = _sut.ChunkSegments(segments);

        result[0].Content.Should().Contain("[01:01:02 → 01:01:05]");
    }
}
