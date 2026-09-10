using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Eval.Reporting;

/// <summary>Résultat d'une question golden pour un run donné. Error non-null si AskAsync a levé
/// une exception pour cette question (voir EvalRunner) — les autres champs restent alors neutres.</summary>
public sealed record EvalQuestionResult(
    string QuestionId,
    string Question,
    string Answer,
    RagStrategy StrategyUsed,
    bool RecallHit,
    int? RecallRank,
    double? FidelityScore,
    string? FidelityRationale,
    long DurationMs,
    string? Error = null);
