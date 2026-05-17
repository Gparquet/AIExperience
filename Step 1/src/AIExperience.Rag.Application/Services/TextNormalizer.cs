using AIExperience.Rag.Domain.Interfaces.Services;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AIExperience.Rag.Application.Services;

public sealed class TextNormalizer : ITextNomalize
{
    public string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        //décomposition des caractères accentués en caractères de base
        var normalizedText = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in normalizedText)
        {
            // Suppression des caractères de diacritiques
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            // Seuls les caractères qui ne sont pas des marques de non-espacement (diacritiques) sont ajoutés au StringBuilder
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        }

        // Recomposition du texte normalisé
        normalizedText = sb.ToString().Normalize(NormalizationForm.FormC);
        // Mise en minscules et suppression de la ponctuation
        normalizedText = normalizedText.ToLowerInvariant();
        normalizedText = Regex.Replace(normalizedText, @"[^\w\s]", " ");
        normalizedText = Regex.Replace(normalizedText, @"\s+", " ").Trim();

        // Implémentation de la normalisation du texte
        return sb.ToString();
    }
}
