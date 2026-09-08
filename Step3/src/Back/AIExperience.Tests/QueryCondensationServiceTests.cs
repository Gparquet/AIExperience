using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Infrastructure.AI.Rag;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ChatMessage = AIExperience.Rag.Domain.Entities.ChatMessage;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="QueryCondensationService"/> : condensation nominale, et les deux
/// replis sur la question brute (échec de l'appel LLM, réponse inexploitable).
/// </summary>
public sealed class QueryCondensationServiceTests
{
    /// <summary>Faux IChatClient renvoyant une réponse canée ou levant une exception à la demande.</summary>
    private sealed class FakeChatClient(Func<string>? respond = null, bool throwOnCall = false) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (throwOnCall)
                throw new InvalidOperationException("Échec LLM simulé.");
            return Task.FromResult(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, respond!())));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Non utilisé par QueryCondensationService.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static List<ChatMessage> SampleHistory() =>
    [
        ChatMessage.CreateUserMessage(Guid.NewGuid(), "Quels sont les horaires du magasin ?"),
        ChatMessage.CreateAssistantMessage(Guid.NewGuid(), "Le magasin ouvre à 9h en semaine.", 10, RagStrategy.Direct, 100)
    ];

    [Fact]
    public async Task CondenseAsync_SansHistorique_RetourneLaQuestionBruteSansAppelerLeLlm()
    {
        var service = new QueryCondensationService(
            new FakeChatClient(throwOnCall: true), NullLogger<QueryCondensationService>.Instance);

        var result = await service.CondenseAsync("Bonjour", []);

        result.Should().Be("Bonjour");
    }

    [Fact]
    public async Task CondenseAsync_AvecHistorique_RetourneLaQuestionCondenseeNettoyee()
    {
        var service = new QueryCondensationService(
            new FakeChatClient(() => "Quels sont les horaires du magasin le week-end ?"),
            NullLogger<QueryCondensationService>.Instance);

        var result = await service.CondenseAsync("et le week-end ?", SampleHistory());

        result.Should().Be("Quels sont les horaires du magasin le week-end ?");
    }

    [Fact]
    public async Task CondenseAsync_QuandLAppelLlmEchoue_ReplieSurLaQuestionBrute()
    {
        var service = new QueryCondensationService(
            new FakeChatClient(throwOnCall: true), NullLogger<QueryCondensationService>.Instance);

        var result = await service.CondenseAsync("et le week-end ?", SampleHistory());

        result.Should().Be("et le week-end ?");
    }

    [Fact]
    public async Task CondenseAsync_QuandLaReponseEstInexploitable_ReplieSurLaQuestionBrute()
    {
        var service = new QueryCondensationService(
            new FakeChatClient(() => "   "), NullLogger<QueryCondensationService>.Instance);

        var result = await service.CondenseAsync("et le week-end ?", SampleHistory());

        result.Should().Be("et le week-end ?");
    }
}
