using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Infrastructure.AI.Rag;
using AIExperience.Rag.Infrastructure.Options;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="RagStrategyResolver"/> (constat R-17) : vérifie que les flags
/// <c>HyDE.Enabled</c>/<c>MultiQuery.Enabled</c> sont bien honorés par un repli sur Direct.
/// </summary>
public sealed class RagStrategyResolverTests
{
    [Fact]
    public void ResolveFallback_HydeRoutedButDisabled_FallsBackToDirect()
    {
        var options = new RagOptions();
        options.HyDE.Enabled = false;

        var result = RagStrategyResolver.ResolveFallback(RagStrategy.HyDE, options);

        result.Should().Be(RagStrategy.Direct,
            because: "HyDE désactivé en configuration ne doit jamais être exécuté");
    }

    [Fact]
    public void ResolveFallback_FusionRoutedButDisabled_FallsBackToDirect()
    {
        var options = new RagOptions();
        options.MultiQuery.Enabled = false;

        var result = RagStrategyResolver.ResolveFallback(RagStrategy.Fusion, options);

        result.Should().Be(RagStrategy.Direct,
            because: "Fusion (MultiQuery) désactivée en configuration ne doit jamais être exécutée");
    }

    [Fact]
    public void ResolveFallback_HydeRoutedAndEnabled_StaysHyde()
    {
        var options = new RagOptions();
        options.HyDE.Enabled = true;

        var result = RagStrategyResolver.ResolveFallback(RagStrategy.HyDE, options);

        result.Should().Be(RagStrategy.HyDE);
    }

    [Fact]
    public void ResolveFallback_FusionRoutedAndEnabled_StaysFusion()
    {
        var options = new RagOptions();
        options.MultiQuery.Enabled = true;

        var result = RagStrategyResolver.ResolveFallback(RagStrategy.Fusion, options);

        result.Should().Be(RagStrategy.Fusion);
    }

    [Fact]
    public void ResolveFallback_Direct_NeverAffectedByFlags()
    {
        var options = new RagOptions();
        options.HyDE.Enabled = false;
        options.MultiQuery.Enabled = false;

        var result = RagStrategyResolver.ResolveFallback(RagStrategy.Direct, options);

        result.Should().Be(RagStrategy.Direct);
    }
}
