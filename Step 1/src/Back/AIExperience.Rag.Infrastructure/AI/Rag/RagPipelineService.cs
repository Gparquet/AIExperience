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
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
            var sw = Stopwatch.StartNew();
            var ragOptions = options.Value;

            // Mode sans LLM : recherche full-text PostgreSQL, retourne les chunks bruts sans synthèse.
            if (!query.UseLlm)
                return await AskFullTextAsync(query, ragOptions, sw, ct);

            // Mode LLM direct sans RAG : aucune récupération documentaire, appel direct au LLM.
            if (!query.UseRag)
                return await AskDirectLlmAsync(query, sw, ct);

            // 1. Résolution de la stratégie
            var strategy = query.Strategy == RagStrategy.Adaptive
                ? await adaptiveQueryRouter.GetRagStrategyAsync(query.Question, ct)
                : query.Strategy;

            // 2. Récupération des chunks selon la stratégie
            var rankedChunks = await RetrieveChunksAsync(query, strategy, ragOptions, ct);

            // 3. Compression du contexte — réduit les tokens envoyés au LLM en conservant les phrases pertinentes.
            //    Fallback sur les chunks bruts si la compression est désactivée ou retourne vide.
            IReadOnlyList<DocumentChunk> contextChunks;
            if (ragOptions.ContextCompression.Enabled && rankedChunks.Count > 0)
            {
                var compressed = await contextCompressorService.CompressAsync(query.Question, rankedChunks.Select(r => r.Chunk), ct);
                contextChunks = compressed.Count > 0 ? compressed : rankedChunks.Select(r => r.Chunk).ToList();
            }
            else
            {
                contextChunks = rankedChunks.Select(r => r.Chunk).ToList();
            }

            // 4. Construction du prompt et appel au LLM
            var chatHistory = await BuildChatHistoryAsync(query, contextChunks, ct);
            var completionResult = await chatClient.GetResponseAsync(
                chatHistory.Select(m => new Microsoft.Extensions.AI.ChatMessage(m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)).ToList(),
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.1f },
                ct);

            sw.Stop();

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
                TotalTokens = (int)(completionResult.Usage?.TotalTokenCount ?? 0),
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        public async IAsyncEnumerable<RagStreamChunk> AskStreamAsync(
            RagQuery query,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var ragOptions = options.Value;

            // Mode sans LLM : pas de streaming — retourne un unique événement "done" avec les chunks bruts.
            if (!query.UseLlm)
            {
                var response = await AskFullTextAsync(query, ragOptions, sw, ct);
                yield return new RagStreamChunk { IsDone = true, FinalResponse = response };
                yield break;
            }

            // Mode LLM direct sans RAG : streaming direct sans récupération documentaire.
            if (!query.UseRag)
            {
                await foreach (var chunk in StreamDirectLlmAsync(query, sw, ct))
                    yield return chunk;
                yield break;
            }

            // 1. Résolution de la stratégie
            var strategy = query.Strategy == RagStrategy.Adaptive
                ? await adaptiveQueryRouter.GetRagStrategyAsync(query.Question, ct)
                : query.Strategy;

            // 2. Récupération des chunks
            var rankedChunks = await RetrieveChunksAsync(query, strategy, ragOptions, ct);

            // 3. Compression du contexte
            IReadOnlyList<DocumentChunk> contextChunks;
            if (ragOptions.ContextCompression.Enabled && rankedChunks.Count > 0)
            {
                var compressed = await contextCompressorService.CompressAsync(query.Question, rankedChunks.Select(r => r.Chunk), ct);
                contextChunks = compressed.Count > 0 ? compressed : rankedChunks.Select(r => r.Chunk).ToList();
            }
            else
            {
                contextChunks = rankedChunks.Select(r => r.Chunk).ToList();
            }

            // 4. Construction du prompt
            var chatHistory = await BuildChatHistoryAsync(query, contextChunks, ct);
            var messages = chatHistory
                .Select(m => new Microsoft.Extensions.AI.ChatMessage(
                    m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content))
                .ToList();

            // 5. Streaming LLM
            var totalText = new StringBuilder();
            await foreach (var update in chatClient.GetStreamingResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.1f },
                ct))
            {
                var token = update.Text;
                if (!string.IsNullOrEmpty(token))
                {
                    totalText.Append(token);
                    yield return new RagStreamChunk { Token = token };
                }
            }

            sw.Stop();

            // 6. Citations + réponse finale
            var citations = rankedChunks.Select(r => Citation.Create(
                Guid.Empty, r.Chunk.DocumentId,
                $"Document {r.Chunk.DocumentId}",
                r.Chunk.Content[..Math.Min(200, r.Chunk.Content.Length)],
                r.Score, r.Chunk.PageNumber)).ToList();

            yield return new RagStreamChunk
            {
                IsDone = true,
                FinalResponse = new RagResponse
                {
                    Answer = totalText.ToString(),
                    Citations = citations,
                    StrategyUsed = strategy,
                    TotalTokens = 0,
                    DurationMs = sw.ElapsedMilliseconds
                }
            };
        }

        /// <summary>
        /// Recherche full-text (sans LLM) via <c>plainto_tsquery</c> PostgreSQL.
        /// Retourne les chunks bruts avec leur score textuel, sans synthèse ni embedding.
        /// </summary>
        private async Task<RagResponse> AskFullTextAsync(
            RagQuery query, RagOptions opts, Stopwatch sw, CancellationToken ct)
        {
            var docIds = query.DocumentIds.Count > 0 ? query.DocumentIds.ToArray() : null;
            var chunks = await vectorStoreService.SearchFullTextAsync(query.Question, opts.Retrieval.TopK, docIds, ct);

            // Les citations portent les données structurées — l'answer est un résumé sobre.
            var answer = chunks.Count == 0
                ? "Aucun résultat trouvé pour cette recherche."
                : $"{chunks.Count} résultat(s) trouvé(s) pour « {query.Question} ».";

            var citations = chunks.Select(r => Citation.Create(
                Guid.Empty, r.Chunk.DocumentId,
                $"Document {r.Chunk.DocumentId}",
                r.Chunk.Content[..Math.Min(300, r.Chunk.Content.Length)],
                r.Score, r.Chunk.PageNumber)).ToList();

            sw.Stop();
            return new RagResponse
            {
                Answer = answer,
                Citations = citations,
                StrategyUsed = RagStrategy.FullText,
                TotalTokens = 0,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        /// <summary>
        /// Appelle le LLM directement sans aucune récupération documentaire.
        /// Utilisé pour la démonstration "LLM sans RAG" — le modèle répond depuis ses connaissances générales.
        /// </summary>
        private async Task<RagResponse> AskDirectLlmAsync(RagQuery query, Stopwatch sw, CancellationToken ct)
        {
            var chatHistory = new ChatHistory(RagPrompts.DirectLlmSystem);

            if (query.IncludeHistory && query.SessionId != Guid.Empty)
            {
                var pastMessages = await conversationRepository.GetMessagesAsync(query.SessionId, query.MaxHistoryTurns, ct);
                foreach (var msg in pastMessages)
                {
                    if (msg.Role == MessageRole.User) chatHistory.AddUserMessage(msg.Content);
                    else if (msg.Role == MessageRole.Assistant) chatHistory.AddAssistantMessage(msg.Content);
                }
            }

            chatHistory.AddUserMessage(RagPrompts.DirectLlmUser.Replace("{question}", query.Question));

            var messages = chatHistory.Select(m => new Microsoft.Extensions.AI.ChatMessage(
                m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)).ToList();

            var completionResult = await chatClient.GetResponseAsync(messages,
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.7f }, ct);

            sw.Stop();
            return new RagResponse
            {
                Answer = string.Join("\n", completionResult.Messages.Select(c => c.Text)) ?? string.Empty,
                Citations = [],
                StrategyUsed = RagStrategy.DirectLlm,
                TotalTokens = (int)(completionResult.Usage?.TotalTokenCount ?? 0),
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        /// <summary>
        /// Streaming LLM direct sans RAG — même logique que <see cref="AskDirectLlmAsync"/> mais en mode streaming.
        /// </summary>
        private async IAsyncEnumerable<RagStreamChunk> StreamDirectLlmAsync(
            RagQuery query, Stopwatch sw, [EnumeratorCancellation] CancellationToken ct)
        {
            var chatHistory = new ChatHistory(RagPrompts.DirectLlmSystem);

            if (query.IncludeHistory && query.SessionId != Guid.Empty)
            {
                var pastMessages = await conversationRepository.GetMessagesAsync(query.SessionId, query.MaxHistoryTurns, ct);
                foreach (var msg in pastMessages)
                {
                    if (msg.Role == MessageRole.User) chatHistory.AddUserMessage(msg.Content);
                    else if (msg.Role == MessageRole.Assistant) chatHistory.AddAssistantMessage(msg.Content);
                }
            }

            chatHistory.AddUserMessage(RagPrompts.DirectLlmUser.Replace("{question}", query.Question));

            var messages = chatHistory.Select(m => new Microsoft.Extensions.AI.ChatMessage(
                m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)).ToList();

            var totalText = new StringBuilder();
            await foreach (var update in chatClient.GetStreamingResponseAsync(
                messages, new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.7f }, ct))
            {
                var token = update.Text;
                if (!string.IsNullOrEmpty(token))
                {
                    totalText.Append(token);
                    yield return new RagStreamChunk { Token = token };
                }
            }

            sw.Stop();
            yield return new RagStreamChunk
            {
                IsDone = true,
                FinalResponse = new RagResponse
                {
                    Answer = totalText.ToString(),
                    Citations = [],
                    StrategyUsed = RagStrategy.DirectLlm,
                    TotalTokens = 0,
                    DurationMs = sw.ElapsedMilliseconds
                }
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
