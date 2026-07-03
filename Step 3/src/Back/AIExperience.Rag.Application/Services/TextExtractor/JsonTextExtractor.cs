using System.Text;
using System.Text.Json;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les fichiers JSON : aplatit récursivement la structure en lignes
/// "clé.sous_clé: valeur" (objets imbriqués → chemin en pointillés, tableaux → index entre crochets).
/// </summary>
public sealed class JsonTextExtractor : ITextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        using var document = JsonDocument.Parse(json);

        var sb = new StringBuilder();
        Flatten(document.RootElement, string.Empty, sb);
        return sb.ToString().Trim();
    }

    /// <summary>Aplatit récursivement un <see cref="JsonElement"/> en lignes "chemin: valeur".</summary>
    private static void Flatten(JsonElement element, string path, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                    Flatten(property.Value, childPath, sb);
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{path}[{index}]", sb);
                    index++;
                }
                break;

            default:
                // String/Number/True/False/Null : ToString() retourne la représentation textuelle adaptée.
                sb.AppendLine($"{path}: {element}");
                break;
        }
    }
}
