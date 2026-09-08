using System.Runtime.CompilerServices;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.AI.Rag;
using AIExperience.Rag.Infrastructure.Options;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
// Alias : dans ce fichier, ChatMessage désigne toujours l'entité Domain (les signatures LLM du
// FakeChatClient qualifient explicitement Microsoft.Extensions.AI.ChatMessage).
using ChatMessage = AIExperience.Rag.Domain.Entities.ChatMessage;

namespace AIExperience.Tests;

/// <summary>
/// Tests de la résolution/persistance de session branchées dans <see cref="RagPipelineService"/> (Lot 3).
/// Utilise le chemin "LLM direct" (UseRag=false) pour n'exercer que la session + le LLM sans dépendances
/// de récupération, et un chemin RAG minimal pour vérifier la persistance des citations.
/// </summary>
public sealed class RagPipelineServiceHistoryTests
{
    /// <summary>Faux repository conversation en mémoire (pas de bibliothèque de mocking dans ce projet).</summary>
    private sealed class FakeConversationRepository : IConversationRepository
    {
        public List<ConversationSession> Sessions { get; } = [];
        public List<ChatMessage> Messages { get; } = [];

        public Task<ConversationSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default)
            => Task.FromResult(Sessions.FirstOrDefault(s => s.Id == sessionId));

