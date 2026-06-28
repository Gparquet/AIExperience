using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="RagPrompts"/> (constat R-3 du plan Lot 0).
/// Vérifie que les prompts système sont génériques et neutres —
/// les anciens prompts "finance" / "voyage" biaisaient les réponses sans raison.
/// </summary>
public sealed class RagPromptsTests
{
    // ── R-3 : Prompts système neutres ─────────────────────────────────────────

    [Fact]
    public void RagSystem_DoesNotContain_Finance()
    {
        // L'ancien prompt "Tu es un assistant expert dans la finance" biaisait
        // toutes les réponses même sur des corpus non financiers.
        RagPrompts.RagSystem.Should().NotContainEquivalentOf("finance",
            because: "le prompt RAG doit être générique, non spécialisé dans un domaine");
    }

    [Fact]
    public void DirectLlmSystem_DoesNotContain_Voyage()
    {
        // L'ancien prompt "Tu es un assistant expert dans le voyage" était incohérent
        // et contredisait l'usage d'un RAG générique documentaire.
        RagPrompts.DirectLlmSystem.Should().NotContainEquivalentOf("voyage",
            because: "le prompt LLM direct doit être générique, non spécialisé dans le voyage");
    }

    [Fact]
    public void RagSystem_ContainsSourceCitationInstruction()
    {
        // Le prompt doit indiquer au LLM comment citer les sources
        // pour que les citations textuelles correspondent aux chunks récupérés.
        RagPrompts.RagSystem.Should().ContainEquivalentOf("SOURCE",
            because: "le prompt doit instruire le LLM sur le format de citation [SOURCE: ...]");
    }

    [Fact]
    public void RagSystem_ContainsContextConstraint()
    {
        // Le LLM doit répondre uniquement depuis le contexte documentaire fourni
        RagPrompts.RagSystem.Should().ContainEquivalentOf("context",
            because: "le prompt doit contraindre le LLM à répondre depuis le contexte");
    }

    [Fact]
    public void RagSystem_IsNotEmpty()
    {
        RagPrompts.RagSystem.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DirectLlmSystem_IsNotEmpty()
    {
        RagPrompts.DirectLlmSystem.Should().NotBeNullOrWhiteSpace();
    }

    // ── Intégrité des templates ───────────────────────────────────────────────

    [Fact]
    public void RagUser_ContainsContextPlaceholder()
    {
        RagPrompts.RagUser.Should().Contain("{context}",
            because: "le template utilisateur doit exposer le placeholder {context}");
    }

    [Fact]
    public void RagUser_ContainsQuestionPlaceholder()
    {
        RagPrompts.RagUser.Should().Contain("{question}",
            because: "le template utilisateur doit exposer le placeholder {question}");
    }
}
