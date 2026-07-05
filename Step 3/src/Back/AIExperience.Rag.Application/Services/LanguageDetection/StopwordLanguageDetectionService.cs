using System.Text.RegularExpressions;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services.LanguageDetection;

/// <summary>
/// Détection de langue par fréquence de mots vides (stopwords), sans dépendance externe :
/// choix délibéré (constat I-6) pour rester cohérent avec la philosophie 100% locale du projet
/// (Whisper, pas d'appel cloud) et éviter le risque de compatibilité .NET 10 d'une bibliothèque
/// tierce non vérifiée. Couvre fr/en/es/de/it — suffisant pour discriminer un document mal indexé
/// avec le mauvais dictionnaire Postgres (cas concret du plan : PDF anglais indexé en français).
/// </summary>
public sealed partial class StopwordLanguageDetectionService : ILanguageDetectionService
{
    /// <summary>Langue retournée si le texte est trop court ou le score trop ambigu.</summary>
    private const string DefaultLanguage = "fr";

    /// <summary>Longueur minimale (en caractères) du texte pour tenter une détection.</summary>
    private const int MinTextLength = 50;

    /// <summary>Ratio minimal mots-vides/mots-total pour retenir une langue plutôt que le repli.</summary>
    private const double MinConfidenceRatio = 0.02;

    /// <summary>
    /// Dictionnaire des mots vides (stopwords) par langue ISO 639-1. Liste volontairement
    /// restreinte aux mots les plus fréquents et discriminants de chaque langue.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> StopwordsByLanguage = new()
    {
        ["fr"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "le","la","les","de","des","un","une","et","est","dans","pour","que","qui","ne","pas",
            "sur","avec","au","aux","ce","cette","ces","il","elle","nous","vous","ils","elles",
            "son","sa","ses","plus","mais","ou","donc"
        },
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","of","and","a","to","in","is","you","that","it","he","was","for","on","are","as",
            "with","his","they","at","be","this","have","from","or","one","had","by","word","out"
        },
        ["es"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "el","la","de","que","y","a","en","un","ser","se","no","por","con","su","para","como",
            "estar","tener","lo","todo","pero","hacer","o","poder","decir","este","ir","mundo","vea"
        },
        ["de"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "der","die","das","und","in","zu","den","ist","von","mit","sich","des","auf","für","im",
            "dem","nicht","ein","eine","als","auch","es","an","werden","aus","er","hat","dass","sie","alle"
        },
        ["it"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "il","di","che","è","e","la","per","un","in","non","mi","si","con","lo","ho","ma","ci",
            "come","da","i","questa","quello","gli","le","tu","se","noi","lei","suo","tanti"
        }
    };

    /// <inheritdoc/>
    public string Detect(string text)
    {
        // Repli immédiat si le texte est vide ou trop court pour être fiable.
        if (string.IsNullOrWhiteSpace(text) || text.Length < MinTextLength)
            return DefaultLanguage;

        // Tokenisation simple : suites de lettres Unicode, insensible à la casse.
        var words = WordsRegex().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();
        if (words.Count == 0) return DefaultLanguage;

        // Calcule pour chaque langue le ratio mots-vides/mots-total et retient la meilleure.
        var best = StopwordsByLanguage
            .Select(kv => (Language: kv.Key, Score: (double)words.Count(w => kv.Value.Contains(w)) / words.Count))
            .OrderByDescending(x => x.Score)
            .First();

        // Repli si aucune langue ne se démarque suffisamment (texte trop ambigu).
        return best.Score >= MinConfidenceRatio ? best.Language : DefaultLanguage;
    }

    /// <summary>Regex compilée détectant les suites de lettres Unicode (mots).</summary>
    [GeneratedRegex(@"[\p{L}]+", RegexOptions.Compiled)]
    private static partial Regex WordsRegex();
}
