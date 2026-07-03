using System.Text.RegularExpressions;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Parseur pur (sans dépendance LLM) de la réponse du reranker batché (correctif R-18/R-5').
/// Le prompt <c>RagPrompts.RerankerBatch</c> demande un format strict "numéro:score" par ligne,
/// mais les petits modèles locaux ajoutent parfois du texte autour (ex. "Extrait 1: 7") : le
/// regex tolère ce bruit tant que le motif "chiffres:chiffres" reste identifiable.
/// </summary>
public static class BatchedRerankResponseParser
{
    // Sépareur ":" ou "-" toléré (certains modèles confondent les deux formats de liste).
    private static readonly Regex ScoreLinePattern = new(
        @"\b(\d+)\s*[:\-]\s*(\d+)\b", RegexOptions.Compiled);

    /// <summary>
    /// Extrait les couples (index 1-based, score normalisé 0.0-1.0) de la réponse du LLM.
    /// Un score brut hors de la plage 0-10 est ramené dans la plage (clamp) plutôt que rejeté,
    /// car un petit modèle qui répond "15/10" exprime tout de même une intention de score maximal.
    /// En cas d'index dupliqué (ligne répétée), seule la première occurrence est conservée pour
    /// un résultat déterministe.
    /// </summary>
    /// <param name="llmResponse">Texte brut retourné par le LLM.</param>
    /// <returns>Dictionnaire index → score normalisé. Vide si aucune ligne exploitable.</returns>
    public static IReadOnlyDictionary<int, double> Parse(string? llmResponse)
    {
        var scores = new Dictionary<int, double>();

        if (string.IsNullOrWhiteSpace(llmResponse))
            return scores;

        foreach (Match match in ScoreLinePattern.Matches(llmResponse))
        {
            if (!int.TryParse(match.Groups[1].Value, out var index)) continue;
            if (!int.TryParse(match.Groups[2].Value, out var rawScore)) continue;

            var clamped = Math.Clamp(rawScore, 0, 10);
            scores.TryAdd(index, clamped / 10.0);
        }

        return scores;
    }
}
