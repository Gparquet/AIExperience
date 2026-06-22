using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Web.Api.DTOs;

public record AskQuestionRequest(
    string Question,
    List<Guid> DocumentIds,
    RagStrategy Strategy = RagStrategy.Adaptive,
    /// <summary>
    /// Quand <c>false</c>, le pipeline utilise la recherche full-text PostgreSQL sans LLM.
    /// Permet de comparer l'approche classique versus RAG lors d'une démonstration.
    /// </summary>
    bool UseLlm = true,
    /// <summary>
    /// Quand <c>false</c> et <c>UseLlm = true</c>, la question est envoyée directement au LLM sans récupération documentaire.
    /// Permet de comparer la réponse du LLM seul versus RAG+LLM lors d'une démonstration.
    /// </summary>
    bool UseRag = true);

public record CitationResponse(
    string DocumentName,
    int? PageNumber,
    string Excerpt,
    double Score);

public record AskQuestionResponse(
    string Answer,
    List<CitationResponse> Citations,
    string StrategyUsed,
    int TotalTokens,
    long DurationMs);
