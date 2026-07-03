using AIExperience.Rag.Infrastructure.Options;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour les valeurs par défaut de <see cref="RagOptions"/> (constats R-5 et R-6).
/// Vérifie que le Reranker et la Compression contextuelle sont désactivés par défaut
/// afin d'éviter ~26 appels LLM séquentiels sur chaque question RAG.
/// </summary>
public sealed class RagOptionsDefaultTests
{
    private readonly RagOptions _sut = new();

    // ── R-5 : Reranker ────────────────────────────────────────────────────────

    [Fact]
    public void Reranker_DefaultEnabled_IsFalse()
    {
        // Le reranker LLM génère N appels séquentiels (1 par chunk) :
        // activer par défaut divise la performance par ~20 sur un modèle local 1B.
        _sut.Reranker.Enabled.Should().BeFalse(
            because: "le reranker LLM doit être désactivé par défaut pour éviter ~20 appels séquentiels");
    }

    [Fact]
    public void Reranker_DefaultTopKAfterRerank_IsReasonable()
    {
        // Valeur de repli si le reranker est activé explicitement
        _sut.Reranker.TopKAfterRerank.Should().BeGreaterThan(0)
            .And.BeLessOrEqualTo(20);
    }

    // ── R-6 : Compression contextuelle ────────────────────────────────────────

    [Fact]
    public void ContextCompression_DefaultEnabled_IsFalse()
    {
        // La compression génère 1 appel LLM par chunk survivant :
        // cumulée au reranker, une question RAG déclencherait ~26 appels LLM.
        _sut.ContextCompression.Enabled.Should().BeFalse(
            because: "la compression contextuelle doit être désactivée par défaut pour éviter des appels LLM supplémentaires");
    }

    // ── Autres options : invariants ───────────────────────────────────────────

    [Fact]
    public void Retrieval_DefaultTopK_IsPositive()
    {
        _sut.Retrieval.TopK.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Retrieval_DefaultScoreThreshold_IsInRange()
    {
        _sut.Retrieval.ScoreThreshold.Should().BeInRange(0.0, 1.0);
    }

    // ── R-17 : Cache ──────────────────────────────────────────────────────────

    [Fact]
    public void Cache_DefaultEnabled_IsFalse()
    {
        // Aucune implémentation de cache n'existe (R-8) : Enabled=true par défaut
        // ferait croire à tort qu'un cache est actif.
        _sut.Cache.Enabled.Should().BeFalse(
            because: "le cache n'est pas implémenté, la config ne doit pas prétendre le contraire");
    }
}
