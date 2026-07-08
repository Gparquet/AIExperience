using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using AIExperience.Web.Api.DTOs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIExperience.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(IRagPipelineService ragPipelineService) : ControllerBase
{
    /// <summary>Retourne les prompts système par défaut — le front-end les charge au démarrage pour éviter toute duplication.</summary>
    [HttpGet("system-prompts")]
    public ActionResult<SystemPromptsResponse> GetSystemPrompts() =>
        Ok(new SystemPromptsResponse(RagPrompts.RagSystem, RagPrompts.DirectLlmSystem));

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
            SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt
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
            ragResponse.DurationMs));
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
                SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt
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
        return new AskQuestionResponse(r.Answer, citations, r.StrategyUsed.ToString(), r.TotalTokens, r.DurationMs);
    }

    private async Task WriteSseAsync(string eventName, string data, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
