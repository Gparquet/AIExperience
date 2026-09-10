using AIExperience.Eval.Metrics;
using AIExperience.Rag.Domain.Entities;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="RecallCalculator"/> : correspondance document seul, page exacte,
/// chevauchement de plage horaire (vidéo), et rang de la première citation correspondante.
/// </summary>
public sealed class RecallCalculatorTests
{
    private static Citation MakeCitation(Guid documentId, int? page = null, TimeSpan? start = null, TimeSpan? end = null) =>
        Citation.Create(Guid.Empty, documentId, "Document", "extrait", 0.8, page, startTime: start, endTime: end);

    [Fact]
    public void Evaluate_DocumentIdSeulSansPageNiPlage_HitSurCorrespondanceDocumentId()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId) };
        var citations = new[] { MakeCitation(Guid.NewGuid()), MakeCitation(docId) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeTrue();
        result.Rank.Should().Be(2);
    }

    [Fact]
    public void Evaluate_PageAttendueExacte_Hit()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, PageNumber: 1) };
        var citations = new[] { MakeCitation(docId, page: 1) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeTrue();
        result.Rank.Should().Be(1);
    }

    [Fact]
    public void Evaluate_PageDifferente_Miss()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, PageNumber: 1) };
        var citations = new[] { MakeCitation(docId, page: 2) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeFalse();
        result.Rank.Should().BeNull();
    }

    [Fact]
    public void Evaluate_PlageHoraireChevauchante_Hit()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, StartTimeSeconds: 100, EndTimeSeconds: 200) };
        var citations = new[] { MakeCitation(docId, start: TimeSpan.FromSeconds(150), end: TimeSpan.FromSeconds(250)) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PlageHoraireDisjointe_Miss()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, StartTimeSeconds: 100, EndTimeSeconds: 200) };
        var citations = new[] { MakeCitation(docId, start: TimeSpan.FromSeconds(300), end: TimeSpan.FromSeconds(400)) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RangDeLaPremiereCitationCorrespondante()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId) };
        var citations = new[] { MakeCitation(Guid.NewGuid()), MakeCitation(Guid.NewGuid()), MakeCitation(docId) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Rank.Should().Be(3);
    }

    [Fact]
    public void Evaluate_ListeCitationsVide_Miss()
    {
        var result = RecallCalculator.Evaluate([new ExpectedSource(Guid.NewGuid())], []);

        result.Hit.Should().BeFalse();
        result.Rank.Should().BeNull();
    }
}
