using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="IngestDocumentHandler"/> : vérifie que le handler passe par le statut
/// "en cours de traitement" avant l'ingestion, orchestre correctement l'ingestion (succès →
/// Completed, échec → Failed) sans exposer les détails techniques de l'exception, et qu'une
/// annulation explicite n'est pas traitée comme un échec métier.
/// </summary>
public sealed class IngestDocumentHandlerTests
{
    /// <summary>Faux repository en mémoire respectant le contrat <see cref="IDocumentRepository"/>.</summary>
    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public List<Document> Documents { get; } = [];
        public int UpdateCallCount { get; private set; }

        public Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));

        public Task<IEnumerable<Document>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<Document>>(Documents);

        public Task<(IEnumerable<Document> Items, int TotalCount)> GetByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<(IEnumerable<Document>, int)>((Documents.Where(d => d.UserId == userId), Documents.Count));

        public Task<IEnumerable<Document>> GetPendingDocumentsAsync(int maxCount = 10, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<Document>>([]);

        public Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default)
            => Task.FromResult(Documents
                .Where(d => d.UserId == userId && d.FileName == fileName)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefault());

        public Task AddAsync(Document document, CancellationToken ct = default)
        {
            Documents.Add(document);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Document document, CancellationToken ct = default)
        {
            UpdateCallCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            Documents.RemoveAll(d => d.Id == id);
            return Task.CompletedTask;
        }
    }

    /// <summary>Faux UnitOfWork en mémoire (pas de bibliothèque de mocking dans ce projet).</summary>
    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>Faux notifieur en mémoire, ne fait qu'enregistrer les appels pour vérification.</summary>
    private sealed class FakeIngestionNotifier : IIngestionNotifier
    {
        public List<string> NotifiedStatuses { get; } = [];

        public Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        {
            NotifiedStatuses.Add(status);
            return Task.CompletedTask;
        }

        public Task NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Faux service d'ingestion dont le comportement (succès/échec/annulation) est configurable par le test.</summary>
    private sealed class FakeIngestionService(Exception? exceptionToThrow = null) : IIngestionService
    {
        public Task IngestAsync(string filePath, Guid documentId, DocumentMetadata metadata,
            ChunkingStrategy strategy = ChunkingStrategy.Recursive, CancellationToken ct = default)
            => exceptionToThrow is null ? Task.CompletedTask : Task.FromException(exceptionToThrow);

        public Task IngestTextAsync(string text, Guid documentId, DocumentMetadata metadata, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task IngestFromSegmentsAsync(IReadOnlyList<AIExperience.Rag.Domain.Models.Video.TranscriptionSegment> segments,
            Guid documentId, DocumentMetadata metadata, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task IngestVideoOrAudioAsync(string filePath, Guid documentId, DocumentMetadata metadata,
            string language, CancellationToken ct = default)
            => exceptionToThrow is null ? Task.CompletedTask : Task.FromException(exceptionToThrow);

        public Task DeleteAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static Document CreateStoredDocument(FakeDocumentRepository repository, string fileName = "rapport.pdf")
    {
        var document = Document.Create(fileName, "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-a");
        // Le fichier de travail référencé doit exister réellement : le handler le supprime une
        // fois le document arrivé dans un état final.
        var workFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{Path.GetExtension(fileName)}");
        File.WriteAllText(workFilePath, "contenu factice");
        document.SetFileReference(workFilePath);
        repository.Documents.Add(document);
        return document;
    }

    private sealed class NoOpIngestionProgressReporter : IIngestionProgressReporter
    {
        public Task EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportAsync(Guid documentId, IngestionStage stage, int? percent, IngestionProgressCounters counters, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static IngestDocumentHandler CreateHandler(
        FakeDocumentRepository repository, IIngestionService ingestionService, IIngestionNotifier? notifier = null)
        => new(ingestionService, repository,
            new DocumentIngestionStatusUpdater(repository, new FakeUnitOfWork(), notifier ?? new FakeIngestionNotifier()),
            new NoOpIngestionProgressReporter(),
            NullLogger<IngestDocumentHandler>.Instance);

    [Fact]
    public async Task Handle_IngestionSucceeds_MarksDocumentCompletedAndSaves()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateStoredDocument(repository);
        var handler = CreateHandler(repository, new FakeIngestionService());

        var response = await handler.Handle(new IngestDocumentCommand { DocumentId = document.Id }, CancellationToken.None);

        response.Status.Should().Be(IngestionStatus.Completed);
        document.Status.Should().Be(IngestionStatus.Completed);
    }

    [Fact]
    public async Task Handle_PassesThroughProcessingStatusBeforeIngesting()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateStoredDocument(repository);
        var notifier = new FakeIngestionNotifier();
        var handler = CreateHandler(repository, new FakeIngestionService(), notifier);

        await handler.Handle(new IngestDocumentCommand { DocumentId = document.Id }, CancellationToken.None);

        notifier.NotifiedStatuses.Should().Equal("Processing", "Completed");
    }

    [Fact]
    public async Task Handle_IngestionThrows_MarksDocumentFailedWithGenericMessage()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateStoredDocument(repository);
        var handler = CreateHandler(repository,
            new FakeIngestionService(new InvalidOperationException("Npgsql: connexion refusée sur host interne 10.0.0.5")));

        var response = await handler.Handle(new IngestDocumentCommand { DocumentId = document.Id }, CancellationToken.None);

        response.Status.Should().Be(IngestionStatus.Failed);
        document.Status.Should().Be(IngestionStatus.Failed);
        // Le message stocké/exposé doit rester générique : pas de fuite du détail technique de l'exception.
        document.ErrorMessage.Should().NotContain("Npgsql").And.NotContain("10.0.0.5");
    }

    [Fact]
    public async Task Handle_IngestionCancelled_PropagatesWithoutMarkingDocumentFailed()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateStoredDocument(repository);
        var handler = CreateHandler(repository, new FakeIngestionService(new OperationCanceledException()));

        var act = () => handler.Handle(new IngestDocumentCommand { DocumentId = document.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Le passage à Processing a bien lieu avant la tentative d'ingestion (corrige le bug où le
        // statut restait Pending pendant tout le traitement) ; une annulation n'est pas un échec métier.
        document.Status.Should().Be(IngestionStatus.Processing);
    }

    [Fact]
    public async Task Handle_IngestionCancelled_KeepsTheWorkFileForARetry()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateStoredDocument(repository);
        var handler = CreateHandler(repository, new FakeIngestionService(new OperationCanceledException()));

        var act = () => handler.Handle(new IngestDocumentCommand { DocumentId = document.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(document.FileReference).Should().BeTrue("le fichier doit rester disponible pour une reprise après annulation");

        File.Delete(document.FileReference!);
    }

    [Fact]
    public async Task Handle_DocumentNotFound_ThrowsInvalidOperationException()
    {
        var repository = new FakeDocumentRepository();
        var handler = CreateHandler(repository, new FakeIngestionService());

        var act = () => handler.Handle(new IngestDocumentCommand { DocumentId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
