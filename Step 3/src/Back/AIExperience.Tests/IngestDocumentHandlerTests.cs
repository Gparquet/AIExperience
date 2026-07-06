using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="IngestDocumentHandler"/> : vérifie que le handler orchestre correctement
/// l'ingestion (succès → Completed, échec → Failed) sans exposer les détails techniques de l'exception,
/// et qu'une annulation explicite n'est pas traitée comme un échec métier.
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
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(0);
        }
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

        public Task DeleteAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static Document CreateStoredDocument(FakeDocumentRepository repository)
    {
        var document = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-a");
        repository.Documents.Add(document);
        return document;
    }

    [Fact]
    public async Task Handle_IngestionSucceeds_MarksDocumentCompletedAndSaves()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateStoredDocument(repository);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new IngestDocumentHandler(new FakeIngestionService(), repository, unitOfWork,
            NullLogger<IngestDocumentHandler>.Instance);

        var response = await handler.Handle(
            new IngestDocumentCommand { DocumentId = document.Id, FilePath = "fake/rapport.pdf", DocumentMetadata = document.Metadata },
            CancellationToken.None);

        response.Status.Should().Be(IngestionStatus.Completed);
        document.Status.Should().Be(IngestionStatus.Completed);
        unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_IngestionThrows_MarksDocumentFailedWithGenericMessage()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateStoredDocument(repository);
        var handler = new IngestDocumentHandler(
            new FakeIngestionService(new InvalidOperationException("Npgsql: connexion refusée sur host interne 10.0.0.5")),
            repository, new FakeUnitOfWork(), NullLogger<IngestDocumentHandler>.Instance);

        var response = await handler.Handle(
            new IngestDocumentCommand { DocumentId = document.Id, FilePath = "fake/rapport.pdf", DocumentMetadata = document.Metadata },
            CancellationToken.None);

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
        var handler = new IngestDocumentHandler(
            new FakeIngestionService(new OperationCanceledException()),
            repository, new FakeUnitOfWork(), NullLogger<IngestDocumentHandler>.Instance);

        var act = () => handler.Handle(
            new IngestDocumentCommand { DocumentId = document.Id, FilePath = "fake/rapport.pdf", DocumentMetadata = document.Metadata },
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        document.Status.Should().Be(IngestionStatus.Pending, "une annulation n'est pas un échec métier");
    }

    [Fact]
    public async Task Handle_DocumentNotFound_ThrowsInvalidOperationException()
    {
        var repository = new FakeDocumentRepository();
        var handler = new IngestDocumentHandler(new FakeIngestionService(), repository, new FakeUnitOfWork(),
            NullLogger<IngestDocumentHandler>.Instance);

        var act = () => handler.Handle(
            new IngestDocumentCommand { DocumentId = Guid.NewGuid(), FilePath = "fake/rapport.pdf", DocumentMetadata = DocumentMetadata.Empty },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
