using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using Microsoft.Extensions.AI;

namespace AIExperience.Rag.Infrastructure.AI.Rag
{

    /// <summary>
    /// Routeur adaptatif qui délègue au LLM le choix de la stratégie RAG
    /// Selon la complexité de la question, le contexte disponible et les capacités du modèle, 
    /// il peut choisir entre : 
    /// </summary>
    public sealed class AdaptiveQueryRouter(IChatClient chatClient) : IAdaptiveQueryRouter
    {
        private const string SystemPrompt = """
        Tu es un classificateur de requêtes pour un système RAG.
        Analyse la question de l'utilisateur et retourne UNIQUEMENT un des mots suivants (sans explication) :
        - "Direct"   → question courte, factuelle, précise
        - "HyDE"     → question ouverte, complexe, nécessite une inférence ou une explication
        - "Fusion"   → question comparative, multi-aspects ou nécessitant une synthèse

        Réponds avec un seul mot parmi : Direct, HyDE, Fusion
        """;

        ///inehiteddoc
        public async Task<RagStrategy> GetRagStrategyAsync(string question, CancellationToken ct = default)
        {
            var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, question)
        };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var raw = response.Text.Trim();

            return raw switch
            {
                "HyDE" => RagStrategy.HyDE,
                "Fusion" => RagStrategy.Fusion,
                _ => RagStrategy.Direct
            };

        }
    }
}
