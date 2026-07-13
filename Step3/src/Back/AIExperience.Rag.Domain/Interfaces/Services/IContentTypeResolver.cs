namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Résout le content-type MIME d'un fichier à partir de son nom, utilisé par tous les points
/// d'entrée qui reçoivent un fichier (upload document, upload vidéo, ingestion console).
/// </summary>
public interface IContentTypeResolver
{
    /// <summary>Retourne le content-type associé à l'extension du fichier, ou "application/octet-stream" si inconnu.</summary>
    /// <param name="fileName">Nom du fichier (l'extension seule est utilisée).</param>
    string Resolve(string fileName);
}
