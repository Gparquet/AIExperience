using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Document.Exceptions;
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="UploadDocumentHandler"/> : vérifie la règle de détection de doublon exact
/// et de nouvelle version (même nom, contenu différent), ainsi que le remplacement atomique lorsqu'un
/// ReplaceDocumentId valide est fourni, en comparant toujours contre le document le plus récent.
/// </summary>
public sealed class UploadDocumentHandlerTests
{
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

    /// <summary>Faux service de hash retournant une valeur configurée, quel que soit le chemin de fichier (pas d'I/O disque dans ce test).</summary>
    private sealed class FakeFileHashService(string hash) : IFileHashService
    {
        public Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default) => Task.FromResult(hash);
    }

    /// <summary>Faux repository outbox en mémoire respectant le contrat <see cref="IOutboxRepository"/>.</summary>
    private sealed class FakeOutboxRepository : IOutboxRepository
    {
        public List<OutboxMessage> Messages { get; } = [];

        public void Add(OutboxMessage message) => Messages.Add(message);

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int maxCount, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>(Messages.Where(m => m.ProcessedAt is null).ToList());

        public Task UpdateAsync(OutboxMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Faux repository en mémoire respectant le contrat <see cref="IDocumentRepository"/>.</summary>
    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public List<Document> Documents { get; } = [];
        public List<Guid> DeletedIds { get; } = [];

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

        public Task UpdateAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            DeletedIds.Add(id);
            Documents.RemoveAll(d => d.Id == id);
            return Task.CompletedTask;
        }
    }

    private static UploadDocumentCommand CreateCommand(Guid? replaceDocumentId = null) => new()
    {
        FileName = "rapport.pdf",
        ContentType = "application/pdf",
        FileSizeBytes = 1024,
        UserId = "user-1",
        DocumentMetadata = DocumentMetadata.Create(title: "Rapport"),
        FilePath = "fake/rapport.pdf",
        ReplaceDocumentId = replaceDocumentId
    };

    [Fact]
    public async Task Handle_NoExistingDocument_CreatesDocumentNormally()
    {
        var repository = new FakeDocumentRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), unitOfWork, new FakeFileHashService("hash-a"));

        var response = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
        unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoExistingDocument_QueuesAnIngestionJobForTheNewDocument()
    {
        var repository = new FakeDocumentRepository();
        var outbox = new FakeOutboxRepository();
        var handler = new UploadDocumentHandler(repository, outbox, new FakeUnitOfWork(), new FakeFileHashService("hash-a"));

        var response = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        outbox.Messages.Should().ContainSingle(m =>
            m.EventType == IngestionEventTypes.DocumentIngestionRequested && m.Payload.Contains(response.DocumentId.ToString()));
    }

    [Fact]
    public async Task Handle_SetsFileReferenceToTheUploadedFilePath()
    {
        var repository = new FakeDocumentRepository();
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), new FakeUnitOfWork(), new FakeFileHashService("hash-a"));

        var response = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        repository.Documents.Single(d => d.Id == response.DocumentId).FileReference.Should().Be("fake/rapport.pdf");
    }

    [Fact]
    public async Task Handle_ExactDuplicateWithoutReplaceId_ThrowsWithExactDuplicateMatchType()
    {
        var repository = new FakeDocumentRepository();
        var existing = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-a");
        repository.Documents.Add(existing);
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), new FakeUnitOfWork(), new FakeFileHashService("hash-a"));

        var act = () => handler.HandleAsync(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DuplicateDocumentException>();
        exception.Which.ExistingDocumentId.Should().Be(existing.Id);
        exception.Which.MatchType.Should().Be(DocumentMatchType.ExactDuplicate);
        repository.Documents.Should().ContainSingle(); // aucune création, aucune suppression
    }

    [Fact]
    public async Task Handle_SameNameDifferentContentWithoutReplaceId_ThrowsWithSameNameDifferentContentMatchType()
    {
        var repository = new FakeDocumentRepository();
        var existing = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-old");
        repository.Documents.Add(existing);
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), new FakeUnitOfWork(), new FakeFileHashService("hash-new"));

        var act = () => handler.HandleAsync(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DuplicateDocumentException>();
        exception.Which.ExistingDocumentId.Should().Be(existing.Id);
        exception.Which.MatchType.Should().Be(DocumentMatchType.SameNameDifferentContent);
        repository.Documents.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ExactDuplicateWithMatchingReplaceId_DeletesOldAndCreatesNewInSingleTransaction()
    {
        var repository = new FakeDocumentRepository();
        var existing = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-a");
        repository.Documents.Add(existing);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), unitOfWork, new FakeFileHashService("hash-a"));

        var response = await handler.HandleAsync(CreateCommand(existing.Id), CancellationToken.None);

        repository.DeletedIds.Should().ContainSingle().Which.Should().Be(existing.Id);
        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
        response.DocumentId.Should().NotBe(existing.Id);
        unitOfWork.SaveChangesCallCount.Should().Be(1, "suppression et création doivent être atomiques en une seule transaction");
    }

    [Fact]
    public async Task Handle_SameNameDifferentContentWithMatchingReplaceId_DeletesOldAndCreatesNewInSingleTransaction()
    {
        var repository = new FakeDocumentRepository();
        var existing = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-old");
        repository.Documents.Add(existing);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), unitOfWork, new FakeFileHashService("hash-new"));

        var response = await handler.HandleAsync(CreateCommand(existing.Id), CancellationToken.None);

        repository.DeletedIds.Should().ContainSingle().Which.Should().Be(existing.Id);
        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
        response.DocumentId.Should().NotBe(existing.Id);
        unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ReplaceIdProvidedButNoDuplicateFound_CreatesDocumentNormally()
    {
        var repository = new FakeDocumentRepository();
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), new FakeUnitOfWork(), new FakeFileHashService("hash-a"));
        var staleReplaceId = Guid.NewGuid();

        var response = await handler.HandleAsync(CreateCommand(staleReplaceId), CancellationToken.None);

        repository.DeletedIds.Should().BeEmpty();
        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
    }

    [Fact]
    public async Task Handle_MultipleHomonyms_ComparesAgainstMostRecentOnly()
    {
        var repository = new FakeDocumentRepository();
        // Deux documents portant le même nom, créés successivement : Document.Create horodate
        // automatiquement CreatedAt via DateTimeOffset.UtcNow (résolution sub-microseconde sur .NET
        // moderne), donc "older" précède toujours "newer" en pratique — pas besoin de délai artificiel.
        var older = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-older");
        var newer = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-newer");
        repository.Documents.Add(older);
        repository.Documents.Add(newer);
        // Le hash uploadé correspond à "newer" : si la comparaison ciblait "older" par erreur,
        // ce test échouerait (MatchType serait SameNameDifferentContent et ExistingDocumentId celui de "older").
        var handler = new UploadDocumentHandler(repository, new FakeOutboxRepository(), new FakeUnitOfWork(), new FakeFileHashService("hash-newer"));

        var act = () => handler.HandleAsync(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DuplicateDocumentException>();
        exception.Which.ExistingDocumentId.Should().Be(newer.Id, "la comparaison doit cibler le document le plus récent portant ce nom");
        exception.Which.MatchType.Should().Be(DocumentMatchType.ExactDuplicate);
    }
}
