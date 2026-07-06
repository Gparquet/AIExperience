namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Calcule une empreinte de contenu de fichier, utilisée pour la détection de doublon à l'upload.
/// </summary>
public interface IFileHashService
{
    /// <summary>Calcule le SHA-256 (hex minuscule) du contenu d'un fichier.</summary>
    /// <param name="filePath">Chemin du fichier dont le contenu doit être empreint.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default);
}
