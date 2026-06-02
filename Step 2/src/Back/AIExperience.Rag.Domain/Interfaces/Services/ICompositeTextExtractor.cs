namespace AIExperience.Rag.Domain.Interfaces.Services;

public interface ICompositeTextExtractor
{
    /// <summary>
    /// Extrait le texte du fichier spécifié.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken);
}
