using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Application.Video.Command;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="CreateVideoTranscriptionJobHandler"/> : vérifie la création rapide du
/// document et la mise en file du job de transcription associé, dans la même transaction.
/// </summary>
public sealed class CreateVideoTranscriptionJobHandlerTests
{
    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public List<Document> Documents { get; } = [];

        public Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));
        public Task<IEnumerable<Document>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<Document>>(Documents);
        public Task<(IEnumerable<Document> Items, int TotalCount)> GetByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<(IEnumerable<Document>, int)>((Documents.Where(d => d.UserId == userId), Documents.Count));
        public Task<IEnumerable<Document>> GetPendingDocumentsAsync(int maxCount = 10, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<Document>>([]);
        public Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default)
            => Task.FromResult<Document?>(null);
        public Task AddAsync(Document document, CancellationToken ct = default)
        {
            Documents.Add(document);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeOutboxRepository : IOutboxRepository
    {
        public List<OutboxMessage> Messages { get; } = [];

        public void Add(OutboxMessage message) => Messages.Add(message);
        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int maxCount, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>(Messages.Where(m => m.ProcessedAt is null).ToList());
        public Task UpdateAsync(OutboxMessage message, CancellationToken ct = default) => Task.CompletedTask;
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

    private static CreateVideoTranscriptionJobCommand CreateCommand() => new()
    {
        FileName = "reunion.mp4",
        ContentType = "video/mp4",
        FileSizeBytes = 2048,
        FileReference = "work/reunion.mp4",
        Language = "en",
        CleanWithLlm = true,
        IndexInRag = false,
        Title = "Réunion hebdo"
    };

    [Fact]
    public async Task Handle_CreatesDocumentWithRequestedOptions()
    {
        var repository = new FakeDocumentRepository();
        var handler = new CreateVideoTranscriptionJobHandler(repository, new FakeOutboxRepository(), new FakeUnitOfWork());

        var response = await handler.Handle(CreateCommand(), CancellationToken.None);

        var document = repository.Documents.Single(d => d.Id == response.DocumentId);
        document.Metadata.Language.Should().Be("en");
        document.Metadata.Title.Should().Be("Réunion hebdo");
        document.IndexInRag.Should().BeFalse();
        document.CleanTranscriptionWithLlm.Should().BeTrue();
        document.FileReference.Should().Be("work/reunion.mp4");
    }

    [Fact]
    public async Task Handle_WithoutExplicitTitle_UsesFileNameWithoutExtension()
    {
        var repository = new FakeDocumentRepository();
        var handler = new CreateVideoTranscriptionJobHandler(repository, new FakeOutboxRepository(), new FakeUnitOfWork());

        var response = await handler.Handle(CreateCommand() with { Title = null }, CancellationToken.None);

        repository.Documents.Single(d => d.Id == response.DocumentId).Metadata.Title.Should().Be("reunion");
    }

    [Fact]
    public async Task Handle_QueuesAVideoTranscriptionJobForTheNewDocument()
    {
        var repository = new FakeDocumentRepository();
        var outbox = new FakeOutboxRepository();
        var handler = new CreateVideoTranscriptionJobHandler(repository, outbox, new FakeUnitOfWork());

        var response = await handler.Handle(CreateCommand(), CancellationToken.None);

        outbox.Messages.Should().ContainSingle(m =>
            m.EventType == IngestionEventTypes.VideoTranscriptionRequested && m.Payload.Contains(response.DocumentId.ToString()));
    }

    [Fact]
    public async Task Handle_SavesOnceForBothDocumentAndOutboxMessage()
    {
        var repository = new FakeDocumentRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateVideoTranscriptionJobHandler(repository, new FakeOutboxRepository(), unitOfWork);

        await handler.Handle(CreateCommand(), CancellationToken.None);

        unitOfWork.SaveChangesCallCount.Should().Be(1, "création du document et mise en file doivent être atomiques");
    }
}
