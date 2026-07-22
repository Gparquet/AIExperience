# Lot 3 — Historique de conversation réel — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Brancher l'historique de conversation multi-tour déjà à moitié câblé (persistance des sessions/messages/citations + résolution de session côté pipeline + endpoints de relecture + sidebar front), sans dégrader la qualité de recherche ni de citations de la réponse en direct.

**Architecture :** La résolution et la persistance de session sont centralisées aux **2 seuls points d'entrée publics** de `RagPipelineService` (`AskAsync`, `AskStreamAsync`), et non dispersées dans les 5 helpers privés comme l'envisageait le design : la session est créée/rechargée en tête, l'échange (message utilisateur + message assistant + citations) est persisté en un unique `SaveChangesAsync` en fin de traitement, quel que soit le mode (`classic`/`llm`/`rag`). La lecture d'historique déjà existante (`BuildChatHistoryAsync`, `AskDirectLlmAsync`, `StreamDirectLlmAsync`) reste inchangée : elle continue de lire `query.SessionId` — jamais muté — donc l'historique injecté au LLM exclut toujours le tour courant. Le front passe d'une colonne unique à `sidebar | conversation` avec une route `/chat/:sessionId?`.

**Tech Stack :** .NET 10 / EF Core 10 / Npgsql / ASP.NET Core 10 (back) ; React 19 + TypeScript + React Router v7 (front). Tests : xUnit + FluentAssertions, fakes écrits à la main (aucune bibliothèque de mocking dans ce projet).

## Global Constraints

