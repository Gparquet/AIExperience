using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Domain.Models;
using AIExperience.Web.Api.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace AIExperience.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(IRagPipelineService ragPipelineService) : ControllerBase
{
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
            Strategy = request.Strategy
        }, cancellationToken);

        var citations = ragResponse.Citations
            .Select(c => new CitationResponse(c.DocumentName, c.PageNumber, c.Excerpt, c.Score))
            .ToList();

        return Ok(new AskQuestionResponse(
            ragResponse.Answer,
            citations,
            ragResponse.StrategyUsed.ToString(),
            ragResponse.TotalTokens,
            ragResponse.DurationMs));
    }
}
