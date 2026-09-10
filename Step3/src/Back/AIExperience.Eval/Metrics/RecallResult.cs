namespace AIExperience.Eval.Metrics;

/// <summary>Résultat du calcul de recall pour une question : correspondance trouvée et son rang.</summary>
public sealed record RecallResult(bool Hit, int? Rank);
