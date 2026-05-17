using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace AIExperience.Rag.Infrastructure.AI.Rag
{
    public sealed class RagPipelineService(
        IVectorStoreService vectorStoreService,
        IEmbeddingService embeddingService,
        IAdaptiveQueryRouter adaptiveQueryRouter,
        IContextCompressorService contextCompressorService,
        IConversationRepository conversationRepository,
        IOptions<RagOptions> options,
        IChatClient chatClient) : IRagPipelineService
    {
        public async Task<RagResponse> AskAsync(RagQuery query, CancellationToken ct = default)
        {
            var ragOptions = options.Value;

            // 1. Résolution de la stratégie
            var strategy = query.Strategy == RagStrategy.Adaptive
                ? await adaptiveQueryRouter.GetRagStrategyAsync(query.Question, ct)
                : query.Strategy;

            // 2. Récupération des chunks selon la stratégie
            var rankedChunks = await RetrieveChunksAsync(query, strategy, ragOptions, ct);

            // 3. Compression du contexte. Permet de réduire le nombre de tokens envoyés au LLM tout en conservant l'essentiel de l'information.
            IReadOnlyList<DocumentChunk> contextChunks = null;
            if (ragOptions.ContextCompression.Enabled && rankedChunks.Count > 0)
            {
                contextChunks = await contextCompressorService.CompressAsync(query.Question, rankedChunks.Select(r => r.Chunk), ct);
            }
            
            if(contextChunks?.Count == 0 || !ragOptions.ContextCompression.Enabled && rankedChunks.Count == 0)
            {
                contextChunks = rankedChunks.Select(r => r.Chunk).ToList();
            }
               

            // 4. Construction du prompt et appel au LLM
            var chatHistory = await BuildChatHistoryAsync(query, contextChunks, ct);
            var completionResult = await chatClient.GetResponseAsync(
                chatHistory.Select(m => new Microsoft.Extensions.AI.ChatMessage(m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)).ToList(),
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.1f },
                ct);

            // 5. Construction des citations
            var citations = rankedChunks.Select(r => Citation.Create(
                Guid.Empty, r.Chunk.DocumentId,
                $"Document {r.Chunk.DocumentId}",
                r.Chunk.Content[..Math.Min(200, r.Chunk.Content.Length)],
                r.Score, r.Chunk.PageNumber)).ToList();

            return new RagResponse
            {
                Answer = string.Join("\n", completionResult.Messages.Select(c => c.Text)) ?? string.Empty,
                Citations = citations,
                StrategyUsed = strategy,
                TotalTokens = (int)(completionResult.Usage?.TotalTokenCount ?? 0)
            };
        }

        private async Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> RetrieveChunksAsync(
        RagQuery query, RagStrategy strategy, RagOptions opts, CancellationToken ct)
        {
            //TODO: mise en place de hyde et de fusion

            var docIds = query.DocumentIds.Count > 0 ? query.DocumentIds.ToArray() : null;

            // Direct
            var directVector = await embeddingService.EmbedAsync(query.Question, ct);
            var directChunks = await vectorStoreService.SearchAsync(directVector, opts.Retrieval.TopK, docIds, opts.Retrieval.ScoreThreshold, ct);
            // TODO: mise en place d'un service de reclassement des chunks. 
            // Evaluation de la pertinence réelle de chaque chunk par  rapport à la question indépendamment du cosinus
            return directChunks;
        }

        private async Task<ChatHistory> BuildChatHistoryAsync(
        RagQuery query,
        IReadOnlyList<DocumentChunk> contextChunks,
        CancellationToken ct)
        {
            var chatHistory = new ChatHistory(RagPrompts.RagSystem);

            // Injection de l'historique de conversation
            if (query.IncludeHistory && query.SessionId != Guid.Empty)
            {
                var pastMessages = await conversationRepository.GetMessagesAsync(
                    query.SessionId, query.MaxHistoryTurns, ct);

                foreach (var msg in pastMessages)
                {
                    if (msg.Role == MessageRole.User)
                        chatHistory.AddUserMessage(msg.Content);
                    else if (msg.Role == MessageRole.Assistant)
                        chatHistory.AddAssistantMessage(msg.Content);
                }
            }

            // Construction du contexte documentaire
            var contextBuilder = new StringBuilder();
            foreach (var (chunk, i) in contextChunks.Select((c, i) => (c, i + 1)))
            {
                contextBuilder.AppendLine($"[Extrait {i}] Document: {chunk.DocumentId}, Page: {chunk.PageNumber}");
                contextBuilder.AppendLine(chunk.Content);
                contextBuilder.AppendLine();
            }

            var userPrompt = RagPrompts.RagUser
                .Replace("{context}", contextBuilder.ToString())
                .Replace("{question}", query.Question);

            chatHistory.AddUserMessage(userPrompt);
            return chatHistory;

        }
    }
}
