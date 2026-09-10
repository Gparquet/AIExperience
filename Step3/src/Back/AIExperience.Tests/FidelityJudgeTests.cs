using AIExperience.Eval.Judge;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="FidelityJudge"/> avec un faux IChatClient (même pattern que
/// QueryCondensationServiceTests) — pas de mock, juste une implémentation minimale.
/// </summary>
public sealed class FidelityJudgeTests
{
    private sealed class FakeChatClient(Func<string>? respond = null, bool throwOnCall = false) : IChatClient
    {
        public string? LastUserPrompt { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (throwOnCall)
                throw new InvalidOperationException("Échec LLM simulé.");
            LastUserPrompt = messages.Last().Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond!())));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Non utilisé par FidelityJudge.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task ScoreAsync_ReponseNominale_RetourneScoreEtJustificationParses()
    {
        var judge = new FidelityJudge(new FakeChatClient(() => "SCORE: 1\nJUSTIFICATION: complet et correct."));

        var result = await judge.ScoreAsync("Question ?", "Réponse générée.", ["point clé 1"]);

        result.Score.Should().Be(1.0);
        result.Rationale.Should().Be("complet et correct.");
    }

    [Fact]
    public async Task ScoreAsync_PromptInclutQuestionReponseEtPointsCles()
    {
        var fake = new FakeChatClient(() => "SCORE: 1\nJUSTIFICATION: ok");
        var judge = new FidelityJudge(fake);

        await judge.ScoreAsync("Ma question ?", "Ma réponse.", ["point A", "point B"]);

        fake.LastUserPrompt.Should().Contain("Ma question ?");
        fake.LastUserPrompt.Should().Contain("Ma réponse.");
        fake.LastUserPrompt.Should().Contain("point A");
        fake.LastUserPrompt.Should().Contain("point B");
    }

    [Fact]
    public async Task ScoreAsync_QuandLAppelLlmEchoue_ReplieSurScoreNullAvecMessageDErreur()
    {
        var judge = new FidelityJudge(new FakeChatClient(throwOnCall: true));

        var result = await judge.ScoreAsync("Question ?", "Réponse.", ["point"]);

        result.Score.Should().BeNull();
        result.Rationale.Should().Contain("Erreur lors de l'appel au juge");
    }
}
