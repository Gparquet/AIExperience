using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;
using Microsoft.Extensions.AI;

namespace AIExperience.Rag.Infrastructure.AI.Rag
{
    public sealed class ContextCompressorService(IChatClient chatClient) : IContextCompressorService
    {
        public async Task<IReadOnlyList<DocumentChunk>> CompressAsync(string question, IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
        {
            var compressedChunks = new List<DocumentChunk>();

            foreach (var chunk in chunks)
            {
                var prompt = RagPrompts.Compression
                    .Replace("{question}", question)
                    .Replace("{chunk}", chunk.Content[..Math.Min(1000, chunk.Content.Length)]);

                var result = await chatClient.GetResponseAsync(prompt, new ChatOptions { MaxOutputTokens = 500, Temperature = 0 },
                    cancellationToken: ct);

                var compressed = result.Text.Trim();

                if (!string.Equals(compressed, "AUCUN", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(compressed))
                {
                    var compressedChunk = DocumentChunk.Create(
                        chunk.DocumentId, compressed, chunk.ChunkIndex,
                        chunk.EmbeddingDimensions, chunk.PageNumber, chunk.SectionTitle);

                    compressedChunks.Add(compressedChunk);
                }
            }

            return compressedChunks;
        }
    }
}
