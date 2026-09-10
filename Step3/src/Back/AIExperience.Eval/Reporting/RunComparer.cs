namespace AIExperience.Eval.Reporting;

public enum RecallChange { Unchanged, HitToMiss, MissToHit }

public sealed record QuestionComparison(
    string QuestionId,
    bool? RecallHitBefore,
    bool? RecallHitAfter,
    RecallChange RecallChange,
    double? FidelityScoreBefore,
    double? FidelityScoreAfter,
    double? FidelityDelta);

public sealed record RunComparison(
    IReadOnlyList<QuestionComparison> Questions,
    double RecallAtKDelta,
    double MeanReciprocalRankDelta,
    double? MeanFidelityScoreDelta);

/// <summary>
/// Compare deux rapports de run pour un avant/après (ex. avant/après un correctif du pipeline
/// RAG). Une question absente de l'un des deux rapports (id désynchronisé, jeu de données modifié
/// entre les deux runs) est comparée avec des valeurs "avant"/"après" à null plutôt que de faire
/// échouer la comparaison.
/// </summary>
public static class RunComparer
{
    public static RunComparison Compare(EvalRunReport before, EvalRunReport after)
    {
        var beforeById = before.Questions.ToDictionary(q => q.QuestionId);
        var afterById = after.Questions.ToDictionary(q => q.QuestionId);
        var allIds = beforeById.Keys.Union(afterById.Keys);

        var comparisons = allIds.Select(id =>
        {
            beforeById.TryGetValue(id, out var b);
            afterById.TryGetValue(id, out var a);

            var recallChange = (b?.RecallHit, a?.RecallHit) switch
            {
                (true, false) => RecallChange.HitToMiss,
                (false, true) => RecallChange.MissToHit,
                _ => RecallChange.Unchanged
            };

            double? fidelityDelta = b?.FidelityScore is { } fb && a?.FidelityScore is { } fa ? fa - fb : null;

            return new QuestionComparison(id, b?.RecallHit, a?.RecallHit, recallChange, b?.FidelityScore, a?.FidelityScore, fidelityDelta);
        }).ToList();

        double? meanFidelityDelta = before.MeanFidelityScore is { } mfb && after.MeanFidelityScore is { } mfa
            ? mfa - mfb : null;

        return new RunComparison(
            comparisons,
            after.MeanRecallAtK - before.MeanRecallAtK,
            after.MeanReciprocalRank - before.MeanReciprocalRank,
            meanFidelityDelta);
    }
}
