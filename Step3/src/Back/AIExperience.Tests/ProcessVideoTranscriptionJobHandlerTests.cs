using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Video.Command;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.Video;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Domain.Models.Video;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="ProcessVideoTranscriptionJobHandler"/> : vérifie le passage par le
/// statut "en cours de traitement", le respect des options par document (indexation RAG,
/// nettoyage LLM), et qu'un échec de transcription ne bloque jamais le document en "Processing".
/// </summary>
public sealed class ProcessVideoTranscriptionJobHandlerTests
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

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

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

    private sealed class FakeVideoProcessorService(Exception? exceptionToThrow = null) : IVideoProcessorService
    {
        public Task<string> ExtractAudioAsync(string videoPath, string outputAudioPath, CancellationToken cancellationToken = default)
            => exceptionToThrow is null ? Task.FromResult(outputAudioPath) : Task.FromException<string>(exceptionToThrow);

        public bool IsSupported(string filePath) => true;

        public Task<TimeSpan?> TryGetMediaDurationAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSpan?>(null);
    }

    private sealed class FakeTranscriptionService(TranscriptionResult result) : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(string audioPath, string language = "fr",
            Action<TranscriptionSegment>? onSegment = null, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    /// <summary>Faux client de chat minimal — seul GetResponseAsync est réellement exercé par le handler.</summary>
    private sealed class FakeChatClient(string cleanedText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new AiChatMessage(ChatRole.Assistant, cleanedText)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Non utilisé par ce handler.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static TranscriptionResult CreateResult(string fullText = "Bonjour euh, ceci est un test.")
        => new()
        {
            FullText = fullText,
            Segments = [new TranscriptionSegment { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(2), Text = fullText }],
            Duration = TimeSpan.FromSeconds(2),
            Language = "fr"
        };

    private static Document CreateVideoDocument(FakeDocumentRepository repository, bool indexInRag = true, bool cleanWithLlm = false)
    {
        var document = Document.Create("reunion.mp4", "video/mp4", 2048, "user-1",
            DocumentMetadata.Create(title: "Réunion", language: "fr"),
            indexInRag: indexInRag, cleanTranscriptionWithLlm: cleanWithLlm);

        var workFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        File.WriteAllText(workFilePath, "contenu vidéo factice");
        document.SetFileReference(workFilePath);
        repository.Documents.Add(document);
        return document;
    }

    private static ProcessVideoTranscriptionJobHandler CreateHandler(
        FakeDocumentRepository repository,
        IVideoProcessorService? videoProcessor = null,
        ITranscriptionService? transcriptionService = null,
        IIngestionService? ingestionService = null,
        IChatClient? chatClient = null,
        IIngestionNotifier? notifier = null)
        => new(
            videoProcessor ?? new FakeVideoProcessorService(),
            transcriptionService ?? new FakeTranscriptionService(CreateResult()),
            chatClient ?? new FakeChatClient("texte nettoyé"),
            ingestionService ?? new RecordingIngestionService(),
            repository,
            new DocumentIngestionStatusUpdater(repository, new FakeUnitOfWork(), notifier ?? new FakeIngestionNotifier()),
            new NoOpIngestionProgressReporter(),
            NullLogger<ProcessVideoTranscriptionJobHandler>.Instance);

    /// <summary>Faux service d'ingestion qui enregistre s'il a été appelé, pour vérifier le respect de IndexInRag.</summary>
    private sealed class RecordingIngestionService(Exception? exceptionToThrow = null) : IIngestionService
    {
        public bool IngestFromSegmentsCalled { get; private set; }

        public Task IngestAsync(string filePath, Guid documentId, DocumentMetadata metadata,
            ChunkingStrategy strategy = ChunkingStrategy.Recursive, CancellationToken ct = default) => Task.CompletedTask;

        public Task IngestTextAsync(string text, Guid documentId, DocumentMetadata metadata, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task IngestFromSegmentsAsync(IReadOnlyList<TranscriptionSegment> segments, Guid documentId, DocumentMetadata metadata, CancellationToken ct = default)
        {
            IngestFromSegmentsCalled = true;
            return exceptionToThrow is null ? Task.CompletedTask : Task.FromException(exceptionToThrow);
        }

        public Task IngestVideoOrAudioAsync(string filePath, Guid documentId, DocumentMetadata metadata, string language, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Handle_Succeeds_MarksDocumentCompletedAndStoresTranscription()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository);
        var handler = CreateHandler(repository, transcriptionService: new FakeTranscriptionService(CreateResult("texte transcrit")));

        var response = await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        response.Status.Should().Be(IngestionStatus.Completed);
        document.RawTranscription.Should().Be("texte transcrit");
    }

    [Fact]
    public async Task Handle_PassesThroughProcessingStatusBeforeTranscribing()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository);
        var notifier = new FakeIngestionNotifier();
        var handler = CreateHandler(repository, notifier: notifier);

        await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        notifier.NotifiedStatuses.Should().Equal("Processing", "Completed");
    }

    [Fact]
    public async Task Handle_IndexInRagTrue_CallsIngestFromSegments()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository, indexInRag: true);
        var ingestionService = new RecordingIngestionService();
        var handler = CreateHandler(repository, ingestionService: ingestionService);

        await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        ingestionService.IngestFromSegmentsCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_IndexInRagFalse_SkipsIngestFromSegments()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository, indexInRag: false);
        var ingestionService = new RecordingIngestionService();
        var handler = CreateHandler(repository, ingestionService: ingestionService);

        var response = await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        ingestionService.IngestFromSegmentsCalled.Should().BeFalse();
        response.Status.Should().Be(IngestionStatus.Completed, "une transcription sans indexation reste un succès");
    }

    [Fact]
    public async Task Handle_CleanWithLlmFalse_LeavesCleanedTranscriptionNull()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository, cleanWithLlm: false);
        var handler = CreateHandler(repository);

        await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        document.CleanedTranscription.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CleanWithLlmTrue_StoresCleanedTranscription()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository, cleanWithLlm: true);
        var handler = CreateHandler(repository, chatClient: new FakeChatClient("version nettoyée"));

        await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        document.CleanedTranscription.Should().Be("version nettoyée");
    }

    [Fact]
    public async Task Handle_TranscriptionThrows_MarksDocumentFailedWithGenericMessage()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository);
        var handler = CreateHandler(repository,
            transcriptionService: new ThrowingTranscriptionService(new InvalidOperationException("modèle Whisper introuvable sur disque")));

        var response = await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        response.Status.Should().Be(IngestionStatus.Failed);
        document.ErrorMessage.Should().NotContain("Whisper introuvable sur disque");
    }

    private sealed class ThrowingTranscriptionService(Exception exception) : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(string audioPath, string language = "fr",
            Action<TranscriptionSegment>? onSegment = null, CancellationToken cancellationToken = default)
            => Task.FromException<TranscriptionResult>(exception);
    }

    [Fact]
    public async Task Handle_TranscriptionCancelled_PropagatesWithoutMarkingDocumentFailed()
    {
        var repository = new FakeDocumentRepository();
        var document = CreateVideoDocument(repository);
        var handler = CreateHandler(repository,
            transcriptionService: new ThrowingTranscriptionService(new OperationCanceledException()));

        var act = () => handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        document.Status.Should().Be(IngestionStatus.Processing);

        File.Delete(document.FileReference!);
    }

    [Fact]
    public async Task Handle_AudioOnlyFile_SkipsAudioExtraction()
    {
        var repository = new FakeDocumentRepository();
        var document = Document.Create("note.mp3", "audio/mpeg", 512, "user-1", DocumentMetadata.Create(title: "Note"));
        var workFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
        File.WriteAllText(workFilePath, "contenu audio factice");
        document.SetFileReference(workFilePath);
        repository.Documents.Add(document);

        var videoProcessor = new ThrowingIfCalledVideoProcessorService();
        var handler = CreateHandler(repository, videoProcessor: videoProcessor);

        var response = await handler.HandleAsync(new ProcessVideoTranscriptionJobCommand { DocumentId = document.Id }, CancellationToken.None);

        response.Status.Should().Be(IngestionStatus.Completed);
    }

    private sealed class ThrowingIfCalledVideoProcessorService : IVideoProcessorService
    {
        public Task<string> ExtractAudioAsync(string videoPath, string outputAudioPath, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("L'extraction audio ne doit pas être appelée pour un fichier déjà audio.");

        public bool IsSupported(string filePath) => true;

        public Task<TimeSpan?> TryGetMediaDurationAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSpan?>(null);
    }

    private sealed class NoOpIngestionProgressReporter : IIngestionProgressReporter
    {
        public Task EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportAsync(Guid documentId, IngestionStage stage, int? percent, IngestionProgressCounters counters, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
