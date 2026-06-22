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
    bool UseLlm = true,
    /// <summary>
    /// Quand <c>false</c> et <c>UseLlm = true</c>, la question est envoyée directement au LLM sans récupération documentaire.
    /// Permet de comparer la réponse du LLM seul versus RAG+LLM lors d'une démonstration.
    /// </summary>
    bool UseRag = true,
    /// <summary>
    /// Prompt système personnalisé transmis par le client.
    /// Si <c>null</c> ou vide, le pipeline utilise le prompt par défaut défini dans <c>RagPrompts</c>.
    /// </summary>
    string? SystemPrompt = null);

/// <summary>Prompts système par défaut exposés au front-end pour éviter toute duplication.</summary>
public record SystemPromptsResponse(string Rag, string DirectLlm);

public record CitationResponse(
    string DocumentName,
    int? PageNumber,
    string Excerpt,
    double Score,
    /// <summary>Titre de la section du document source (null si non disponible).</summary>
    string? SectionTitle,
    /// <summary>Position ordinale du chunk dans le document (0-based).</summary>
    int ChunkIndex,
    /// <summary>Début du chunk dans la vidéo source en secondes (null pour les documents non-vidéo).</summary>
    double? StartTimeSeconds,
    /// <summary>Fin du chunk dans la vidéo source en secondes (null pour les documents non-vidéo).</summary>
    double? EndTimeSeconds);

public record AskQuestionResponse(
    string Answer,
    List<CitationResponse> Citations,
    string StrategyUsed,
    int TotalTokens,
    long DurationMs);
