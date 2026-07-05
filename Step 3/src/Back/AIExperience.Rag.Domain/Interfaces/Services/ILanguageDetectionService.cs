namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Détecte la langue dominante d'un texte, pour indexer le full-text Postgres avec
/// le bon dictionnaire (constat I-6 du plan Lot 2) au lieu de la valeur "french" figée.
/// </summary>
public interface ILanguageDetectionService
{
    /// <summary>
    /// Retourne un code ISO 639-1 ("fr", "en", ...) détecté à partir du texte fourni.
    /// Retourne la langue par défaut de l'implémentation si le texte est trop court
    /// ou si aucune langue ne se démarque clairement.
    /// </summary>
    /// <param name="text">Texte à analyser (ex. contenu extrait d'un document).</param>
    string Detect(string text);
}
