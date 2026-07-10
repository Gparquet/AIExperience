using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIExperience.Tests;

public class IngestionProgressReporterTests
{
    // Faux notifier qui compte les notifications reçues.
    private sealed class CountingNotifier : IIngestionNotifier
    {
        public int ProgressCount { get; private set; }
        public Task NotifyStatusChangedAsync(Guid id, string status, string? err, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyProgressAsync(Guid id, IngestionProgress p, CancellationToken ct = default)
        {
            ProgressCount++;
            return Task.CompletedTask;
        }
    }

    // Sous-classe de test : remplace l'écriture en base par un simple compteur.
    private sealed class TestReporter(
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory sf,
        IIngestionNotifier n,
        IOptions<IngestionOptions> o,
        Microsoft.Extensions.Logging.ILogger<AIExperience.Rag.Infrastructure.Notifications.IngestionProgressReporter> l)
        : AIExperience.Rag.Infrastructure.Notifications.IngestionProgressReporter(sf, n, o, l)
    {
        public int PersistCount { get; private set; }
        protected override Task PersistAsync(Guid documentId, IngestionProgress? progress, CancellationToken ct)
        {
            PersistCount++;
            return Task.CompletedTask;
        }
    }

    private static TestReporter CreateReporter(IIngestionNotifier notifier, out IOptions<IngestionOptions> options)
    {
        options = Options.Create(new IngestionOptions
        {
            WorkDirectory = "unused",
            ProgressPersistThrottleSeconds = 60 // grand : garantit le throttling pendant le test
        });
        var scopeFactory = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider().GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AIExperience.Rag.Infrastructure.Notifications.IngestionProgressReporter>.Instance;
        return new TestReporter(scopeFactory, notifier, options, logger);
    }

    [Fact]
    public async Task ReportAsync_NotifieAChaqueAppel()
    {
        // Le canal temps réel n'est pas throttlé : chaque tick doit être poussé.
        var notifier = new CountingNotifier();
        var reporter = CreateReporter(notifier, out _);
        var id = Guid.NewGuid();

        await reporter.ReportAsync(id, IngestionStage.Embedding, 10, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 20, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 30, IngestionProgressCounters.Empty);

        notifier.ProgressCount.Should().Be(3);
    }

    [Fact]
    public async Task ReportAsync_ThrottleLesPersistances()
    {
        // Avec un throttle de 60s, seule la première persistance passe ; les suivantes sont ignorées.
        var reporter = CreateReporter(new CountingNotifier(), out _);
        var id = Guid.NewGuid();

        await reporter.ReportAsync(id, IngestionStage.Embedding, 10, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 20, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 30, IngestionProgressCounters.Empty);

        reporter.PersistCount.Should().Be(1);
    }

    [Fact]
    public async Task EnterStageAsync_PersisteToujours()
    {
        // Chaque transition d'étape doit être persistée, throttle ou pas.
        var reporter = CreateReporter(new CountingNotifier(), out _);
        var id = Guid.NewGuid();

        await reporter.EnterStageAsync(id, IngestionStage.Chunking);
        await reporter.EnterStageAsync(id, IngestionStage.Embedding);

        reporter.PersistCount.Should().Be(2);
    }
}
