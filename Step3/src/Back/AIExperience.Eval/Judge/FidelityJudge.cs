using Microsoft.Extensions.AI;

namespace AIExperience.Eval.Judge;

/// <summary>
/// Juge de fidélité (LLM-as-judge) : compare une réponse générée aux points clés attendus du jeu
/// golden, avec le même IChatClient (donc potentiellement le même modèle faible) que le pipeline
/// RAG évalué. Un échec de l'appel est dégradé gracieusement (même logique que
/// QueryCondensationService) plutôt que de faire échouer tout le run pour une seule question.
/// </summary>
public sealed class FidelityJudge(IChatClient chatClient)
{
    public async Task<JudgeResult> ScoreAsync(
        string question, string generatedAnswer, IReadOnlyList<string> expectedKeyPoints, CancellationToken ct = default)
    {
        var keyPoints = string.Join("\n", expectedKeyPoints.Select(p => $"- {p}"));
        var userPrompt = EvalPrompts.JudgeUser
            .Replace("{question}", question)
            .Replace("{answer}", generatedAnswer)
            .Replace("{keyPoints}", keyPoints);

        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, EvalPrompts.JudgeSystem),
                    new ChatMessage(ChatRole.User, userPrompt)
                ],
                new ChatOptions { MaxOutputTokens = 300, Temperature = 0.0f },
                ct);

            var text = string.Join("\n", response.Messages.Select(m => m.Text));
            return JudgeResponseParser.Parse(text);
        }
        catch (Exception ex)
        {
            return new JudgeResult(null, $"Erreur lors de l'appel au juge : {ex.Message}");
        }
    }
}
