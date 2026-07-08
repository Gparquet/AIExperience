namespace AIExperience.Web.Api.Helpers;

/// <summary>
/// Représente un fichier uploadé (<see cref="IFormFile"/>) copié temporairement sur disque.
/// Supprime automatiquement le fichier lors du Dispose, évitant la duplication du pattern
/// "créer chemin temp → écrire le flux → finally supprimer" présent dans plusieurs controllers.
/// </summary>
public sealed class TempUploadedFile : IAsyncDisposable
{
    /// <summary>
    /// Chemin absolu du fichier temporaire sur disque.
    /// </summary>
    public string Path { get; }

    private TempUploadedFile(string path)
    {
        Path = path;
    }

    /// <summary>
    /// Copie le contenu du fichier uploadé vers un fichier temporaire avec la même extension
    /// et retourne l'instance permettant d'y accéder puis de le nettoyer automatiquement.
    /// </summary>
    public static async Task<TempUploadedFile> CreateAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            Guid.NewGuid() + System.IO.Path.GetExtension(file.FileName));

        await using (var stream = File.Create(tempPath))
            await file.CopyToAsync(stream, cancellationToken);

        return new TempUploadedFile(tempPath);
    }

    /// <summary>
    /// Supprime le fichier temporaire s'il existe encore.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (File.Exists(Path))
            File.Delete(Path);

        return ValueTask.CompletedTask;
    }
}
