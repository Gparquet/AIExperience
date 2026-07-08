using AIExperience.Rag.Infrastructure.AI.Rag;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="BatchedRerankResponseParser"/> (correctif R-18/R-5').
/// Le reranker batché remplace N appels LLM séquentiels par 1 seul appel : ce parseur pur
/// est la partie testable sans dépendance LLM (pas de mock disponible dans ce projet de tests).
/// </summary>
public sealed class BatchedRerankResponseParserTests
{
    [Fact]
    public void Parse_NominalCase_ReturnsAllScoresNormalized()
    {
        var response = "1:7\n2:0\n3:9";

        var result = BatchedRerankResponseParser.Parse(response);

        result.Should().HaveCount(3);
        result[1].Should().Be(0.7);
        result[2].Should().Be(0.0);
        result[3].Should().Be(0.9);
    }

    [Fact]
    public void Parse_ToleratesSurroundingText()
    {
        // Un petit modèle local ajoute parfois du texte parasite autour du chiffre attendu.
        var response = "Extrait 1: 7\nExtrait 2 : 3\nExtrait numero 3 - 10";

        var result = BatchedRerankResponseParser.Parse(response);

        result.Should().HaveCount(3);
        result[1].Should().Be(0.7);
        result[2].Should().Be(0.3);
        result[3].Should().Be(1.0);
    }

    [Fact]
    public void Parse_ClampsOutOfRangeScore_ToMaximum()
    {
        // "1:15" exprime une intention de score maximal malgré le dépassement de l'échelle 0-10.
        var response = "1:15";

        var result = BatchedRerankResponseParser.Parse(response);

        result[1].Should().Be(1.0);
    }

    [Fact]
    public void Parse_KeepsFirstOccurrence_OnDuplicateIndex()
    {
        var response = "1:7\n1:2";

        var result = BatchedRerankResponseParser.Parse(response);

        result.Should().HaveCount(1);
        result[1].Should().Be(0.7, because: "la première occurrence doit l'emporter pour un résultat déterministe");
    }

    [Fact]
    public void Parse_IgnoresLinesWithoutScorePattern()
    {
        var response = "Voici mon évaluation :\n1:8\nJe ne suis pas sûr pour la suite.";

        var result = BatchedRerankResponseParser.Parse(response);

        result.Should().HaveCount(1);
        result[1].Should().Be(0.8);
    }

    [Fact]
    public void Parse_EmptyResponse_ReturnsEmptyDictionary()
    {
        BatchedRerankResponseParser.Parse(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullResponse_ReturnsEmptyDictionary()
    {
        BatchedRerankResponseParser.Parse(null).Should().BeEmpty();
    }

    [Fact]
    public void Parse_WhitespaceOnlyResponse_ReturnsEmptyDictionary()
    {
        BatchedRerankResponseParser.Parse("   \n  \n ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_DashSeparator_IsAlsoAccepted()
    {
        var response = "1-7\n2-3";

        var result = BatchedRerankResponseParser.Parse(response);

        result.Should().HaveCount(2);
        result[1].Should().Be(0.7);
        result[2].Should().Be(0.3);
    }
}
