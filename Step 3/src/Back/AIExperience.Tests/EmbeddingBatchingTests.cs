using AIExperience.Rag.Infrastructure.AI.Embedding;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Resilience;
using Polly;
using Polly.Retry;

namespace AIExperience.Tests;

/// <summary>
/// Tests unitaires pour <see cref="OpenAIEmbeddingService"/>.
/// Vérifie :
///   — le sous-batching : 96 textes par appel au lieu d'un seul appel massif.
///   — la résilience : le service retente automatiquement sur erreur transitoire (Polly retry).
/// </summary>
public sealed class EmbeddingBatchingTests
{
    #region Sous-batching des appels

    [Fact]
    public async Task EmbedBatchAsync_200Texts_MakesThreeCalls()
    {
        // 200 textes / 96 par batch = ⌈200/96⌉ = 3 appels au générateur
        var fakeGenerator = new CallCountingGenerator(dimension: 4);
        var sut = new OpenAIEmbeddingService(fakeGenerator);
        var texts = Enumerable.Range(0, 200).Select(i => $"Texte {i}");

        await sut.EmbedBatchAsync(texts);

        fakeGenerator.CallCount.Should().Be(3,
            "200 textes divisés en batches de 96 requièrent 3 appels (96+96+8)");
    }

    [Fact]
    public async Task EmbedBatchAsync_ExactBatchSize_MakesOneCall()
    {
        // 96 textes = exactement un batch → 1 seul appel
        var fakeGenerator = new CallCountingGenerator(dimension: 4);
        var sut = new OpenAIEmbeddingService(fakeGenerator);
        var texts = Enumerable.Range(0, 96).Select(i => $"Texte {i}");

        await sut.EmbedBatchAsync(texts);

        fakeGenerator.CallCount.Should().Be(1, "96 textes tiennent dans un seul batch");
    }

    [Fact]
    public async Task EmbedBatchAsync_OneBeyondBatchSize_MakesTwoCalls()
    {
        // 97 textes = 96 + 1 → 2 appels
        var fakeGenerator = new CallCountingGenerator(dimension: 4);
        var sut = new OpenAIEmbeddingService(fakeGenerator);
        var texts = Enumerable.Range(0, 97).Select(i => $"Texte {i}");

        await sut.EmbedBatchAsync(texts);

        fakeGenerator.CallCount.Should().Be(2, "97 textes = 1 batch de 96 + 1 batch de 1");
    }

    [Fact]
    public async Task EmbedBatchAsync_SingleText_MakesOneCall()
    {
        // Cas minimal : 1 seul texte → 1 appel
        var fakeGenerator = new CallCountingGenerator(dimension: 4);
        var sut = new OpenAIEmbeddingService(fakeGenerator);

        await sut.EmbedBatchAsync(["Texte unique"]);

        fakeGenerator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsCorrectCount()
    {
        // Les résultats doivent correspondre en nombre aux textes d'entrée
        var fakeGenerator = new CallCountingGenerator(dimension: 4);
        var sut = new OpenAIEmbeddingService(fakeGenerator);
        var texts = Enumerable.Range(0, 150).Select(i => $"Texte {i}").ToList();

        var results = await sut.EmbedBatchAsync(texts);

        results.Should().HaveCount(150, "chaque texte doit produire exactement un embedding");
    }

    [Fact]
    public async Task EmbedBatchAsync_EachResultHasCorrectDimension()
    {
        // Chaque vecteur retourné doit avoir la dimension annoncée par le générateur
        const int dimension = 768;
        var fakeGenerator = new CallCountingGenerator(dimension);
        var sut = new OpenAIEmbeddingService(fakeGenerator);
        var texts = Enumerable.Range(0, 10).Select(i => $"Texte {i}");

        var results = await sut.EmbedBatchAsync(texts);

        results.Should().AllSatisfy(r =>
            r.Length.Should().Be(dimension, "chaque embedding doit avoir la dimension correcte"));
    }

    #endregion

    #region Résilience Polly (retry)

    [Fact]
    public async Task EmbedBatchAsync_TwoTransientFailures_RetriesAndSucceeds()
    {
        // 2 échecs transitoires puis succès : le service doit retenter et finalement retourner un résultat.
        var failingGenerator = new TransientlyFailingGenerator(failTimes: 2, dimension: 4);

        // Pipeline Polly : 3 tentatives maximum, sans délai (rapide en test)
        var retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();

        var sut = new OpenAIEmbeddingService(failingGenerator, retryPipeline);

        var act = async () => await sut.EmbedBatchAsync(["Texte test"]);

        await act.Should().NotThrowAsync(
            "le retry doit absorber 2 échecs transitoires et réussir à la 3e tentative");
    }

    [Fact]
    public async Task EmbedBatchAsync_PersistentFailure_ThrowsAfterMaxRetries()
    {
        // Si l'erreur persiste au-delà du nombre max de retries → exception propagée
        var alwaysFailingGenerator = new TransientlyFailingGenerator(failTimes: 10, dimension: 4);

        var retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();

        var sut = new OpenAIEmbeddingService(alwaysFailingGenerator, retryPipeline);

        var act = async () => await sut.EmbedBatchAsync(["Texte test"]);

        await act.Should().ThrowAsync<HttpRequestException>(
            "après MaxRetryAttempts tentatives échouées, l'exception doit remonter");
    }

    #endregion

    #region Fakes

    /// <summary>
    /// Générateur factice qui compte le nombre d'appels reçus.
    /// Utilisé pour vérifier que le sous-batching produit le bon nombre d'appels.
    /// </summary>
    private sealed class CallCountingGenerator(int dimension)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _callCount;

        /// <summary>Nombre total d'appels à <see cref="GenerateAsync"/>.</summary>
        public int CallCount => _callCount;

        public EmbeddingGeneratorMetadata Metadata { get; } =
            new("fake-counter", null, null, dimension);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            var embeddings = values
                .Select(_ => new Embedding<float>(new float[dimension]))
                .ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>
    /// Générateur factice qui échoue les <paramref name="failTimes"/> premiers appels
    /// avec <see cref="HttpRequestException"/>, puis réussit.
    /// Simule les erreurs transitoires réseau (429, 503...).
    /// </summary>
    private sealed class TransientlyFailingGenerator(int failTimes, int dimension)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _callCount;

        public EmbeddingGeneratorMetadata Metadata { get; } =
            new("fake-failing", null, null, dimension);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _callCount);
            if (count <= failTimes)
                throw new HttpRequestException($"Erreur transitoire simulée (tentative {count}/{failTimes})");

            var embeddings = values
                .Select(_ => new Embedding<float>(new float[dimension]))
                .ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    #endregion
}
