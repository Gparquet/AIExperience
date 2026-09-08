using System.Text;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
// Alias : ChatMessage est ambigu dans ce fichier entre l'entité Domain et le type LLM
// de Microsoft.Extensions.AI utilisé par IChatClient (même convention que RagPipelineService).
using ChatMessage = AIExperience.Rag.Domain.Entities.ChatMessage;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Implémentation de <see cref="IQueryCondensationService"/> : un appel LLM court, isolé du reste
/// du pipeline. Si l'appel échoue ou renvoie du bruit inexploitable, on replie sur la question
/// brute plutôt que de faire échouer toute la récupération — même logique de dégradation
/// gracieuse que <see cref="LlmRerankerService"/>.
/// </summary>
public sealed class QueryCondensationService(
    IChatClient chatClient,
    ILogger<QueryCondensationService> logger) : IQueryCondensationService
{
    /// <inheritdoc/>
    public async Task<string> CondenseAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        // Garde-fou : pas d'historique = rien à condenser. Appeler quand même le LLM ici gaspillerait
        // un aller-retour, et un petit modèle peut dégrader une question déjà autonome (premier tour).
        if (history.Count == 0)
            return question;

        var prompt = RagPrompts.Condensation
            .Replace("{history}", BuildHistoryBlock(history))
            .Replace("{question}", question);

        try
        {
            var response = await chatClient.GetResponseAsync(
                prompt,
                new ChatOptions { MaxOutputTokens = 200, Temperature = 0 },
                ct);

            return CondensationResponseCleaner.Clean(response.Text) ?? question;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Condensation de question : échec de l'appel LLM. Repli sur la question brute.");
            return question;
        }
    }

    private static string BuildHistoryBlock(IReadOnlyList<ChatMessage> history)
    {
        var builder = new StringBuilder();
        foreach (var msg in history)
        {
            var speaker = msg.Role == MessageRole.User ? "Utilisateur" : "Assistant";
            builder.AppendLine($"{speaker} : {msg.Content}");
        }
        return builder.ToString();
    }
}
