using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Web.Api.DTOs;

public record AskQuestionRequest(
    string Question,
    List<Guid> DocumentIds,
    RagStrategy Strategy = RagStrategy.Adaptive);

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
