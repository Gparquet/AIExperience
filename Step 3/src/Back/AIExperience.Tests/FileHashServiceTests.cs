using AIExperience.Rag.Application.Services;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="FileHashService"/> : vérifie le calcul du SHA-256 d'un fichier réel
/// (extrait de la logique auparavant dupliquée dans DocumentsController et App.Console).
/// </summary>
public sealed class FileHashServiceTests
{
    [Fact]
    public async Task ComputeSha256Async_KnownContent_ReturnsExpectedHash()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var contentBytes = Encoding.UTF8.GetBytes("hello world");
            await File.WriteAllBytesAsync(tempPath, contentBytes);
            var expectedHash = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();
            var service = new FileHashService();

            var hash = await service.ComputeSha256Async(tempPath);

            hash.Should().Be(expectedHash);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ComputeSha256Async_SameContent_ReturnsSameHashRegardlessOfPath()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(pathA, "contenu identique");
            await File.WriteAllTextAsync(pathB, "contenu identique");
            var service = new FileHashService();

            var hashA = await service.ComputeSha256Async(pathA);
            var hashB = await service.ComputeSha256Async(pathB);

            hashA.Should().Be(hashB);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }
}
