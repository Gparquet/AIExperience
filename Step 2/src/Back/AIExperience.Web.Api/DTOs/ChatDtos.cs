using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Web.Api.DTOs;

public record AskQuestionRequest(
    string Question,
    List<Guid> DocumentIds,
    RagStrategy Strategy = RagStrategy.HyDE,
    /// <summary>
    /// Quand <c>false</c>, le pipeline utilise la recherche full-text PostgreSQL sans LLM.
    /// Permet de comparer l'approche classique versus RAG lors d'une démonstration.
    /// </summary>
    bool UseLlm = true);

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
