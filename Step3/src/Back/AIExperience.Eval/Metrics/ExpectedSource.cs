namespace AIExperience.Eval.Metrics;

/// <summary>
/// Source attendue pour une question golden, avec le DocumentId déjà résolu (le jeu de données
/// référence les documents par nom de fichier — voir GoldenDatasetLoader/EvalRunner).
/// </summary>
public sealed record ExpectedSource(
    Guid DocumentId,
    int? PageNumber = null,
    double? StartTimeSeconds = null,
    double? EndTimeSeconds = null);
