using AIExperience.Rag.Application.Services;
using FluentAssertions;
using Xunit;

namespace AIExperience.Tests;

public class IngestionServiceEmbeddingProgressTests
{
    [Theory]
    [InlineData(0, 16, 0)]
    [InlineData(1, 16, 1)]
    [InlineData(16, 16, 1)]
    [InlineData(17, 16, 2)]
    [InlineData(120, 16, 8)]
    public void Split_CalculeLeBonNombreDeLots(int total, int batchSize, int expectedBatches)
    {
        // Le nombre de lots conditionne l'affichage « lot i/n » côté UI.
        var batches = IngestionBatching.Split(total, batchSize);
        batches.Count.Should().Be(expectedBatches);
    }

    [Fact]
    public void Split_CouvreTousLesIndicesSansTrou()
    {
        // Les lots doivent couvrir [0, total) exactement une fois, dans l'ordre.
        var batches = IngestionBatching.Split(50, 16);
        batches.SelectMany(b => Enumerable.Range(b.Start, b.Count))
            .Should().BeEquivalentTo(Enumerable.Range(0, 50), o => o.WithStrictOrdering());
    }
}
