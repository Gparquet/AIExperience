using AIExperience.Eval.Reporting;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Infrastructure.Options;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="RunComparer"/> : détection des changements de recall (hit/miss),
/// delta de fidélité, et robustesse quand une question n'existe que dans un des deux rapports.
/// </summary>
public sealed class RunComparerTests
{
    private static EvalRunReport MakeReport(params EvalQuestionResult[] questions) =>
        EvalRunReport.Build(DateTimeOffset.UtcNow, new RagOptions(), questions);

    private static EvalQuestionResult MakeQuestion(string id, bool recallHit, double? fidelity) =>
        new(id, "Q ?", "Réponse", RagStrategy.Adaptive, recallHit, recallHit ? 1 : null, fidelity, "justification", 100);

    [Fact]
    public void Compare_RecallHitDevientMiss_EstDetecte()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));
        var after = MakeReport(MakeQuestion("q1", recallHit: false, fidelity: 0.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions[0].RecallChange.Should().Be(RecallChange.HitToMiss);
    }

    [Fact]
    public void Compare_RecallMissDevientHit_EstDetecte()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: false, fidelity: 0.0));
        var after = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions[0].RecallChange.Should().Be(RecallChange.MissToHit);
    }

    [Fact]
    public void Compare_DeltaDeFideliteCalcule()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 0.5));
        var after = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions[0].FidelityDelta.Should().Be(0.5);
    }

    [Fact]
    public void Compare_QuestionAbsenteDUnDesDeuxRapports_NeLeveAucuneException()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));
        var after = MakeReport(MakeQuestion("q2", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions.Should().HaveCount(2);
        comparison.Questions.Single(q => q.QuestionId == "q1").RecallHitAfter.Should().BeNull();
        comparison.Questions.Single(q => q.QuestionId == "q2").RecallHitBefore.Should().BeNull();
    }

    [Fact]
    public void Compare_DeltaAgregeDeRecallAtK()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: false, fidelity: 0.0));
        var after = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.RecallAtKDelta.Should().Be(1.0);
    }
}
