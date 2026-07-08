namespace AIExperience.Rag.Infrastructure.VectorStore;

/// <summary>
/// Mappe un code de langue ISO 639-1 vers le nom de configuration de recherche texte
/// Postgres (<c>regconfig</c>) correspondant (constat I-6 du plan Lot 2).
/// Repli sur "simple" (tokenisation sans stemming) pour toute langue non mappée :
/// garantit qu'aucun appel SQL n'échoue pour cause de regconfig invalide.
/// </summary>
public static class PostgresTextSearchConfig
{
    private static readonly Dictionary<string, string> RegConfigByIsoCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = "french",
        ["en"] = "english",
        ["es"] = "spanish",
        ["de"] = "german",
        ["it"] = "italian",
    };

    /// <summary>Retourne le nom de configuration Postgres pour un code ISO, ou "simple" si inconnu.</summary>
    public static string Resolve(string? isoLanguageCode) =>
        isoLanguageCode is not null && RegConfigByIsoCode.TryGetValue(isoLanguageCode, out var regconfig)
            ? regconfig
            : "simple";
}
