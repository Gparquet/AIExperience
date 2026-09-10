namespace AIExperience.Eval.GoldenDataset;

/// <summary>Source attendue référencée par nom de fichier (pas par DocumentId, qui change à chaque ré-ingestion).</summary>
public sealed record ExpectedSourceDto(
    string FileName,
    int? PageNumber = null,
    double? StartTimeSeconds = null,
    double? EndTimeSeconds = null);

/// <summary>Une entrée du jeu de données golden.</summary>
public sealed record GoldenQuestionDto(
    string Id,
    string Question,
    IReadOnlyList<string> ExpectedAnswerKeyPoints,
    IReadOnlyList<ExpectedSourceDto> ExpectedSources);

/// <summary>Racine du fichier JSON du jeu de données golden.</summary>
public sealed record GoldenDatasetDto(IReadOnlyList<GoldenQuestionDto> Questions);
