using System.Globalization;
using System.Text.RegularExpressions;

namespace AIExperience.Eval.Judge;

/// <summary>
/// Parseur pur de la réponse du juge LLM (fidélité) — même philosophie que
/// BatchedRerankResponseParser : tolérant au bruit d'un petit modèle local, jamais de valeur
/// par défaut silencieuse en cas d'échec.
/// </summary>
public static class JudgeResponseParser
{
    // (?!\d) évite qu'un score "10" soit lu comme "1" tronqué.
    private static readonly Regex ScoreRegex = new(
        @"SCORE\s*[:\-]\s*([01](?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RationaleRegex = new(
        @"JUSTIFICATION\s*[:\-]\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static JudgeResult Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new JudgeResult(null, response);

        var scoreMatch = ScoreRegex.Match(response);
        if (!scoreMatch.Success)
            return new JudgeResult(null, response.Trim());

        var rawScore = scoreMatch.Groups[1].Value.Replace(',', '.');
        if (!double.TryParse(rawScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
            return new JudgeResult(null, response.Trim());

        // Seules les 3 valeurs du barème sont acceptées (voir EvalPrompts.JudgeSystem) : une
        // valeur hors barème (ex. 0.7) signale un non-respect de la consigne par le modèle,
        // traité comme un échec de parsing plutôt qu'arrondi silencieusement.
        if (score != 0.0 && score != 0.5 && score != 1.0)
            return new JudgeResult(null, response.Trim());

        var rationaleMatch = RationaleRegex.Match(response);
        var rationale = rationaleMatch.Success ? rationaleMatch.Groups[1].Value.Trim() : response.Trim();

        return new JudgeResult(score, rationale);
    }
}
