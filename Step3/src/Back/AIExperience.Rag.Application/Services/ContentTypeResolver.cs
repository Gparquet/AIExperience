using AIExperience.Rag.Domain.Interfaces.Services;
using Microsoft.AspNetCore.StaticFiles;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Implémentation par défaut de <see cref="IContentTypeResolver"/>, basée sur la table
/// d'extensions connues d'ASP.NET Core. Sans état, réutilisable en concurrence.
/// </summary>
public sealed class ContentTypeResolver : IContentTypeResolver
{
    private readonly FileExtensionContentTypeProvider _provider = new();

    /// <inheritdoc/>
    public string Resolve(string fileName) =>
        _provider.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";
}
