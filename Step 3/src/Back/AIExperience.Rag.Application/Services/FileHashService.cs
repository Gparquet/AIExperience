using AIExperience.Rag.Domain.Interfaces.Services;
using System.Security.Cryptography;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Implémentation par défaut de <see cref="IFileHashService"/> : lit le fichier en flux et calcule son SHA-256.
/// Sans état, réutilisable en concurrence.
/// </summary>
public sealed class FileHashService : IFileHashService
{
    /// <inheritdoc/>
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
