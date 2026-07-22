using AIExperience.Rag.Application.Common;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using AIExperience.Web.Api.DTOs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AIExperience.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(
    IRagPipelineService ragPipelineService,
    IConversationRepository conversationRepository,
    IOptions<DevAuthOptions> devAuthOptions) : ControllerBase
{
    /// <summary>Retourne les prompts système par défaut — le front-end les charge au démarrage pour éviter toute duplication.</summary>
    [HttpGet("system-prompts")]
    public ActionResult<SystemPromptsResponse> GetSystemPrompts() =>
        Ok(new SystemPromptsResponse(RagPrompts.RagSystem, RagPrompts.DirectLlmSystem));

    /// <summary>Liste les conversations de l'utilisateur courant, triées par activité récente (pour la sidebar).</summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IEnumerable<ChatSessionSummaryResponse>>> GetSessions(CancellationToken cancellationToken)
    {
        var summaries = await conversationRepository.GetSessionSummariesAsync(
            devAuthOptions.Value.DefaultUserId, cancellationToken);

        return Ok(summaries.Select(s => new ChatSessionSummaryResponse(s.Id, s.Title, s.UpdatedAt, s.MessageCount)));
    }

    /// <summary>Recharge le détail d'une conversation (titre + messages + citations). 404 si absente ou d'un autre utilisateur.</summary>
    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<ChatSessionDetailResponse>> GetSession(Guid id, CancellationToken cancellationToken)
    {
        var session = await conversationRepository.GetSessionByIdAsync(id, cancellationToken);

        // Isolation par utilisateur : une session inexistante OU appartenant à un autre utilisateur renvoie 404.
        if (session is null || session.UserId != devAuthOptions.Value.DefaultUserId)
            return NotFound();

        var messages = session.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageResponse(
                m.Role.ToString(),
                m.Content,
                // Citations rechargées depuis la base : document/page/extrait/score présents ;
                // section et horodatages vidéo absents (propriétés [NotMapped], limitation assumée du lot).
                m.Citations.Count == 0
                    ? null
                    : m.Citations
                        .Select(c => new CitationResponse(
                            c.DocumentName, c.PageNumber, c.Excerpt, c.Score,
                            c.SectionTitle, c.ChunkIndex,
                            c.StartTime?.TotalSeconds, c.EndTime?.TotalSeconds))
                        .ToList(),
                m.StrategyUsed?.ToString(),
                m.TokensUsed,
                m.DurationMs,
                m.CreatedAt))
            .ToList();

        return Ok(new ChatSessionDetailResponse(session.Id, session.Title, messages));
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskQuestionResponse>> Ask(
        [FromBody] AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("La question ne peut pas être vide.");

        var ragResponse = await ragPipelineService.AskAsync(new RagQuery
        {
            Question = request.Question,
            DocumentIds = request.DocumentIds,
            Strategy = request.Strategy,
            UseLlm = request.UseLlm,
            UseRag = request.UseRag,
            SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt,
            SessionId = request.SessionId ?? Guid.Empty,
            UserId = devAuthOptions.Value.DefaultUserId
        }, cancellationToken);

        var citations = ragResponse.Citations
            .Select(c => new CitationResponse(c.DocumentName, c.PageNumber, c.Excerpt, c.Score, c.SectionTitle, c.ChunkIndex,
                c.StartTime?.TotalSeconds, c.EndTime?.TotalSeconds))
            .ToList();

        return Ok(new AskQuestionResponse(
            ragResponse.Answer,
            citations,
            ragResponse.StrategyUsed.ToString(),
            ragResponse.TotalTokens,
            ragResponse.DurationMs,
            ragResponse.SessionId));
    }

    [HttpPost("stream")]
    public async Task AskStream(
        [FromBody] AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("La question ne peut pas être vide.", cancellationToken);
            return;
        }

        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in ragPipelineService.AskStreamAsync(new RagQuery
            {
                Question = request.Question,
                DocumentIds = request.DocumentIds,
                Strategy = request.Strategy,
                UseLlm = request.UseLlm,
                UseRag = request.UseRag,
                SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt,
                SessionId = request.SessionId ?? Guid.Empty,
                UserId = devAuthOptions.Value.DefaultUserId
            }, cancellationToken))
            {
                if (chunk.IsDone && chunk.FinalResponse is { } final)
                {
                    var dto = BuildResponse(final);
                    await WriteSseAsync("done", JsonSerializer.Serialize(dto, JsonSerializerOptions.Web), cancellationToken);
                }
                else if (chunk.Token is { } token)
                {
                    await WriteSseAsync("token", JsonSerializer.Serialize(new { token }, JsonSerializerOptions.Web), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private AskQuestionResponse BuildResponse(RagResponse r)
    {
        var citations = r.Citations
            .Select(c => new CitationResponse(c.DocumentName, c.PageNumber, c.Excerpt, c.Score, c.SectionTitle, c.ChunkIndex,
                c.StartTime?.TotalSeconds, c.EndTime?.TotalSeconds))
            .ToList();
        return new AskQuestionResponse(r.Answer, citations, r.StrategyUsed.ToString(), r.TotalTokens, r.DurationMs, r.SessionId);
    }

    private async Task WriteSseAsync(string eventName, string data, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
