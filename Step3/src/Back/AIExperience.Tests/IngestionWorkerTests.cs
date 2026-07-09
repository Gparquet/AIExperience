using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Application.Video.Command;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="IngestionWorker"/> : vérifie que chaque message non traité déclenche
/// la bonne commande selon son type d'événement, qu'il est marqué traité ensuite, et qu'un échec
/// avant même d'atteindre la commande (payload corrompu, etc.) est retenté un nombre limité de
/// fois avant d'être abandonné.
/// </summary>
public sealed class IngestionWorkerTests
{
    private sealed class FakeOutboxRepository : IOutboxRepository
    {
        public List<OutboxMessage> Messages { get; } = [];

        public void Add(OutboxMessage message) => Messages.Add(message);

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int maxCount, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>(Messages.Where(m => m.ProcessedAt is null).Take(maxCount).ToList());

        public Task UpdateAsync(OutboxMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>Faux ISender qui enregistre les commandes reçues et peut être configuré pour échouer.</summary>
    private sealed class FakeSender : ISender
    {
        public List<object> SentRequests { get; } = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);
            return ExceptionToThrow is null
                ? Task.FromResult(default(TResponse)!)
                : Task.FromException<TResponse>(ExceptionToThrow);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        {
            SentRequests.Add(request!);
            return ExceptionToThrow is null ? Task.CompletedTask : Task.FromException(ExceptionToThrow);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Non utilisé par le worker d'ingestion.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Non utilisé par le worker d'ingestion.");
    }

    /// <summary>
    /// Construit un worker branché sur un vrai conteneur DI (scopes réels), avec des dépendances
    /// en mémoire — c'est la façon la plus simple de tester un BackgroundService sans dupliquer
    /// sa logique interne de scoping.
    /// </summary>
    private static (IngestionWorker Worker, FakeOutboxRepository Outbox, FakeSender Sender) CreateWorker(
        int maxRetryAttempts = 5)
    {
        var outbox = new FakeOutboxRepository();
        var sender = new FakeSender();

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxRepository>(outbox);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddSingleton<ISender>(sender);
        services.AddSingleton(Options.Create(new IngestionOptions
        {
            WorkDirectory = "unused",
            PollIntervalSeconds = 60, // assez grand pour qu'un seul passage ait lieu pendant le test
            BatchSize = 10,
            MaxRetryAttempts = maxRetryAttempts
        }));

        var provider = services.BuildServiceProvider();
        var signal = new IngestionSignal();
        var worker = new IngestionWorker(
            signal,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<IngestionOptions>>(),
            NullLogger<IngestionWorker>.Instance);

        return (worker, outbox, sender);
    }

    private static async Task RunOnePassAsync(IngestionWorker worker)
    {
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300); // laisse le temps au premier passage de sonde de s'exécuter
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_DocumentIngestionMessage_SendsIngestDocumentCommand()
    {
        var (worker, outbox, sender) = CreateWorker();
        var documentId = Guid.NewGuid();
        outbox.Messages.Add(OutboxMessage.Create(
            IngestionEventTypes.DocumentIngestionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = documentId })));

        await RunOnePassAsync(worker);

        sender.SentRequests.OfType<IngestDocumentCommand>().Should().ContainSingle(cmd => cmd.DocumentId == documentId);
    }

    [Fact]
    public async Task ExecuteAsync_VideoTranscriptionMessage_SendsProcessVideoTranscriptionJobCommand()
    {
        var (worker, outbox, sender) = CreateWorker();
        var documentId = Guid.NewGuid();
        outbox.Messages.Add(OutboxMessage.Create(
            IngestionEventTypes.VideoTranscriptionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = documentId })));

        await RunOnePassAsync(worker);

        sender.SentRequests.OfType<ProcessVideoTranscriptionJobCommand>().Should().ContainSingle(cmd => cmd.DocumentId == documentId);
    }

    [Fact]
    public async Task ExecuteAsync_MessageHandledSuccessfully_MarksItProcessed()
    {
        var (worker, outbox, _) = CreateWorker();
        var message = OutboxMessage.Create(
            IngestionEventTypes.DocumentIngestionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = Guid.NewGuid() }));
        outbox.Messages.Add(message);

        await RunOnePassAsync(worker);

        message.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_SendThrows_RecordsFailedAttemptWithoutMarkingProcessedBelowThreshold()
    {
        var (worker, outbox, sender) = CreateWorker(maxRetryAttempts: 5);
        sender.ExceptionToThrow = new InvalidOperationException("base de données momentanément indisponible");
        var message = OutboxMessage.Create(
            IngestionEventTypes.DocumentIngestionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = Guid.NewGuid() }));
        outbox.Messages.Add(message);

        await RunOnePassAsync(worker);

        message.RetryCount.Should().Be(1);
        message.ProcessedAt.Should().BeNull("il reste éligible à une nouvelle tentative au prochain tour de sonde");
    }

    [Fact]
    public async Task ExecuteAsync_SendThrowsRepeatedly_GivesUpAfterMaxRetryAttempts()
    {
        var (worker, outbox, sender) = CreateWorker(maxRetryAttempts: 2);
        sender.ExceptionToThrow = new InvalidOperationException("payload systématiquement invalide");
        var message = OutboxMessage.Create(
            IngestionEventTypes.DocumentIngestionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = Guid.NewGuid() }));
        message.MarkFailedAttempt("échec précédent 1"); // simule une tentative déjà comptabilisée
        outbox.Messages.Add(message);

        await RunOnePassAsync(worker);

        message.RetryCount.Should().Be(2);
        message.ProcessedAt.Should().NotBeNull("le seuil de tentatives est atteint, le message est abandonné");
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyProcessedMessage_IsNotResent()
    {
        var (worker, outbox, sender) = CreateWorker();
        var message = OutboxMessage.Create(
            IngestionEventTypes.DocumentIngestionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = Guid.NewGuid() }));
        message.MarkProcessed();
        outbox.Messages.Add(message);

        await RunOnePassAsync(worker);

        sender.SentRequests.Should().BeEmpty();
    }
}