        public Task<IEnumerable<ConversationSession>> GetSessionsByUserIdAsync(string userId, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<ConversationSession>>(Sessions.Where(s => s.UserId == userId));

        public Task<IEnumerable<ChatMessage>> GetMessagesAsync(Guid sessionId, int maxTurns = 20, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<ChatMessage>>(
                Messages.Where(m => m.SessionId == sessionId).OrderBy(m => m.CreatedAt).Take(maxTurns));

        public Task<IReadOnlyList<ConversationSessionSummary>> GetSessionSummariesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationSessionSummary>>(
                Sessions.Where(s => s.UserId == userId)
                    .Select(s => new ConversationSessionSummary(s.Id, s.Title, s.UpdatedAt, Messages.Count(m => m.SessionId == s.Id)))
                    .ToList());

        public Task AddSessionAsync(ConversationSession session, CancellationToken ct = default)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task AddMessageAsync(ChatMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task UpdateSessionAsync(ConversationSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
        {
            var removed = Sessions.RemoveAll(s => s.Id == sessionId) > 0;
            return Task.FromResult(removed);
        }

        public Task<int> DeleteAllSessionsAsync(string userId, CancellationToken ct = default)
        {
            var removed = Sessions.RemoveAll(s => s.UserId == userId);
            return Task.FromResult(removed);
        }
    }

    /// <summary>Faux UnitOfWork : compte les commits et peut simuler un échec de persistance.</summary>
    private sealed class FakeUnitOfWork(bool throwOnSave = false) : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            if (throwOnSave)
                throw new InvalidOperationException("Échec DB simulé.");
            return Task.FromResult(0);
        }
    }

    /// <summary>Faux IChatClient renvoyant une réponse canée, en une passe comme en streaming.</summary>
    private sealed class FakeChatClient(string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, answer)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>Faux magasin vectoriel renvoyant un unique chunk pour la recherche cosinus, rien en lexical.</summary>
    private sealed class FakeVectorStoreService(DocumentChunk chunk) : IVectorStoreService
    {
        public Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchAsync(
            float[] vector, int topK = 20, Guid[]? documentIds = null, double scoreThreshold = 0.75, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(DocumentChunk, double)>>([(chunk, 0.9)]);

        public Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchFullTextAsync(
            string query, int topK = 10, Guid[]? documentIds = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(DocumentChunk, double)>>([]);

        public Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchLexicalAsync(
            string query, int topK = 10, Guid[]? documentIds = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(DocumentChunk, double)>>([]);

        public Task UpsertAsync(DocumentChunk chunk, float[] embedding, string language, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertBatchAsync(
            IReadOnlyList<(DocumentChunk Chunk, float[] Embedding, string Language)> items, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DocumentChunk>> GetAllChunksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentChunk>>([]);
    }

    /// <summary>Faux service d'embedding renvoyant un vecteur neutre (la recherche est simulée par le fake vectoriel).</summary>
    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken ct = default)
            => Task.FromResult(new float[768]);

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, EmbeddingTaskType taskType, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>([]);
    }

    /// <summary>Variante du faux service d'embedding qui capture le dernier texte embeddé : vérifie
    /// que la récupération reçoit bien la question condensée, et non la question brute.</summary>
    private sealed class CapturingEmbeddingService : IEmbeddingService
    {
        public string? LastText { get; private set; }

        public Task<float[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken ct = default)
        {
            LastText = text;
            return Task.FromResult(new float[768]);
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, EmbeddingTaskType taskType, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>([]);
    }

    /// <summary>Faux service de condensation renvoyant toujours la même question condensée canée.</summary>
    private sealed class FakeQueryCondensationService(string condensed) : IQueryCondensationService
    {
        public Task<string> CondenseAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
            => Task.FromResult(condensed);
    }

    /// <summary>Faux service de condensation qui échoue si on l'appelle — sert à prouver qu'un garde-fou
    /// (premier tour, condensation désactivée) empêche bien l'appel LLM correspondant.</summary>
    private sealed class ThrowingQueryCondensationService : IQueryCondensationService
    {
        public Task<string> CondenseAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
            => throw new InvalidOperationException("La condensation ne devait pas être appelée dans ce scénario.");
    }

    /// <summary>Construit un service en mode "LLM direct" : seules la session et le LLM sont exercés, le reste est inutile (null!).</summary>
    private static RagPipelineService CreateDirectLlmService(
        FakeConversationRepository repo, FakeUnitOfWork uow, FakeChatClient chat)
        => new(
            vectorStoreService: null!, embeddingService: null!, adaptiveQueryRouter: null!,
            hydeService: null!, multiQueryService: null!, rerankerService: null!, contextCompressorService: null!,
            queryCondensationService: null!,
            conversationRepository: repo, unitOfWork: uow,
            options: Options.Create(new RagOptions()), chatClient: chat,
            logger: NullLogger<RagPipelineService>.Instance);

    [Fact]
    public async Task AskAsync_SansSessionId_CreeUneNouvelleSessionEtPersisteLesDeuxMessages()
    {
        var repo = new FakeConversationRepository();
        var uow = new FakeUnitOfWork();
        var service = CreateDirectLlmService(repo, uow, new FakeChatClient("Bonjour"));

        var response = await service.AskAsync(new RagQuery { Question = "Salut", UserId = "user-1", UseRag = false });

        response.SessionId.Should().NotBe(Guid.Empty);
        repo.Sessions.Should().ContainSingle().Which.UserId.Should().Be("user-1");
        repo.Messages.Should().HaveCount(2);
        repo.Messages.Should().Contain(m => m.Role == MessageRole.User && m.Content == "Salut");
        repo.Messages.Should().Contain(m => m.Role == MessageRole.Assistant && m.Content == "Bonjour");
        uow.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AskAsync_AvecSessionIdExistant_ReutiliseLaSessionSansEnCreerUneAutre()
    {
        var repo = new FakeConversationRepository();
        var existing = ConversationSession.Create("user-1", "Titre existant");
        repo.Sessions.Add(existing);
        var service = CreateDirectLlmService(repo, new FakeUnitOfWork(), new FakeChatClient("Réponse"));

        var response = await service.AskAsync(new RagQuery
        {
            Question = "Suite de la discussion", UserId = "user-1", SessionId = existing.Id, UseRag = false
        });

        response.SessionId.Should().Be(existing.Id);
        repo.Sessions.Should().ContainSingle("aucune nouvelle session ne doit être créée quand un id valide est fourni");
    }

    [Fact]
    public async Task AskAsync_QuandLaPersistanceEchoue_RetourneQuandMemeLaReponse()
    {
        var repo = new FakeConversationRepository();
        var uow = new FakeUnitOfWork(throwOnSave: true);
        var service = CreateDirectLlmService(repo, uow, new FakeChatClient("Malgré tout"));

        var response = await service.AskAsync(new RagQuery { Question = "Q", UserId = "user-1", UseRag = false });

        response.Answer.Should().Be("Malgré tout", "un échec de persistance ne doit pas priver l'utilisateur de sa réponse");
        uow.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AskAsync_ModeRag_PersisteLesCitationsAvecLIdDuMessageAssistant()
    {
        // Chunk source dont le nom apparaîtra dans la balise [SOURCE:] de la réponse simulée.
        var chunk = DocumentChunk.Create(
            Guid.NewGuid(), "Le chien dort paisiblement.", chunkIndex: 0, embeddingDimensions: 768,
            pageNumber: 1, documentName: "TestDoc.pdf");
        var repo = new FakeConversationRepository();
        var service = new RagPipelineService(
            vectorStoreService: new FakeVectorStoreService(chunk), embeddingService: new FakeEmbeddingService(),
            adaptiveQueryRouter: null!, hydeService: null!, multiQueryService: null!,
            rerankerService: null!, contextCompressorService: null!,
            queryCondensationService: null!,
            conversationRepository: repo, unitOfWork: new FakeUnitOfWork(),
            options: Options.Create(new RagOptions()),
            chatClient: new FakeChatClient("Le chien dort [SOURCE: TestDoc.pdf, p.1]."),
            logger: NullLogger<RagPipelineService>.Instance);

        var response = await service.AskAsync(new RagQuery
        {
            Question = "Que fait le chien ?", UserId = "user-1", Strategy = RagStrategy.Direct
        });

        // Qualité live préservée : la citation reste présente dans la réponse renvoyée.
        response.Citations.Should().ContainSingle(c => c.DocumentName == "TestDoc.pdf");
        // Persistance : le message assistant porte la citation avec SON propre MessageId (plus de Guid.Empty).
        var assistant = repo.Messages.Single(m => m.Role == MessageRole.Assistant);
        assistant.Citations.Should().ContainSingle(c => c.DocumentName == "TestDoc.pdf" && c.MessageId == assistant.Id);
    }

    [Fact]
    public async Task AskStreamAsync_ModeLlmDirect_EstampilleEtPersisteLEchange()
    {
        var repo = new FakeConversationRepository();
        var uow = new FakeUnitOfWork();
        var service = CreateDirectLlmService(repo, uow, new FakeChatClient("Réponse streamée"));

        RagResponse? final = null;
        await foreach (var chunk in service.AskStreamAsync(new RagQuery
        {
            Question = "Question streamée", UserId = "user-1", UseRag = false
        }))
        {
            if (chunk.IsDone)
                final = chunk.FinalResponse;
        }

        final.Should().NotBeNull();
        final!.SessionId.Should().NotBe(Guid.Empty);
        repo.Sessions.Should().ContainSingle();
        repo.Messages.Should().Contain(m => m.Role == MessageRole.User && m.Content == "Question streamée");
        repo.Messages.Should().Contain(m => m.Role == MessageRole.Assistant && m.Content == "Réponse streamée");
        uow.SaveChangesCallCount.Should().Be(1);
    }

    // --- Condensation de question multi-tour ---

    [Fact]
    public async Task AskAsync_ModeRag_AvecHistorique_CondenseLaQuestionPourLaRecuperationMaisPersisteLOriginale()
    {
        var chunk = DocumentChunk.Create(
            Guid.NewGuid(), "Les week-ends, le magasin ouvre à 10h.", chunkIndex: 0, embeddingDimensions: 768,
            pageNumber: 1, documentName: "Horaires.pdf");
        var repo = new FakeConversationRepository();
        var session = ConversationSession.Create("user-1", "Horaires");
        repo.Sessions.Add(session);
        repo.Messages.Add(ChatMessage.CreateUserMessage(session.Id, "Quels sont les horaires du magasin ?"));
        repo.Messages.Add(ChatMessage.CreateAssistantMessage(
            session.Id, "Le magasin ouvre à 9h en semaine.", 10, RagStrategy.Direct, 100));

        var embeddingService = new CapturingEmbeddingService();
        var service = new RagPipelineService(
            vectorStoreService: new FakeVectorStoreService(chunk), embeddingService: embeddingService,
            adaptiveQueryRouter: null!, hydeService: null!, multiQueryService: null!,
            rerankerService: null!, contextCompressorService: null!,
            queryCondensationService: new FakeQueryCondensationService("Quels sont les horaires du magasin le week-end ?"),
            conversationRepository: repo, unitOfWork: new FakeUnitOfWork(),
            options: Options.Create(new RagOptions()),
            chatClient: new FakeChatClient("Le magasin ouvre à 10h le week-end [SOURCE: Horaires.pdf, p.1]."),
            logger: NullLogger<RagPipelineService>.Instance);

        var response = await service.AskAsync(new RagQuery
        {
            Question = "et le week-end ?", UserId = "user-1", SessionId = session.Id, Strategy = RagStrategy.Direct
        });

        // La récupération (ici : l'embedding de la question) reçoit la question condensée...
        embeddingService.LastText.Should().Be("Quels sont les horaires du magasin le week-end ?");
        // ...mais le message persisté et la réponse restent liés à la question ORIGINALE de l'utilisateur.
        repo.Messages.Should().Contain(m => m.Role == MessageRole.User && m.Content == "et le week-end ?");
        response.Answer.Should().Contain("10h");
    }

    [Fact]
    public async Task AskAsync_PremierTourSansHistorique_NAppellePasLaCondensation()
    {
        var chunk = DocumentChunk.Create(
            Guid.NewGuid(), "Contenu de test.", chunkIndex: 0, embeddingDimensions: 768, documentName: "Doc.pdf");
        var repo = new FakeConversationRepository();
        var service = new RagPipelineService(
            vectorStoreService: new FakeVectorStoreService(chunk), embeddingService: new FakeEmbeddingService(),
            adaptiveQueryRouter: null!, hydeService: null!, multiQueryService: null!,
            rerankerService: null!, contextCompressorService: null!,
            queryCondensationService: new ThrowingQueryCondensationService(),
            conversationRepository: repo, unitOfWork: new FakeUnitOfWork(),
            options: Options.Create(new RagOptions()),
            chatClient: new FakeChatClient("Réponse [SOURCE: Doc.pdf]."),
            logger: NullLogger<RagPipelineService>.Instance);

        // Aucun SessionId fourni (premier tour) : la condensation ne doit jamais être invoquée,
        // même si le service configuré échouerait à l'appel.
        var act = () => service.AskAsync(new RagQuery
        {
            Question = "Bonjour", UserId = "user-1", Strategy = RagStrategy.Direct
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AskAsync_CondensationDesactiveeEnConfiguration_IgnoreLeServiceDeCondensation()
    {
        var chunk = DocumentChunk.Create(
            Guid.NewGuid(), "Contenu de test.", chunkIndex: 0, embeddingDimensions: 768, documentName: "Doc.pdf");
        var repo = new FakeConversationRepository();
        var session = ConversationSession.Create("user-1", "Session");
        repo.Sessions.Add(session);
        repo.Messages.Add(ChatMessage.CreateUserMessage(session.Id, "Première question"));
        repo.Messages.Add(ChatMessage.CreateAssistantMessage(session.Id, "Première réponse", 5, RagStrategy.Direct, 50));

        var service = new RagPipelineService(
            vectorStoreService: new FakeVectorStoreService(chunk), embeddingService: new FakeEmbeddingService(),
            adaptiveQueryRouter: null!, hydeService: null!, multiQueryService: null!,
            rerankerService: null!, contextCompressorService: null!,
            queryCondensationService: new ThrowingQueryCondensationService(),
            conversationRepository: repo, unitOfWork: new FakeUnitOfWork(),
            options: Options.Create(new RagOptions { Condensation = new CondensationOptions { Enabled = false } }),
            chatClient: new FakeChatClient("Réponse [SOURCE: Doc.pdf]."),
            logger: NullLogger<RagPipelineService>.Instance);

        var act = () => service.AskAsync(new RagQuery
        {
            Question = "et ensuite ?", UserId = "user-1", SessionId = session.Id, Strategy = RagStrategy.Direct
        });

        await act.Should().NotThrowAsync();
    }
}
