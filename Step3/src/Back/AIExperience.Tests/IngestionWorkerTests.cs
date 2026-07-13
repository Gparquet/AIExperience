using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Application.Video.Command;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentAssertions;
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

    /// <summary>Faux ICommandDispatcher qui enregistre les commandes reçues et peut être configuré pour échouer.</summary>
    private sealed class FakeDispatcher : ICommandDispatcher
    {
        public List<object> SentRequests { get; } = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(command);
            return ExceptionToThrow is null
                ? Task.FromResult(default(TResponse)!)
                : Task.FromException<TResponse>(ExceptionToThrow);
        }
    }

    /// <summary>
    /// Construit un worker branché sur un vrai conteneur DI (scopes réels), avec des dépendances
    /// en mémoire — c'est la façon la plus simple de tester un BackgroundService sans dupliquer
    /// sa logique interne de scoping.
    /// </summary>
    private static (IngestionWorker Worker, FakeOutboxRepository Outbox, FakeDispatcher Dispatcher) CreateWorker(
        int maxRetryAttempts = 5)
    {
        var outbox = new FakeOutboxRepository();
        var dispatcher = new FakeDispatcher();

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxRepository>(outbox);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddSingleton<ICommandDispatcher>(dispatcher);
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

        return (worker, outbox, dispatcher);
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
        var (worker, outbox, dispatcher) = CreateWorker();
        var documentId = Guid.NewGuid();
        outbox.Messages.Add(OutboxMessage.Create(
            IngestionEventTypes.DocumentIngestionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = documentId })));

        await RunOnePassAsync(worker);

        dispatcher.SentRequests.OfType<IngestDocumentCommand>().Should().ContainSingle(cmd => cmd.DocumentId == documentId);
    }

    [Fact]
    public async Task ExecuteAsync_VideoTranscriptionMessage_SendsProcessVideoTranscriptionJobCommand()
    {
        var (worker, outbox, dispatcher) = CreateWorker();
        var documentId = Guid.NewGuid();
        outbox.Messages.Add(OutboxMessage.Create(
            IngestionEventTypes.VideoTranscriptionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = documentId })));

        await RunOnePassAsync(worker);

        dispatcher.SentRequests.OfType<ProcessVideoTranscriptionJobCommand>().Should().ContainSingle(cmd => cmd.DocumentId == documentId);
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
        var (worker, outbox, dispatcher) = CreateWorker(maxRetryAttempts: 5);
        dispatcher.ExceptionToThrow = new InvalidOperationException("base de données momentanément indisponible");
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
        var (worker, outbox, dispatcher) = CreateWorker(maxRetryAttempts: 2);
        dispatcher.ExceptionToThrow = new InvalidOperationException("payload systématiquement invalide");
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
        var (worker, outbox, dispatcher) = CreateWorker();
        var message = OutboxMessage.Create(
            IngestionEventTypes.DocumentIngestionRequested,
            System.Text.Json.JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = Guid.NewGuid() }));
        message.MarkProcessed();
        outbox.Messages.Add(message);

        await RunOnePassAsync(worker);

        dispatcher.SentRequests.Should().BeEmpty();
    }
}
