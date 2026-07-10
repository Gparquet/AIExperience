using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="DocumentIngestionStatusUpdater"/> : vérifie que chaque transition de
/// statut persiste le document puis notifie, dans cet ordre, et que la notification transporte
/// bien le statut et le message d'erreur attendus.
/// </summary>
public sealed class DocumentIngestionStatusUpdaterTests
{
    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public int UpdateCallCount { get; private set; }

        public Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<IEnumerable<Document>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<Document>>([]);
        public Task<(IEnumerable<Document> Items, int TotalCount)> GetByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<(IEnumerable<Document>, int)>(([], 0));
        public Task<IEnumerable<Document>> GetPendingDocumentsAsync(int maxCount = 10, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<Document>>([]);
        public Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default)
            => Task.FromResult<Document?>(null);
        public Task AddAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Document document, CancellationToken ct = default)
        {
            UpdateCallCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeIngestionNotifier : IIngestionNotifier
    {
        public List<(Guid DocumentId, string Status, string? ErrorMessage)> Notifications { get; } = [];

        public Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        {
            Notifications.Add((documentId, status, errorMessage));
            return Task.CompletedTask;
        }

        public Task NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static Document CreateDocument()
        => Document.Create("rapport.pdf", "application/pdf", 1024, "user-1", DocumentMetadata.Create(title: "Rapport"));

    [Fact]
    public async Task MarkProcessingAsync_UpdatesStatusPersistsAndNotifies()
    {
        var repository = new FakeDocumentRepository();
        var unitOfWork = new FakeUnitOfWork();
        var notifier = new FakeIngestionNotifier();
        var updater = new DocumentIngestionStatusUpdater(repository, unitOfWork, notifier);
        var document = CreateDocument();

        await updater.MarkProcessingAsync(document, CancellationToken.None);

        document.Status.Should().Be(IngestionStatus.Processing);
        repository.UpdateCallCount.Should().Be(1);
        unitOfWork.SaveChangesCallCount.Should().Be(1);
        notifier.Notifications.Should().ContainSingle(n => n.DocumentId == document.Id && n.Status == "Processing" && n.ErrorMessage == null);
    }

    [Fact]
    public async Task MarkCompletedAsync_UpdatesStatusPersistsAndNotifies()
    {
        var repository = new FakeDocumentRepository();
        var unitOfWork = new FakeUnitOfWork();
        var notifier = new FakeIngestionNotifier();
        var updater = new DocumentIngestionStatusUpdater(repository, unitOfWork, notifier);
        var document = CreateDocument();

        await updater.MarkCompletedAsync(document, CancellationToken.None);

        document.Status.Should().Be(IngestionStatus.Completed);
        notifier.Notifications.Should().ContainSingle(n => n.Status == "Completed" && n.ErrorMessage == null);
    }

    [Fact]
    public async Task MarkFailedAsync_UpdatesStatusPersistsAndNotifiesWithErrorMessage()
    {
        var repository = new FakeDocumentRepository();
        var unitOfWork = new FakeUnitOfWork();
        var notifier = new FakeIngestionNotifier();
        var updater = new DocumentIngestionStatusUpdater(repository, unitOfWork, notifier);
        var document = CreateDocument();

        await updater.MarkFailedAsync(document, "L'ingestion a échoué.", CancellationToken.None);

        document.Status.Should().Be(IngestionStatus.Failed);
        notifier.Notifications.Should().ContainSingle(n => n.Status == "Failed" && n.ErrorMessage == "L'ingestion a échoué.");
    }

    [Fact]
    public async Task MarkCompletedAsync_PersistsBeforeNotifying()
    {
        // Le statut doit déjà être en base au moment où le front est prévenu, pour qu'un
        // rafraîchissement déclenché par la notification lise un état cohérent.
        var order = new List<string>();
        var repository = new FakeDocumentRepository();
        var unitOfWork = new RecordingUnitOfWork(order);
        var notifier = new RecordingNotifier(order);
        var updater = new DocumentIngestionStatusUpdater(repository, unitOfWork, notifier);
        var document = CreateDocument();

        await updater.MarkCompletedAsync(document, CancellationToken.None);

        order.Should().Equal("save", "notify");
    }

    private sealed class RecordingUnitOfWork(List<string> order) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            order.Add("save");
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingNotifier(List<string> order) : IIngestionNotifier
    {
        public Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        {
            order.Add("notify");
            return Task.CompletedTask;
        }

        public Task NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
