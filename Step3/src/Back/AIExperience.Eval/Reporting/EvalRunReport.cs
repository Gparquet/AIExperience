using AIExperience.Rag.Infrastructure.Options;

namespace AIExperience.Eval.Reporting;

/// <summary>
/// Rapport agrégé d'un run d'évaluation. RagOptionsSnapshot permet, en comparant deux rapports,
/// de savoir QUOI a changé entre deux runs (pas seulement les métriques).
/// </summary>
public sealed record EvalRunReport(
    DateTimeOffset RunAt,
    RagOptions RagOptionsSnapshot,
    IReadOnlyList<EvalQuestionResult> Questions,
    double MeanRecallAtK,
    double MeanReciprocalRank,
    double? MeanFidelityScore,
    int FidelityParseFailures)
{
    public static EvalRunReport Build(DateTimeOffset runAt, RagOptions ragOptions, IReadOnlyList<EvalQuestionResult> questions)
    {
        var meanRecall = questions.Count == 0 ? 0.0 : questions.Count(q => q.RecallHit) / (double)questions.Count;
        var meanReciprocalRank = questions.Count == 0 ? 0.0
            : questions.Average(q => q is { RecallHit: true, RecallRank: { } rank } ? 1.0 / rank : 0.0);

        var fidelityScores = questions.Where(q => q.FidelityScore is not null).Select(q => q.FidelityScore!.Value).ToList();
        double? meanFidelity = fidelityScores.Count > 0 ? fidelityScores.Average() : null;

        // Un échec de parsing se distingue d'une question en échec (Error renseigné) : seul le
        // premier compte comme "échec de parsing" au sens du rapport.
        var parseFailures = questions.Count(q => q.FidelityScore is null && q.Error is null);

        return new EvalRunReport(runAt, ragOptions, questions, meanRecall, meanReciprocalRank, meanFidelity, parseFailures);
    }
}
