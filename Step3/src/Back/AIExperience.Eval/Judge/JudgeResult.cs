namespace AIExperience.Eval.Judge;

/// <summary>
/// Résultat du jugement de fidélité. Score null = échec de parsing explicite (jamais un repli
/// silencieux sur 0 ou 1) — le juge étant un modèle local faible, Rationale porte alors le texte
/// brut de la réponse pour permettre une relecture manuelle.
/// </summary>
public sealed record JudgeResult(double? Score, string? Rationale);
