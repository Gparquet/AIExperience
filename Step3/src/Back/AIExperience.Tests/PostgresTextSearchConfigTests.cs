using AIExperience.Rag.Infrastructure.VectorStore;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="PostgresTextSearchConfig"/> (constat I-6 du plan Lot 2).
/// Vérifie le mapping code ISO 639-1 → nom de configuration Postgres (regconfig),
/// avec repli sur "simple" pour toute langue non mappée (jamais d'échec SQL).
/// </summary>
public sealed class PostgresTextSearchConfigTests
{
    [Theory]
    [InlineData("fr", "french")]
    [InlineData("FR", "french")]
    [InlineData("en", "english")]
    [InlineData("es", "spanish")]
    [InlineData("de", "german")]
    [InlineData("it", "italian")]
    public void Resolve_KnownIsoCode_ReturnsExpectedRegConfig(string isoCode, string expected)
    {
        PostgresTextSearchConfig.Resolve(isoCode).Should().Be(expected);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_UnknownOrMissingIsoCode_FallsBackToSimple(string? isoCode)
    {
        PostgresTextSearchConfig.Resolve(isoCode).Should().Be("simple");
    }
}