- **Langue** : tout le code et tous les commentaires en **français**. Chaque classe/méthode/bloc non trivial reçoit un commentaire XML (`///` en C#) ou JSDoc/inline (`//`) en TS.
- **Entités** : setters privés, création via factory statique `Entity.Create(...)`, jamais de constructeur public à paramètres ni d'initialiseur d'objet pour les entités du Domain.
- **CQRS** : lectures = appel de service/repository direct (pas de MediatR). Les endpoints ajoutés ici sont des lectures → pas de commande.
- **Pas de relecture après écriture** : un contrôleur ne relit jamais le repository pour construire la réponse d'un endpoint d'écriture ; `RagResponse` porte déjà `SessionId`.
- **Ports** : back HTTP `50406`, PostgreSQL `5433`, front `5173`.
- **Base de données** : schéma géré par `Step3/scripts/init.sql`, **pas de migration EF Core**. La table `citations` existe déjà (`id, message_id, document_id, document_name, page_number, excerpt, score`).
- **Commits** : messages en français, sans mention ni signature de Claude (pas de `Co-Authored-By`).
- **Limitation assumée** (validée avec l'utilisateur) : `Citation.SectionTitle`, `ChunkIndex`, `StartTime`, `EndTime` sont `[NotMapped]`. Une conversation rechargée depuis l'historique affiche document/page/score/extrait mais **sans** section ni horodatage vidéo. Aucune migration de schéma dans ce lot pour combler cet écart.

---

## Structure des fichiers

**Back-end (`Step3/src/Back`)**
- Modifier `AIExperience.Rag.Domain/Models/RagResponse.cs` — ajout `Guid SessionId`.
- Modifier `AIExperience.Rag.Domain/Models/RagQuery.cs` — ajout `string UserId`.
- Créer `AIExperience.Rag.Domain/Models/ConversationSessionSummary.cs` — read-model léger pour la sidebar.
- Modifier `AIExperience.Rag.Domain/Interfaces/Repositories/IConversationRepository.cs` — ajout `GetSessionSummariesAsync`.
- Modifier `AIExperience.Rag.Infrastructure/Persistence/Repositories/ConversationRepository.cs` — impl `GetSessionSummariesAsync` + `ThenInclude(Citations)` dans `GetSessionByIdAsync`.
- Modifier `AIExperience.Rag.Infrastructure/Persistence/Configuration/CitationConfiguration.cs` — **mapper la colonne `score`** (correctif critique).
- Modifier `AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs` — injection `IUnitOfWork`, résolution + persistance de session (non-streaming et streaming).
- Modifier `AIExperience.Web.Api/DTOs/ChatDtos.cs` — `SessionId` dans requête/réponse + 3 nouveaux DTOs.
- Modifier `AIExperience.Web.Api/Controllers/ChatController.cs` — pass-through `SessionId`/`UserId` + 2 endpoints de relecture.
- Créer `AIExperience.Tests/RagPipelineServiceHistoryTests.cs` — tests session/persistance/dégradation gracieuse/citations.

**Front-end (`Step3/src/Front`)**
- Modifier `src/types/index.ts` — `sessionId` dans requête/réponse + types session.
- Modifier `src/api/client.ts` — `listSessions`, `getSession`.
- Modifier `src/App.tsx` — routes `/chat` et `/chat/:sessionId`.
- Créer `src/components/ChatSidebar.tsx` — liste des conversations + bouton « nouvelle conversation ».
- Modifier `src/pages/ChatPage.tsx` — chargement/reprise de session, navigation, layout sidebar.
- Modifier `src/index.css` — classes de layout `.chat-layout` / `.chat-sidebar` (ajout, pas de modification de l'existant).

---

### Task 1 : Domain — `RagResponse.SessionId` et `RagQuery.UserId`

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Domain/Models/RagResponse.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Domain/Models/RagQuery.cs`

**Interfaces:**
- Produces : `RagResponse.SessionId` (`Guid`, `init`) ; `RagQuery.UserId` (`string`, `init`, défaut `""`). Consommés par les tâches 4, 5, 6.

- [ ] **Step 1 : Ajouter `SessionId` à `RagResponse`**

Dans `RagResponse.cs`, ajouter après la propriété `DurationMs` :

```csharp
    /// <summary>
    /// Identifiant de la session de conversation à laquelle appartient cet échange.
    /// Toujours renseigné par le pipeline : nouvelle session créée si la requête n'en portait pas,
    /// ou session existante réutilisée. Permet au front de rattacher la réponse à un fil de discussion.
    /// </summary>
    public Guid SessionId { get; init; }
```

- [ ] **Step 2 : Ajouter `UserId` à `RagQuery`**

Dans `RagQuery.cs`, ajouter après la propriété `MaxHistoryTurns` :

```csharp
    /// <summary>
    /// Propriétaire de la session, transmis par la couche de composition (contrôleur) depuis
    /// <c>DevAuthOptions.DefaultUserId</c> tant que l'authentification n'est pas implémentée.
    /// Utilisé à la création d'une nouvelle session de conversation.
    /// </summary>
    public string UserId { get; init; } = string.Empty;
```

- [ ] **Step 3 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: build réussi (aucun appelant existant cassé — ce sont des ajouts de propriétés optionnelles).

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Domain/Models/RagResponse.cs" "Step3/src/Back/AIExperience.Rag.Domain/Models/RagQuery.cs"
git commit -m "feat(rag): porte SessionId dans RagResponse et UserId dans RagQuery"
```

---

### Task 2 : Infrastructure — Correctif critique du mapping EF de `Citation.Score`

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/CitationConfiguration.cs`

**Contexte (pourquoi c'est critique) :** la table `citations` (init.sql) possède une colonne `score DOUBLE PRECISION`, mais `CitationConfiguration` ne mappe **pas** la propriété `Citation.Score`. Par convention, EF Core (Npgsql) génère alors un identifiant `"Score"` (avec guillemets, casse préservée), qui ne correspond pas à la colonne `score`. Comme les citations ne sont **jamais** persistées aujourd'hui (`AddMessageAsync` n'est appelé nulle part), ce bug est resté dormant. Dès que le Lot 3 persistera les citations (tâches 4/5), l'INSERT échouerait ; le `try/catch` de dégradation gracieuse l'avalerait → **toutes les citations disparaîtraient silencieusement de la base**, sans erreur visible. C'est exactement le risque « perte de qualité de citations » à écarter.

**Interfaces:** aucune signature publique modifiée — correction interne de mapping.

- [ ] **Step 1 : Mapper la colonne `score`**

Dans `CitationConfiguration.Configure`, ajouter après la ligne `builder.Property(c => c.PageNumber).HasColumnName("page_number");` :

```csharp
        // La colonne score existe dans init.sql mais n'était pas mappée : sans cette ligne, EF génère
        // l'identifiant "Score" (casse préservée, entre guillemets) qui ne correspond pas à la colonne
        // "score" — l'INSERT des citations échouerait silencieusement une fois la persistance branchée.
        builder.Property(c => c.Score).HasColumnName("score");
```

- [ ] **Step 2 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: build réussi.

- [ ] **Step 3 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/CitationConfiguration.cs"
git commit -m "fix(persistence): mappe la colonne score des citations pour la persistance de l'historique"
```

> **Vérification** : ce correctif ne se prouve qu'à l'INSERT réel contre PostgreSQL (un test de métadonnées EF exigerait d'ajouter un provider Npgsql + config pgvector au projet de tests — hors périmètre). Il est couvert par la vérification manuelle e2e finale (poser une question RAG, recharger la session, confirmer que les citations réapparaissent avec leur score).

---

### Task 3 : Domain + Repository — Résumés de session et chargement des citations

**Files:**
- Create: `Step3/src/Back/AIExperience.Rag.Domain/Models/ConversationSessionSummary.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IConversationRepository.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/ConversationRepository.cs`

**Interfaces:**
- Produces : `ConversationSessionSummary(Guid Id, string Title, DateTimeOffset UpdatedAt, int MessageCount)` ; `IConversationRepository.GetSessionSummariesAsync(string userId, CancellationToken) : Task<IReadOnlyList<ConversationSessionSummary>>`. `GetSessionByIdAsync` charge désormais aussi les citations de chaque message. Consommés par les tâches 4 (fake) et 6 (endpoints).

- [ ] **Step 1 : Créer le read-model `ConversationSessionSummary`**

Créer `ConversationSessionSummary.cs` :

```csharp
namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Projection légère d'une session de conversation pour l'affichage de la liste (sidebar).
/// Évite de charger tous les messages d'une session juste pour peupler la liste.
/// </summary>
/// <param name="Id">Identifiant de la session.</param>
/// <param name="Title">Titre généré depuis la première question.</param>
/// <param name="UpdatedAt">Date de dernière activité (UTC), sert au tri anté-chronologique.</param>
/// <param name="MessageCount">Nombre de messages dans la session.</param>
public sealed record ConversationSessionSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int MessageCount);
```

- [ ] **Step 2 : Déclarer `GetSessionSummariesAsync` dans l'interface**

Dans `IConversationRepository.cs`, ajouter le `using AIExperience.Rag.Domain.Models;` en tête si absent, puis ajouter la méthode :

```csharp
    /// <summary>Récupère les résumés des sessions d'un utilisateur (sans charger les messages), triés par activité récente.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<IReadOnlyList<ConversationSessionSummary>> GetSessionSummariesAsync(string userId, CancellationToken ct = default);
```

- [ ] **Step 3 : Implémenter dans le repository et charger les citations**

Dans `ConversationRepository.cs` :

1. Ajouter `using AIExperience.Rag.Domain.Models;` en tête.
2. Remplacer `GetSessionByIdAsync` pour charger aussi les citations de chaque message (nécessaire à l'endpoint de détail) :

```csharp
    /// <inheritdoc/>
    public async Task<ConversationSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default)
        => await context.ConversationSessions
            .Include(s => s.Messages)
                .ThenInclude(m => m.Citations)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
```

3. Ajouter l'implémentation de `GetSessionSummariesAsync` :

```csharp
    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConversationSessionSummary>> GetSessionSummariesAsync(string userId, CancellationToken ct = default)
        => await context.ConversationSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            // Projection SQL directe : le COUNT des messages est calculé côté base, sans matérialiser les messages.
            .Select(s => new ConversationSessionSummary(s.Id, s.Title, s.UpdatedAt, s.Messages.Count))
            .ToListAsync(ct);
```

- [ ] **Step 4 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: build réussi.

- [ ] **Step 5 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Domain/Models/ConversationSessionSummary.cs" "Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IConversationRepository.cs" "Step3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/ConversationRepository.cs"
git commit -m "feat(conversation): résumés de session pour la sidebar et chargement des citations au détail"
```

---

### Task 4 : Infrastructure — Résolution et persistance de session (mode non-streaming)

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs`
- Test: `Step3/src/Back/AIExperience.Tests/RagPipelineServiceHistoryTests.cs`

**Interfaces:**
- Consumes : `IUnitOfWork` (déjà enregistré en DI, `AppDbContext`), `IConversationRepository` (déjà injecté), `RagQuery.UserId`, `RagResponse.SessionId` (Task 1).
- Produces : `AskAsync` renvoie désormais une `RagResponse` avec `SessionId` renseigné et persiste l'échange. Nouveaux helpers privés `ResolveSessionAsync`, `PersistExchangeAsync`, `FinalizeExchangeAsync`, `RunRagPipelineAsync`, `BuildSessionTitle` — consommés aussi par la Task 5.

- [ ] **Step 1 : Écrire les tests (ils doivent échouer à la compilation puis au run)**

Créer `RagPipelineServiceHistoryTests.cs` :

```csharp
using System.Runtime.CompilerServices;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.AI.Rag;
using AIExperience.Rag.Infrastructure.Options;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
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

    /// <summary>Construit un service en mode "LLM direct" : seules la session et le LLM sont exercés, le reste est inutile (null!).</summary>
    private static RagPipelineService CreateDirectLlmService(
        FakeConversationRepository repo, FakeUnitOfWork uow, FakeChatClient chat)
        => new(
            vectorStoreService: null!, embeddingService: null!, adaptiveQueryRouter: null!,
            hydeService: null!, multiQueryService: null!, rerankerService: null!, contextCompressorService: null!,
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
}
```

- [ ] **Step 2 : Lancer les tests — ils doivent échouer**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter "FullyQualifiedName~RagPipelineServiceHistoryTests"`
Expected: échec de **compilation** (le constructeur de `RagPipelineService` n'a pas encore le paramètre `IUnitOfWork`, `AskAsync` ne persiste pas encore). C'est l'échec attendu de la phase rouge.

- [ ] **Step 3 : Injecter `IUnitOfWork` et ajouter les helpers de session**

Dans `RagPipelineService.cs` :

1. Ajouter `using AIExperience.Rag.Domain.Interfaces.Services;` en tête (pour `IUnitOfWork`) s'il n'y est pas déjà — vérifier la liste des `using`.
2. Ajouter le paramètre `IUnitOfWork unitOfWork` au constructeur primaire, **juste après** `IConversationRepository conversationRepository,` :

```csharp
        IConversationRepository conversationRepository,
        IUnitOfWork unitOfWork,
        IOptions<RagOptions> options,
```

3. Ajouter ces helpers privés à la fin de la classe (avant l'accolade fermante) :

```csharp
        /// <summary>
        /// Résout la session de la requête : recharge et réactive (<see cref="ConversationSession.Touch"/>)
        /// une session existante, ou en crée une neuve (titre = première question tronquée) si aucun id
        /// valide n'est fourni. Le RagQuery n'est jamais muté : la lecture d'historique en aval continue
        /// de se baser sur <c>query.SessionId</c>, donc n'inclut jamais le tour courant.
        /// </summary>
        private async Task<Guid> ResolveSessionAsync(RagQuery query, CancellationToken ct)
        {
            if (query.SessionId != Guid.Empty)
            {
                var existing = await conversationRepository.GetSessionByIdAsync(query.SessionId, ct);
                if (existing is not null)
                {
                    // Marque la session active ; l'UpdatedAt sera persisté avec le reste de l'échange.
                    existing.Touch();
                    return existing.Id;
                }
                // Id fourni mais introuvable (session supprimée, id forgé) : on repart sur une session neuve
                // plutôt que d'échouer la requête.
            }

            var session = ConversationSession.Create(query.UserId, BuildSessionTitle(query.Question));
            await conversationRepository.AddSessionAsync(session, ct);
            return session.Id;
        }

        /// <summary>Génère un titre de session depuis la première question (tronquée à ~60 caractères).</summary>
        private static string BuildSessionTitle(string question)
        {
            var trimmed = question.Trim();
            return trimmed.Length <= 60 ? trimmed : trimmed[..60] + "…";
        }

        /// <summary>
        /// Persiste l'échange complet (message utilisateur + message assistant + citations) en un seul
        /// <see cref="IUnitOfWork.SaveChangesAsync"/>. Les citations sont rattachées via la collection de
        /// navigation du message assistant : EF insère le graphe avec le bon MessageId. Entouré d'un
        /// try/catch : un échec de persistance est logué en Warning mais ne fait jamais échouer la réponse
        /// déjà produite et servie à l'utilisateur (dégradation gracieuse, cohérente avec le reste du pipeline).
        /// </summary>
        private async Task PersistExchangeAsync(Guid sessionId, string question, RagResponse response, CancellationToken ct)
        {
            try
            {
                var userMessage = ChatMessage.CreateUserMessage(sessionId, question);
                await conversationRepository.AddMessageAsync(userMessage, ct);

                var assistantMessage = ChatMessage.CreateAssistantMessage(
                    sessionId, response.Answer, response.TotalTokens, response.StrategyUsed, response.DurationMs);

                // Seuls les champs mappés sont persistés (document/nom/page/extrait/score) ; section et
                // horodatages vidéo restent [NotMapped] — limitation assumée pour ce lot.
                foreach (var citation in response.Citations)
                {
                    assistantMessage.Citations.Add(Citation.Create(
                        assistantMessage.Id,
                        citation.DocumentId,
                        citation.DocumentName,
                        citation.Excerpt,
                        citation.Score,
                        citation.PageNumber));
                }

                await conversationRepository.AddMessageAsync(assistantMessage, ct);
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Échec de persistance de l'échange pour la session {SessionId} — la réponse reste servie à l'utilisateur.",
                    sessionId);
            }
        }

        /// <summary>
        /// Estampille la réponse avec le SessionId résolu puis persiste l'échange. Renvoie la réponse
        /// estampillée. Point de finalisation commun aux branches streaming (où le SessionId doit être
        /// posé sur la réponse portée par l'événement "done").
        /// </summary>
        private async Task<RagResponse> FinalizeExchangeAsync(Guid sessionId, string question, RagResponse response, CancellationToken ct)
        {
            var stamped = response with { SessionId = sessionId };
            await PersistExchangeAsync(sessionId, question, stamped, ct);
            return stamped;
        }
```

- [ ] **Step 4 : Extraire le corps RAG de `AskAsync` dans `RunRagPipelineAsync`**

Dans `RagPipelineService.cs`, remplacer intégralement la méthode `AskAsync` (actuellement lignes ~62-141) par ces **deux** méthodes. `RunRagPipelineAsync` reprend **verbatim** le corps RAG existant (étapes 1→6, garde-fou contexte vide inclus) — aucune modification de la logique de récupération/citations, seule sa localisation change :

```csharp
        /// <inheritdoc/>
        public async Task<RagResponse> AskAsync(RagQuery query, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var ragOptions = options.Value;

            // Résolution de la session AVANT toute lecture d'historique : pour une session existante,
            // GetMessagesAsync (en aval) lit l'historique déjà en base, sans le tour courant.
            var sessionId = await ResolveSessionAsync(query, ct);

            // Dispatch selon le mode, chaque branche produisant une RagResponse.
            RagResponse response =
                !query.UseLlm ? await AskFullTextAsync(query, ragOptions, sw, ct)
                : !query.UseRag ? await AskDirectLlmAsync(query, sw, ct)
                : await RunRagPipelineAsync(query, ragOptions, sw, ct);

            // Le SessionId résolu remonte jusqu'au contrôleur ; l'échange est persisté quel que soit le mode.
            response = response with { SessionId = sessionId };
            await PersistExchangeAsync(sessionId, query.Question, response, ct);
            return response;
        }

        /// <summary>
        /// Cœur du pipeline RAG complet (récupération → reranking → compression → LLM → citations).
        /// Extrait de <see cref="AskAsync"/> pour permettre à celui-ci d'orchestrer la session autour du mode.
        /// </summary>
        private async Task<RagResponse> RunRagPipelineAsync(RagQuery query, RagOptions ragOptions, Stopwatch sw, CancellationToken ct)
        {
            // 1. Résolution de la stratégie (routage Adaptive + repli si HyDE/Fusion désactivés)
            var strategy = await ResolveStrategyAsync(query, ragOptions, ct);

            // 2. Récupération des chunks selon la stratégie résolue
            var rankedChunks = await RetrieveChunksAsync(query, strategy, ragOptions, ct);

            // 3. Reclassement par pertinence réelle — corrige les faux positifs du cosinus
            if (ragOptions.Reranker.Enabled && rankedChunks.Count > 0)
                rankedChunks = await rerankerService.RerankAsync(
                    query.Question, rankedChunks, ragOptions.Reranker.TopKAfterRerank, ct);

            // 4. Compression du contexte — réduit les tokens envoyés au LLM en conservant les phrases pertinentes.
            IReadOnlyList<DocumentChunk> contextChunks;
            if (ragOptions.ContextCompression.Enabled && rankedChunks.Count > 0)
            {
                var compressed = await contextCompressorService.CompressAsync(
                    query.Question, rankedChunks.Select(r => r.Chunk), ct);
                contextChunks = compressed.Count > 0 ? compressed : rankedChunks.Select(r => r.Chunk).ToList();
            }
            else
            {
                contextChunks = rankedChunks.Select(r => r.Chunk).ToList();
            }

            // R-4 : Garde-fou contexte vide — court-circuite l'appel LLM pour éviter les hallucinations.
            if (contextChunks.Count == 0)
            {
                sw.Stop();
                return new RagResponse
                {
                    Answer = "Aucun document pertinent n'a été trouvé pour répondre à cette question.",
                    Citations = [],
                    StrategyUsed = strategy,
                    TotalTokens = 0,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            // 5. Construction du prompt et appel au LLM
            var chatHistory = await BuildChatHistoryAsync(query, contextChunks, ct);
            var completionResult = await chatClient.GetResponseAsync(
                chatHistory.Select(m => new Microsoft.Extensions.AI.ChatMessage(
                    m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)).ToList(),
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.1f },
                ct);

            sw.Stop();

            // 6. Construction puis filtrage des citations (documents réellement référencés dans la réponse).
            var answer = string.Join("\n", completionResult.Messages.Select(c => c.Text)) ?? string.Empty;
            var citations = FilterCitationsByAnswer(BuildCitations(contextChunks, rankedChunks), answer);

            return new RagResponse
            {
                Answer = answer,
                Citations = citations,
                StrategyUsed = strategy,
                TotalTokens = (int)(completionResult.Usage?.TotalTokenCount ?? 0),
                DurationMs = sw.ElapsedMilliseconds
            };
        }
```

- [ ] **Step 5 : Enregistrer le nouveau paramètre DI (vérification)**

`IUnitOfWork` et `RagPipelineService` sont déjà enregistrés (`DependencyInjection.cs:118` et `:256`). Aucun changement de DI requis — le conteneur résout `IUnitOfWork` par type. Vérifier simplement qu'aucune autre construction manuelle de `RagPipelineService` n'existe :

Run: `grep -rn "new RagPipelineService" "Step3/src/Back/AIExperience.Rag.Infrastructure" "Step3/src/Back/AIExperience.Web.Api" "Step3/src/Back/AIExperience.App.Console"`
Expected: aucun résultat (uniquement la construction par DI).

- [ ] **Step 6 : Lancer les tests non-streaming — ils doivent passer**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter "FullyQualifiedName~RagPipelineServiceHistoryTests"`
Expected: les 4 tests PASSENT.

- [ ] **Step 7 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs" "Step3/src/Back/AIExperience.Tests/RagPipelineServiceHistoryTests.cs"
git commit -m "feat(rag): résolution et persistance de session dans le pipeline (mode non-streaming)"
```

---

### Task 5 : Infrastructure — Session et persistance en mode streaming

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs`
- Test: `Step3/src/Back/AIExperience.Tests/RagPipelineServiceHistoryTests.cs` (ajout d'un test)

**Interfaces:**
- Consumes : helpers `ResolveSessionAsync`, `FinalizeExchangeAsync` (Task 4).
- Produces : `AskStreamAsync` résout la session en tête et finalise (SessionId + persistance) sur chaque événement `done` ; `StreamDirectLlmAsync` reçoit le `sessionId` résolu.

- [ ] **Step 1 : Écrire le test streaming (rouge)**

Ajouter cette méthode à la classe `RagPipelineServiceHistoryTests` :

```csharp
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
```

- [ ] **Step 2 : Lancer le test — il doit échouer**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter "FullyQualifiedName~AskStreamAsync_ModeLlmDirect"`
Expected: échec (le `done` streaming ne porte pas encore de `SessionId` et rien n'est persisté).

- [ ] **Step 3 : Câbler la session dans `AskStreamAsync`**

Dans `RagPipelineService.cs`, dans `AskStreamAsync` :

1. Juste après `var ragOptions = options.Value;`, ajouter la résolution de session :

```csharp
            // Résolution de la session en tête, avant toute lecture d'historique (comme en non-streaming).
            var sessionId = await ResolveSessionAsync(query, ct);
```

2. Branche full-text — remplacer :

```csharp
            if (!query.UseLlm)
            {
                var response = await AskFullTextAsync(query, ragOptions, sw, ct);
                yield return new RagStreamChunk { IsDone = true, FinalResponse = response };
                yield break;
            }
```

par :

```csharp
            if (!query.UseLlm)
            {
                var response = await AskFullTextAsync(query, ragOptions, sw, ct);
                yield return new RagStreamChunk
                {
                    IsDone = true,
                    FinalResponse = await FinalizeExchangeAsync(sessionId, query.Question, response, ct)
                };
                yield break;
            }
```

3. Branche LLM direct — remplacer l'appel `StreamDirectLlmAsync(query, sw, ct)` par `StreamDirectLlmAsync(query, sessionId, sw, ct)` :

```csharp
            if (!query.UseRag)
            {
                await foreach (var chunk in StreamDirectLlmAsync(query, sessionId, sw, ct))
                    yield return chunk;
                yield break;
            }
```

4. Garde-fou contexte vide (streaming) — remplacer le bloc `yield return new RagStreamChunk { IsDone = true, FinalResponse = new RagResponse { Answer = "Aucun document pertinent...", ... } };` par une finalisation :

```csharp
            if (contextChunks.Count == 0)
            {
                sw.Stop();
                var emptyResponse = new RagResponse
                {
                    Answer = "Aucun document pertinent n'a été trouvé pour répondre à cette question.",
                    Citations = [],
                    StrategyUsed = strategy,
                    TotalTokens = 0,
                    DurationMs = sw.ElapsedMilliseconds
                };
                yield return new RagStreamChunk
                {
                    IsDone = true,
                    FinalResponse = await FinalizeExchangeAsync(sessionId, query.Question, emptyResponse, ct)
                };
                yield break;
            }
```

5. Événement `done` final du mode RAG streaming — remplacer :

```csharp
            yield return new RagStreamChunk
            {
                IsDone = true,
                FinalResponse = new RagResponse
                {
                    Answer = streamAnswer,
                    Citations = citations,
                    StrategyUsed = strategy,
                    TotalTokens = 0,
                    DurationMs = sw.ElapsedMilliseconds
                }
            };
```

par :

```csharp
            var finalResponse = new RagResponse
            {
                Answer = streamAnswer,
                Citations = citations,
                StrategyUsed = strategy,
                TotalTokens = 0,
                DurationMs = sw.ElapsedMilliseconds
            };
            yield return new RagStreamChunk
            {
                IsDone = true,
                FinalResponse = await FinalizeExchangeAsync(sessionId, query.Question, finalResponse, ct)
            };
```

- [ ] **Step 4 : Faire finaliser `StreamDirectLlmAsync`**

Modifier la signature de `StreamDirectLlmAsync` pour recevoir le `sessionId` et remplacer son événement `done` par une finalisation :

```csharp
        private async IAsyncEnumerable<RagStreamChunk> StreamDirectLlmAsync(
            RagQuery query, Guid sessionId, Stopwatch sw, [EnumeratorCancellation] CancellationToken ct)
        {
```

et, à la fin de la méthode, remplacer le bloc `yield return new RagStreamChunk { IsDone = true, FinalResponse = new RagResponse { ... StrategyUsed = RagStrategy.DirectLlm ... } };` par :

```csharp
            sw.Stop();
            var finalResponse = new RagResponse
            {
                Answer = totalText.ToString(),
                Citations = [],
                StrategyUsed = RagStrategy.DirectLlm,
                TotalTokens = 0,
                DurationMs = sw.ElapsedMilliseconds
            };
            yield return new RagStreamChunk
            {
                IsDone = true,
                FinalResponse = await FinalizeExchangeAsync(sessionId, query.Question, finalResponse, ct)
            };
```

> Note : `AskDirectLlmAsync` (non-streaming) reste inchangé — sa persistance est assurée par la queue de `AskAsync` (Task 4). Seule la variante streaming persiste elle-même, car son événement `done` est produit à l'intérieur de l'itérateur.

- [ ] **Step 5 : Lancer toute la suite d'historique**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter "FullyQualifiedName~RagPipelineServiceHistoryTests"`
Expected: les 5 tests PASSENT.

- [ ] **Step 6 : Compiler la solution entière**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: build réussi.

- [ ] **Step 7 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs" "Step3/src/Back/AIExperience.Tests/RagPipelineServiceHistoryTests.cs"
git commit -m "feat(rag): session et persistance de l'historique en mode streaming"
```

---

### Task 6 : Web.Api — Pass-through `SessionId` et endpoints de relecture

**Files:**
- Modify: `Step3/src/Back/AIExperience.Web.Api/DTOs/ChatDtos.cs`
- Modify: `Step3/src/Back/AIExperience.Web.Api/Controllers/ChatController.cs`

**Interfaces:**
- Consumes : `RagResponse.SessionId`, `RagQuery.UserId`/`SessionId`, `IConversationRepository.GetSessionSummariesAsync` / `GetSessionByIdAsync`, `IOptions<DevAuthOptions>`.
- Produces (API HTTP) : `AskQuestionRequest.SessionId`, `AskQuestionResponse.SessionId`, `GET /api/chat/sessions`, `GET /api/chat/sessions/{id}`. Consommés par le front (Task 7/8).

- [ ] **Step 1 : Étendre les DTOs**

Dans `ChatDtos.cs` :

1. Ajouter `using System;` en tête si nécessaire (pour `Guid`/`DateTimeOffset` — probablement déjà couvert par les usings implicites).
2. Ajouter le paramètre `SessionId` **en dernière position** de `AskQuestionRequest` (record positionnel : les paramètres optionnels doivent rester en fin) :

```csharp
    string? SystemPrompt = null,
    /// <summary>Session de conversation à poursuivre. Null/absent = nouvelle conversation.</summary>
    Guid? SessionId = null);
```

3. Ajouter `SessionId` à `AskQuestionResponse` :

```csharp
public record AskQuestionResponse(
    string Answer,
    List<CitationResponse> Citations,
    string StrategyUsed,
    int TotalTokens,
    long DurationMs,
    /// <summary>Session à laquelle appartient cet échange (nouvelle ou réutilisée).</summary>
    Guid SessionId);
```

4. Ajouter les 3 nouveaux DTOs de lecture à la fin du fichier :

```csharp
/// <summary>Résumé d'une session pour la liste latérale (sidebar).</summary>
public record ChatSessionSummaryResponse(Guid Id, string Title, DateTimeOffset UpdatedAt, int MessageCount);

/// <summary>Un message d'une session, tel que rechargé depuis l'historique.</summary>
public record ChatMessageResponse(
    string Role,
    string Content,
    List<CitationResponse>? Citations,
    string? StrategyUsed,
    int TokensUsed,
    long DurationMs,
    DateTimeOffset CreatedAt);

/// <summary>Détail complet d'une session : titre + messages ordonnés.</summary>
public record ChatSessionDetailResponse(Guid Id, string Title, List<ChatMessageResponse> Messages);
```

- [ ] **Step 2 : Injecter les dépendances de lecture dans le contrôleur**

Dans `ChatController.cs`, ajouter les usings et étendre le constructeur primaire :

```csharp
using AIExperience.Rag.Application.Common;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Options;
```

```csharp
public class ChatController(
    IRagPipelineService ragPipelineService,
    IConversationRepository conversationRepository,
    IOptions<DevAuthOptions> devAuthOptions) : ControllerBase
```

- [ ] **Step 3 : Passer `SessionId` et `UserId` dans les deux `RagQuery`**

Dans `Ask`, ajouter les deux propriétés à l'initialisation du `RagQuery` :

```csharp
        var ragResponse = await ragPipelineService.AskAsync(new RagQuery
        {
            Question = request.Question,
            DocumentIds = request.DocumentIds,
            Strategy = request.Strategy,
            UseLlm = request.UseLlm,
            UseRag = request.UseRag,
            SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt,
            SessionId = request.SessionId ?? Guid.Empty,
            UserId = devAuthOptions.Value.DefaultUserId
        }, cancellationToken);
```

Faire de même dans `AskStream` pour le `RagQuery` passé à `AskStreamAsync` (ajouter les deux mêmes lignes `SessionId` / `UserId`).

- [ ] **Step 4 : Renvoyer `SessionId` dans les réponses `ask` et `stream`**

Dans `Ask`, compléter le `AskQuestionResponse` renvoyé :

```csharp
        return Ok(new AskQuestionResponse(
            ragResponse.Answer,
            citations,
            ragResponse.StrategyUsed.ToString(),
            ragResponse.TotalTokens,
            ragResponse.DurationMs,
            ragResponse.SessionId));
```

Dans `BuildResponse` (utilisé par le streaming), compléter de même :

```csharp
        return new AskQuestionResponse(r.Answer, citations, r.StrategyUsed.ToString(), r.TotalTokens, r.DurationMs, r.SessionId);
```

- [ ] **Step 5 : Ajouter les deux endpoints de relecture**

Ajouter dans `ChatController` (par exemple après `GetSystemPrompts`) :

```csharp
    /// <summary>Liste les conversations de l'utilisateur courant, triées par activité récente (pour la sidebar).</summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IEnumerable<ChatSessionSummaryResponse>>> GetSessions(CancellationToken cancellationToken)
    {
        var summaries = await conversationRepository.GetSessionSummariesAsync(
            devAuthOptions.Value.DefaultUserId, cancellationToken);

        return Ok(summaries.Select(s => new ChatSessionSummaryResponse(s.Id, s.Title, s.UpdatedAt, s.MessageCount)));
    }

    /// <summary>Recharge le détail d'une conversation (titre + messages + citations). 404 si absente ou d'un autre utilisateur.</summary>
    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<ChatSessionDetailResponse>> GetSession(Guid id, CancellationToken cancellationToken)
    {
        var session = await conversationRepository.GetSessionByIdAsync(id, cancellationToken);

        // Isolation par utilisateur : une session inexistante OU appartenant à un autre utilisateur renvoie 404.
        if (session is null || session.UserId != devAuthOptions.Value.DefaultUserId)
            return NotFound();

        var messages = session.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageResponse(
                m.Role.ToString(),
                m.Content,
                // Citations rechargées depuis la base : document/page/extrait/score présents ;
                // section et horodatages vidéo absents (propriétés [NotMapped], limitation assumée du lot).
                m.Citations.Count == 0
                    ? null
                    : m.Citations
                        .Select(c => new CitationResponse(
                            c.DocumentName, c.PageNumber, c.Excerpt, c.Score,
                            c.SectionTitle, c.ChunkIndex,
                            c.StartTime?.TotalSeconds, c.EndTime?.TotalSeconds))
                        .ToList(),
                m.StrategyUsed?.ToString(),
                m.TokensUsed,
                m.DurationMs,
                m.CreatedAt))
            .ToList();

        return Ok(new ChatSessionDetailResponse(session.Id, session.Title, messages));
    }
```

- [ ] **Step 6 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: build réussi.

- [ ] **Step 7 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Web.Api/DTOs/ChatDtos.cs" "Step3/src/Back/AIExperience.Web.Api/Controllers/ChatController.cs"
git commit -m "feat(api): SessionId dans ask/stream et endpoints de relecture des conversations"
```

---

### Task 7 : Front — Types et client API

**Files:**
- Modify: `Step3/src/Front/src/types/index.ts`
- Modify: `Step3/src/Front/src/api/client.ts`

**Interfaces:**
- Consumes (HTTP) : endpoints Task 6.
- Produces (TS) : `AskQuestionRequest.sessionId`, `AskQuestionResponse.sessionId`, `ChatSessionSummary`, `ChatMessageDetail`, `ChatSessionDetail`, `api.chat.listSessions()`, `api.chat.getSession(id)`. Consommés par la Task 8.

- [ ] **Step 1 : Étendre les types**

Dans `types/index.ts` :

1. Ajouter `sessionId?: string;` à `AskQuestionRequest` (après `systemPrompt`) :

```typescript
  /** Prompt système personnalisé. Si absent, le back-end utilise le prompt par défaut. */
  systemPrompt?: string;
  /** Session de conversation à poursuivre. Absent = nouvelle conversation. */
  sessionId?: string;
}
```

2. Ajouter `sessionId: string;` à `AskQuestionResponse` :

```typescript
export interface AskQuestionResponse {
  answer: string;
  citations: CitationResponse[];
  strategyUsed: string;
  totalTokens: number;
  durationMs: number;
  /** Session à laquelle appartient cet échange (nouvelle ou réutilisée). */
  sessionId: string;
}
```

3. Ajouter les types de session à la fin du fichier :

```typescript
/** Résumé d'une conversation pour la liste latérale. */
export interface ChatSessionSummary {
  id: string;
  title: string;
  updatedAt: string;
  messageCount: number;
}

/** Un message d'une conversation rechargée depuis l'historique. */
export interface ChatMessageDetail {
  role: string;
  content: string;
  citations?: CitationResponse[] | null;
  strategyUsed?: string | null;
  tokensUsed: number;
  durationMs: number;
  createdAt: string;
}

/** Détail complet d'une conversation. */
export interface ChatSessionDetail {
  id: string;
  title: string;
  messages: ChatMessageDetail[];
}
```

- [ ] **Step 2 : Ajouter les appels client**

Dans `client.ts` :

1. Compléter l'import de types en tête :

```typescript
import type { AskQuestionRequest, AskQuestionResponse, ChatSessionDetail, ChatSessionSummary, CheckDuplicateResponse, DocumentResponse, StreamEvent, SystemPromptsResponse, VideoTranscriptionResponse } from '../types';
```

2. Dans le namespace `chat`, ajouter les deux méthodes de lecture (après `getSystemPrompts`) :

```typescript
    // Liste des conversations de l'utilisateur courant (sidebar), triées par activité récente côté back-end.
    listSessions: () => request<ChatSessionSummary[]>('/api/chat/sessions'),

    // Détail d'une conversation (titre + messages + citations) pour reprise depuis l'historique.
    getSession: (id: string) => request<ChatSessionDetail>(`/api/chat/sessions/${id}`),
```

> Le champ `sessionId` de la requête est déjà transmis : `ask` et `askStream` sérialisent l'objet `payload` entier (`JSON.stringify(payload)`), donc `payload.sessionId` part automatiquement dès que l'appelant le fournit.

- [ ] **Step 3 : Vérifier le build front**

Run: `cd "Step3/src/Front" && npm run build`
Expected: build TypeScript réussi (aucune erreur de type).

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Front/src/types/index.ts" "Step3/src/Front/src/api/client.ts"
git commit -m "feat(front): types et appels client pour les sessions de conversation"
```

---

### Task 8 : Front — Route, sidebar et reprise de conversation

**Files:**
- Modify: `Step3/src/Front/src/App.tsx`
- Create: `Step3/src/Front/src/components/ChatSidebar.tsx`
- Modify: `Step3/src/Front/src/pages/ChatPage.tsx`
- Modify: `Step3/src/Front/src/index.css`

**Interfaces:**
- Consumes : `api.chat.listSessions()`, `api.chat.getSession(id)`, `AskQuestionResponse.sessionId` (Task 7).

- [ ] **Step 1 : Ajouter la route paramétrée**

Dans `App.tsx`, remplacer la ligne `<Route path="/chat" element={<ChatPage />} />` par deux routes (la variante sans id = nouvelle conversation) :

```tsx
              <Route path="/chat" element={<ChatPage />} />
              <Route path="/chat/:sessionId" element={<ChatPage />} />
```

- [ ] **Step 2 : Créer le composant `ChatSidebar`**

Créer `src/components/ChatSidebar.tsx` :

```tsx
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { ChatSessionSummary } from '../types';

/**
 * Liste latérale des conversations. Se recharge au montage et à chaque incrément de `reloadSignal`
 * (déclenché par ChatPage après un nouvel échange, pour remonter la conversation active en tête).
 */
interface ChatSidebarProps {
  /** Id de la conversation actuellement ouverte (surlignée), ou undefined pour une conversation vierge. */
  activeSessionId?: string;
  /** Compteur : toute incrémentation force un rechargement de la liste. */
  reloadSignal: number;
}

export default function ChatSidebar({ activeSessionId, reloadSignal }: ChatSidebarProps) {
  const navigate = useNavigate();
  const [sessions, setSessions] = useState<ChatSessionSummary[]>([]);

  // Rechargement de la liste au montage puis à chaque nouvel échange (reloadSignal).
  useEffect(() => {
    api.chat.listSessions()
      .then(setSessions)
      .catch(() => { /* la sidebar reste vide si l'API est indisponible — non bloquant */ });
  }, [reloadSignal]);

  return (
    <aside className="chat-sidebar">
      {/* Démarre une conversation vierge : l'URL /chat (sans id) réinitialise l'état de ChatPage. */}
      <button className="btn btn-primary chat-sidebar-new" onClick={() => navigate('/chat')}>
        + Nouvelle conversation
      </button>

      <ul className="chat-sidebar-list">
        {sessions.map(s => (
          <li key={s.id}>
            <button
              className={`chat-sidebar-item ${s.id === activeSessionId ? 'chat-sidebar-item-active' : ''}`}
              onClick={() => navigate(`/chat/${s.id}`)}
              title={s.title}
            >
              <span className="chat-sidebar-item-title">{s.title || 'Sans titre'}</span>
              <span className="chat-sidebar-item-count">{s.messageCount}</span>
            </button>
          </li>
        ))}
        {sessions.length === 0 && (
          <li className="chat-sidebar-empty">Aucune conversation</li>
        )}
      </ul>
    </aside>
  );
}
```

- [ ] **Step 3 : Intégrer la reprise de session dans `ChatPage`**

Dans `ChatPage.tsx` :

1. Compléter les imports en tête :

```tsx
import { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import ChatSidebar from '../components/ChatSidebar';
import type { ChatMessageDetail, CitationResponse, DocumentResponse } from '../types';
```

2. Au début du composant `ChatPage`, après `const location = ...`, ajouter la lecture de l'URL et l'état de session :

```tsx
  const navigate = useNavigate();
  const params = useParams<{ sessionId?: string }>();

  // Session active : initialisée depuis l'URL, mise à jour après le premier échange d'une conversation vierge.
  const [activeSessionId, setActiveSessionId] = useState<string | undefined>(params.sessionId);
  // Force le rechargement de la sidebar après chaque nouvel échange.
  const [sidebarReload, setSidebarReload] = useState(0);
  // Quand on vient de créer une session et de naviguer vers /chat/:id, on évite un re-fetch inutile
  // (les messages en mémoire sont plus riches : ils portent les citations enrichies section/horodatage).
  const skipLoadRef = useRef<string | null>(null);
```

3. Ajouter un helper de mapping (avant le `return`, à côté des autres fonctions) qui convertit un message serveur en message local. Le mode est déduit de `strategyUsed` :

```tsx
  /**
   * Convertit un message rechargé depuis l'historique en message local.
   * Le mode d'affichage est déduit de la stratégie enregistrée :
   *  - "FullText"   → recherche classique
   *  - "DirectLlm"  → LLM direct
   *  - autre (Direct/HyDE/Fusion/Adaptive) → RAG + LLM
   */
  function mapServerMessage(m: ChatMessageDetail): Message {
    const role = m.role.toLowerCase() === 'user' ? 'user' : 'assistant';
    let msgMode: Mode = 'rag';
    if (m.strategyUsed === 'FullText') msgMode = 'classic';
    else if (m.strategyUsed === 'DirectLlm') msgMode = 'llm';

    return {
      role,
      content: m.content,
      mode: role === 'assistant' ? msgMode : undefined,
      citations: m.citations ?? undefined,
      meta: role === 'assistant'
        ? { strategy: m.strategyUsed ?? '', tokens: m.tokensUsed, duration: m.durationMs }
        : undefined,
    };
  }
```

4. Ajouter l'effet de chargement/reprise piloté par l'URL (après les `useEffect` existants) :

```tsx
  // Chargement / reprise de conversation piloté par l'URL (clic sidebar, URL directe, nouvelle conversation).
  useEffect(() => {
    const sid = params.sessionId;
    setActiveSessionId(sid);

    // Conversation vierge (route /chat sans id) : on repart d'un état neuf.
    if (!sid) {
      setMessages([]);
      return;
    }

    // On vient juste de créer cette session : ses messages sont déjà en mémoire, inutile de recharger.
    if (skipLoadRef.current === sid) {
      skipLoadRef.current = null;
      return;
    }

    let cancelled = false;
    api.chat.getSession(sid)
      .then(detail => {
        if (!cancelled) setMessages(detail.messages.map(mapServerMessage));
      })
      .catch(() => {
        // 404 / session invalide : retour silencieux à une conversation vierge (pas d'erreur bloquante).
        if (!cancelled) {
          setMessages([]);
          navigate('/chat', { replace: true });
        }
      });

    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params.sessionId]);
```

5. Dans `handleSubmit`, transmettre le `sessionId` actif dans la requête. Remplacer l'appel `api.chat.askStream({ question: q, documentIds, useLlm, useRag, systemPrompt })` par :

```tsx
      for await (const event of api.chat.askStream({ question: q, documentIds, useLlm, useRag, systemPrompt, sessionId: activeSessionId })) {
```

6. Toujours dans `handleSubmit`, dans la branche `event.event === 'done'`, après avoir ajouté le message assistant, gérer la session résolue et rafraîchir la sidebar :

```tsx
        } else if (event.event === 'done') {
          const res = event.data;
          setStreamingContent('');
          streamingRef.current = '';
          setMessages(prev => [...prev, {
            role: 'assistant',
            content: res.answer,
            meta: { strategy: res.strategyUsed, tokens: res.totalTokens, duration: res.durationMs },
            citations: res.citations,
            mode: currentMode,
          }]);

          // Première réponse d'une conversation vierge : on adopte l'id renvoyé et on rend l'URL
          // rechargeable/partageable, sans recharger les messages qu'on vient d'afficher.
          if (!activeSessionId && res.sessionId) {
            skipLoadRef.current = res.sessionId;
            setActiveSessionId(res.sessionId);
            navigate(`/chat/${res.sessionId}`, { replace: true });
          }
          // Remonte la conversation active en tête de la sidebar.
          setSidebarReload(x => x + 1);
        }
```

7. Envelopper le rendu dans le layout `sidebar | conversation`. Remplacer la ligne d'ouverture `return (\n    <div className="chat-main">` par :

```tsx
  return (
    <div className="chat-layout">
      <ChatSidebar activeSessionId={activeSessionId} reloadSignal={sidebarReload} />
      <div className="chat-main">
```

et ajouter la balise fermante correspondante : le `</div>` final du composant (celui qui ferme `chat-main`) doit être suivi d'un `</div>` fermant `chat-layout`. Concrètement, remplacer la fin du JSX :

```tsx
      </form>
    </div>
  );
}
```

par :

```tsx
      </form>
      </div>
    </div>
  );
}
```

- [ ] **Step 4 : Ajouter les styles de layout**

Dans `index.css`, **ajouter** (sans modifier les règles existantes) à la fin du fichier :

```css
/* ===== Lot 3 — Layout chat avec sidebar de conversations ===== */
.chat-layout {
  display: flex;
  gap: 16px;
  height: 100%;
  min-height: 0;
}

.chat-sidebar {
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 260px;
  flex-shrink: 0;
  border-right: 1px solid var(--border, #e5e7eb);
  padding-right: 12px;
  overflow-y: auto;
}

.chat-sidebar-new {
  width: 100%;
}

.chat-sidebar-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.chat-sidebar-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  width: 100%;
  padding: 8px 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  text-align: left;
  cursor: pointer;
  color: inherit;
}

.chat-sidebar-item:hover {
  background: var(--surface-hover, #f3f4f6);
}

.chat-sidebar-item-active {
  background: var(--surface-active, #e5edff);
  font-weight: 600;
}

.chat-sidebar-item-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chat-sidebar-item-count {
  flex-shrink: 0;
  font-size: 0.75rem;
  opacity: 0.6;
}

.chat-sidebar-empty {
  padding: 8px 10px;
  font-size: 0.85rem;
  opacity: 0.6;
}
```

- [ ] **Step 5 : Vérifier le build front**

Run: `cd "Step3/src/Front" && npm run build`
Expected: build réussi.

- [ ] **Step 6 : Commit**

```bash
git add "Step3/src/Front/src/App.tsx" "Step3/src/Front/src/components/ChatSidebar.tsx" "Step3/src/Front/src/pages/ChatPage.tsx" "Step3/src/Front/src/index.css"
git commit -m "feat(front): sidebar des conversations et reprise de session par URL"
```

---

## Vérification manuelle finale (e2e)

Ces vérifications ne sont pas automatisées (aucune suite de tests JS ; le repository/DB est vérifié en intégration). À exécuter après la Task 8, back + PostgreSQL (port 5433) + front lancés :

- [ ] **Persistance et citations (couvre le correctif critique Task 2)** : en mode **RAG**, poser une question qui produit des citations. Vérifier que la réponse affiche ses sources. Recharger la page (F5) : la conversation réapparaît **avec** ses citations (document/page/score/extrait). Si les citations disparaissent après rechargement → le mapping `score` (Task 2) ou la persistance (Task 4) est en cause ; inspecter les logs `Warning` « Échec de persistance de l'échange ».
- [ ] **Nouvelle conversation** : cliquer « + Nouvelle conversation » vide le fil et l'URL redevient `/chat`. Le premier envoi fait apparaître la conversation dans la sidebar et l'URL devient `/chat/<guid>`.
- [ ] **Multi-tour** : poser une question de suivi référençant la précédente (ex. « et sa couleur ? ») en mode RAG ou LLM → la réponse tient compte du tour précédent (historique injecté).
- [ ] **Reprise depuis la sidebar** : cliquer une autre conversation recharge ses messages ; le mode d'affichage (classique/LLM/RAG) est correctement restitué depuis `strategyUsed`.
- [ ] **URL directe / session invalide** : ouvrir `/chat/<guid-inexistant>` → retour silencieux à une conversation vierge, sans erreur bloquante.
- [ ] **Non-régression qualité** : comparer une même question RAG avant/après le lot → réponse et citations identiques (le cœur de récupération/citations n'a pas changé, seule sa localisation).

---

## Auto-revue (résultat)

- **Couverture du spec** : §2.1 résolution/persistance → Task 4/5 ; §2.2 signatures (`IUnitOfWork`, `RagResponse.SessionId`) → Task 1/4 ; §2.3 `GetSessionSummariesAsync` → Task 3 ; §3 contrats API → Task 6 ; §4 front (route, sidebar, reprise, navigation, layout, session invalide) → Task 8 ; §5 cas limites → Task 5 (garde-fou/dégradation), Task 6 (404), Task 8 (URL invalide) ; §6 tests → Task 4/5. **Ajout hors-spec justifié** : correctif du mapping `Citation.Score` (Task 2), prérequis à la persistance des citations, directement lié à l'exigence « ne pas perdre en qualité de citations ».
- **Divergence assumée vs design** : session résolue/persistée aux 2 points d'entrée publics (et non « 5 points ») — plus simple, même intention (session créée quel que soit le mode).
- **Cohérence des types** : `ResolveSessionAsync`/`PersistExchangeAsync`/`FinalizeExchangeAsync`/`RunRagPipelineAsync`/`BuildSessionTitle` définis en Task 4, réutilisés en Task 5 ; `ConversationSessionSummary` (Task 3) consommé par le fake (Task 4) et les endpoints (Task 6) ; `sessionId` TS (Task 7) consommé en Task 8.
