namespace AIExperience.Rag.Domain.Interfaces.Services;

public interface ITextExtractor
{
    /// <summary>
    /// Permet de savoir si le fichier peut être traité par cet extracteur de texte. 
    /// Par exemple, un extracteur de texte PDF ne pourra traiter que les fichiers PDF.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public bool CanHandle(string filePath);

    /// <summary>
    /// Extrait le texte du fichier spécifié.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken);
}
