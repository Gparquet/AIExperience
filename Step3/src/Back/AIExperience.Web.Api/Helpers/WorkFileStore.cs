namespace AIExperience.Web.Api.Helpers;

/// <summary>
/// Écrit un fichier uploadé dans le répertoire de travail durable de l'ingestion, sous un nom basé
/// sur l'identifiant du document. Contrairement à l'ancien fichier temporaire lié à la durée de la
/// requête, ce fichier doit survivre à la réponse HTTP : il n'est lu que plus tard, par le worker
/// en arrière-plan, et c'est ce même worker qui le supprime une fois le traitement terminé.
/// </summary>
public static class WorkFileStore
{
    /// <summary>Écrit le fichier reçu dans le répertoire de travail et retourne son chemin absolu.</summary>
    public static async Task<string> SaveAsync(IFormFile file, Guid documentId, string workDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(workDirectory);
        var path = Path.Combine(workDirectory, $"{documentId}{Path.GetExtension(file.FileName)}");

        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, cancellationToken);

        return path;
    }
}
