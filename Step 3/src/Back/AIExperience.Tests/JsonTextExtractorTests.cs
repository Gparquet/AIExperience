using AIExperience.Rag.Application.Services.TextExtractor;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="JsonTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'aplatissement récursif JSON en lignes "clé.sous_clé: valeur".
/// </summary>
public sealed class JsonTextExtractorTests
{
    private readonly JsonTextExtractor _sut = new();

    [Theory]
    [InlineData("data.json")]
    [InlineData("CONFIG.JSON")]
    public void CanHandle_JsonFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonJsonFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTextAsync_FlatObject_ReturnsKeyValueLines()
    {
        var path = CreateTempJson("""{ "titre": "Rapport annuel", "annee": 2026 }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("titre: Rapport annuel");
        result.Should().Contain("annee: 2026");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_NestedObject_FlattensWithDottedPath()
    {
        var path = CreateTempJson("""{ "auteur": { "nom": "Dupont", "role": "Directeur" } }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("auteur.nom: Dupont");
        result.Should().Contain("auteur.role: Directeur");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_ArrayOfObjects_FlattensWithIndexedPath()
    {
        var path = CreateTempJson("""{ "items": [ { "nom": "Stylo" }, { "nom": "Cahier" } ] }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("items[0].nom: Stylo");
        result.Should().Contain("items[1].nom: Cahier");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_BooleanTrue_SerializesAsLowercaseTrue()
    {
        var path = CreateTempJson("""{ "actif": true }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("actif: true");
        result.Should().NotContain("actif: True");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_BooleanFalse_SerializesAsLowercaseFalse()
    {
        var path = CreateTempJson("""{ "disponible": false }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("disponible: false");
        result.Should().NotContain("disponible: False");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_NullValue_SerializesAsNull()
    {
        var path = CreateTempJson("""{ "description": null }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("description: null");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyArray_PreservesKeyWithEmptyArrayMarker()
    {
        var path = CreateTempJson("""{ "tags": [] }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("tags: []");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyObject_PreservesKeyWithEmptyObjectMarker()
    {
        var path = CreateTempJson("""{ "metadata": {} }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("metadata: {}");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_MixedTypes_HandlesAllCorrectly()
    {
        var path = CreateTempJson("""{ "titre": "Doc", "actif": true, "views": 42, "notes": null, "tags": [], "config": {} }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("titre: Doc");
        result.Should().Contain("actif: true");
        result.Should().Contain("views: 42");
        result.Should().Contain("notes: null");
        result.Should().Contain("tags: []");
        result.Should().Contain("config: {}");

        File.Delete(path);
    }

    private static string CreateTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
