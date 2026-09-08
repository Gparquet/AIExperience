namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Nettoie et valide la sortie brute du LLM de condensation. Logique pure, testable sans
/// dépendance au LLM — même esprit que <see cref="BatchedRerankResponseParser"/> : un petit modèle
/// local ajoute parfois un préambule ("Question reformulée : ...") ou des guillemets malgré la
/// consigne stricte du prompt ; ce bruit est toléré plutôt que de rejeter la condensation à la
/// moindre déviation de format.
/// </summary>
public static class CondensationResponseCleaner
{
    // Une question condensée reste une question courte ; une réponse bien plus longue signale que
    // le modèle a halluciné une réponse complète au lieu de reformuler la question.
    private const int MaxLength = 500;

    private static readonly string[] NoisePrefixes =
    [
        "question reformulée", "question autonome", "question condensée", "reformulation", "voici la question"
    ];

    /// <summary>
    /// Retourne la question condensée nettoyée, ou <c>null</c> si la réponse est inexploitable
    /// (vide ou trop longue).
    /// </summary>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim().Trim('"', '«', '»');

        // Retire un éventuel préambule ("Question reformulée : ...") que le modèle ajoute parfois
        // malgré la consigne du prompt de ne répondre qu'avec la question reformulée.
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex is > 0 and < 40)
        {
            var label = trimmed[..colonIndex].Trim().ToLowerInvariant();
            if (Array.Exists(NoisePrefixes, p => label.Contains(p)))
                trimmed = trimmed[(colonIndex + 1)..].Trim().Trim('"', '«', '»');
        }

        return trimmed.Length is > 0 and <= MaxLength ? trimmed : null;
    }
}
