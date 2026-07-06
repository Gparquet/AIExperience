# Détection de « nouvelle version » à l'upload de document — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Étendre la détection de doublon existante (même nom **et** même hash) pour couvrir le cas « même nom, contenu différent » : proposer une popup de remplacement avec un message **distinct** de celui du doublon exact, en comparant toujours contre le document le plus récent portant ce nom en cas d'homonymes multiples.

**Architecture:** Remplacement de `IDocumentRepository.GetByFileNameAndHashAsync` par `GetLatestByFileNameAsync(userId, fileName, ct)` — une seule requête par nom, triée par `CreatedAt DESC`. `UploadDocumentHandler` compare ensuite les hashs pour déterminer un `DocumentMatchType` (`ExactDuplicate` | `SameNameDifferentContent`), porté par `DuplicateDocumentException` (étendue, pas de nouvelle exception) et propagé jusqu'au front via les DTOs `CheckDuplicateResponse`/`DuplicateDocumentResponse`.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core / MediatR (back), React 19 + TypeScript + Vite (front), xUnit + FluentAssertions (tests).

**Spec source :** `docs/superpowers/specs/2026-07-05-detection-nouvelle-version-upload-design.md`

## Global Constraints

- Toutes les réponses et TOUS les commentaires de code sont en **français** (`///` XML pour C#, `//`/JSDoc pour TypeScript) — règle CLAUDE.md.
- Clean Architecture stricte : Domain → rien, Application → Domain, Infrastructure → Domain+Application, Web.Api → tout.
- Entités : setters **privés**, création via méthode factory statique `Entity.Create(...)`.
- Pas de migration EF Core / SQL nécessaire pour ce plan : l'index composite existant `(user_id, file_name, content_hash)` sert de préfixe pour filtrer par `(user_id, file_name)` (confirmé dans le spec).
- Aucune bibliothèque de mocking dans `AIExperience.Tests` — fakes écrits à la main, comme l'existant.
- Pas de nouvelle classe d'exception : `DuplicateDocumentException` est étendue avec une propriété `MatchType`.
- **Ne pas toucher** au refactor en cours et non committé de `IngestDocumentCommand`/`IngestDocumentHandler` (extraction de l'ingestion hors de `UploadDocumentHandler`/`DocumentsController`) — sauf renommage strictement nécessaire d'une méthode d'interface implémentée par un fake de test (Task 2), qui ne change aucune logique de ce refactor.
- **Ne rien committer sans confirmation explicite de l'utilisateur à chaque étape** — un incident précédent dans cette session a vu un `git commit` (sans pathspec) embarquer tout l'index par erreur ; chaque commit de ce plan doit être limité aux fichiers listés dans le `git add` de l'étape correspondante, jamais `git add -A` ou `git commit` sans fichiers explicites.

---

### Task 1: `DocumentMatchType` (Domain) + `DuplicateDocumentException` étendue (Application)

**Files:**
- Create: `Step 3/src/Back/AIExperience.Rag.Domain/Enums/DocumentMatchType.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/Document/Exceptions/DuplicateDocumentException.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `DocumentMatchType` (enum `ExactDuplicate` | `SameNameDifferentContent`) ; `DuplicateDocumentException(Guid, string, DateTimeOffset, DocumentMatchType)` avec nouvelle propriété `MatchType` — consommés par `UploadDocumentHandler` (Task 3) et `DocumentsController` (Task 5).

- [ ] **Step 1: Créer l'enum**

Créer `Step 3/src/Back/AIExperience.Rag.Domain/Enums/DocumentMatchType.cs` :

```csharp
namespace AIExperience.Rag.Domain.Enums;

/// <summary>Type de correspondance détecté entre un fichier uploadé et un document existant de même nom.</summary>
public enum DocumentMatchType
{
    /// <summary>Nom de fichier et hash de contenu strictement identiques.</summary>
    ExactDuplicate,

    /// <summary>Même nom de fichier, contenu différent — probable nouvelle version du document.</summary>
    SameNameDifferentContent
}
```

- [ ] **Step 2: Étendre `DuplicateDocumentException`**

Remplacer le contenu de `Step 3/src/Back/AIExperience.Rag.Application/Document/Exceptions/DuplicateDocumentException.cs` :

```csharp
using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Application.Document.Exceptions;

/// <summary>
/// Levée par <see cref="Command.UploadDocumentHandler"/> lorsqu'un document de même nom existe déjà
/// (contenu identique ou différent, voir <see cref="MatchType"/>) et que l'appelant n'a pas confirmé
/// son remplacement via <see cref="Command.UploadDocumentCommand.ReplaceDocumentId"/>.
/// </summary>
public sealed class DuplicateDocumentException(
    Guid existingDocumentId, string existingFileName, DateTimeOffset existingCreatedAt, DocumentMatchType matchType)
    : Exception(DuplicateDocumentException.BuildMessage(existingFileName, existingDocumentId, matchType))
{
    /// <summary>Identifiant du document existant en conflit.</summary>
    public Guid ExistingDocumentId { get; } = existingDocumentId;

    /// <summary>Nom de fichier du document existant en conflit.</summary>
    public string ExistingFileName { get; } = existingFileName;

    /// <summary>Date de création du document existant en conflit.</summary>
    public DateTimeOffset ExistingCreatedAt { get; } = existingCreatedAt;

    /// <summary>Type de correspondance détecté : contenu identique ou nom identique avec contenu différent.</summary>
    public DocumentMatchType MatchType { get; } = matchType;

    private static string BuildMessage(string existingFileName, Guid existingDocumentId, DocumentMatchType matchType) =>
        matchType == DocumentMatchType.ExactDuplicate
            ? $"Un document identique (\"{existingFileName}\") existe déjà (Id: {existingDocumentId})."
            : $"Une version différente de \"{existingFileName}\" existe déjà (Id: {existingDocumentId}).";
}
```

- [ ] **Step 3: Vérifier la compilation**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: **Échec attendu** dans `UploadDocumentHandler.cs` (construit encore `DuplicateDocumentException` avec 3 arguments au lieu de 4). C'est normal : Task 3 corrige cet appelant. Confirmer qu'aucune autre erreur n'apparaît que celle-là.

- [ ] **Step 4: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Enums/DocumentMatchType.cs" "Step 3/src/Back/AIExperience.Rag.Application/Document/Exceptions/DuplicateDocumentException.cs"
git commit -m "feat(rag): ajoute DocumentMatchType et etend DuplicateDocumentException"
```

---

### Task 2: Repository — renommer `GetByFileNameAndHashAsync` en `GetLatestByFileNameAsync`

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IDocumentRepository.cs:49-54`
- Modify: `Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/DocumentRepository.cs:71-74`
- Modify: `Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs` (fake repository, ligne 53-55)
- Modify: `Step 3/src/Back/AIExperience.Tests/IngestDocumentHandlerTests.cs` (fake repository, ligne 36-38) — fichier non committé du refactor `IngestDocumentHandler` en cours ; seul le nom de la méthode d'interface implémentée est renommé ici, aucune logique de ce refactor n'est modifiée.

**Interfaces:**
- Consumes: rien de nouveau.
- Produces: `IDocumentRepository.GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default) : Task<Document?>` — retourne le document le plus récent (`CreatedAt DESC`) portant ce nom pour cet utilisateur, ou `null`. Consommé par `UploadDocumentHandler` (Task 3) et `DocumentsController` (Task 5).

- [ ] **Step 1: Renommer la méthode dans l'interface**

Dans `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IDocumentRepository.cs`, remplacer les lignes 49-54 :

```csharp
    /// <summary>
    /// Recherche le document le plus récent portant ce nom de fichier, pour un utilisateur donné
    /// (détection de doublon / nouvelle version). Ignore le hash de contenu : c'est à l'appelant
    /// de comparer le hash du document retourné pour distinguer un doublon exact d'une nouvelle version.
    /// </summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="fileName">Nom du fichier à comparer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default);
```

- [ ] **Step 2: Renommer l'implémentation**

Dans `Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/DocumentRepository.cs`, remplacer les lignes 71-74 :

```csharp
    /// <inheritdoc/>
    public async Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default)
        => await context.Documents
            .Where(d => d.UserId == userId && d.FileName == fileName)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);
```

- [ ] **Step 3: Mettre à jour le fake repository de `UploadDocumentHandlerTests.cs`**

Dans `Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs`, remplacer les lignes 53-55 :

```csharp
        public Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default)
            => Task.FromResult(Documents
                .Where(d => d.UserId == userId && d.FileName == fileName)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefault());
```

- [ ] **Step 4: Mettre à jour le fake repository de `IngestDocumentHandlerTests.cs`**

Dans `Step 3/src/Back/AIExperience.Tests/IngestDocumentHandlerTests.cs`, remplacer les lignes 36-38 :

```csharp
        public Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default)
            => Task.FromResult(Documents
                .Where(d => d.UserId == userId && d.FileName == fileName)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefault());
```

- [ ] **Step 5: Vérifier la compilation**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: **Échecs attendus** dans `UploadDocumentHandler.cs` (appelle encore `GetByFileNameAndHashAsync` et construit `DuplicateDocumentException` avec 3 arguments) et `DocumentsController.cs` (appelle encore `GetByFileNameAndHashAsync`). C'est normal : Task 3 corrige le premier, Task 5 corrige le second. Confirmer qu'aucune autre erreur n'apparaît.

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IDocumentRepository.cs" "Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/DocumentRepository.cs" "Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs" "Step 3/src/Back/AIExperience.Tests/IngestDocumentHandlerTests.cs"
git commit -m "refactor(rag): renomme GetByFileNameAndHashAsync en GetLatestByFileNameAsync"
```

---

### Task 3: Règle métier dans `UploadDocumentHandler` (TDD)

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentHandler.cs`
- Modify: `Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs`

**Interfaces:**
- Consumes: `IDocumentRepository.GetLatestByFileNameAsync` (Task 2), `DuplicateDocumentException` avec `MatchType` (Task 1).
- Produces: `UploadDocumentHandler.Handle` avec la règle complète (doublon exact / nouvelle version / remplacement / création) — consommé par `DocumentsController` (déjà en place, inchangé dans ce fichier).

- [ ] **Step 1: Écrire les tests qui échouent**

Remplacer le contenu de `Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs` :

```csharp
using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Document.Exceptions;
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
        var handler = new UploadDocumentHandler(repository, unitOfWork, new FakeFileHashService("hash-a"));

        var response = await handler.Handle(CreateCommand(), CancellationToken.None);

        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
        unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExactDuplicateWithoutReplaceId_ThrowsWithExactDuplicateMatchType()
    {
        var repository = new FakeDocumentRepository();
        var existing = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-a");
        repository.Documents.Add(existing);
        var handler = new UploadDocumentHandler(repository, new FakeUnitOfWork(), new FakeFileHashService("hash-a"));

        var act = () => handler.Handle(CreateCommand(), CancellationToken.None);

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
        var handler = new UploadDocumentHandler(repository, new FakeUnitOfWork(), new FakeFileHashService("hash-new"));

        var act = () => handler.Handle(CreateCommand(), CancellationToken.None);

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
        var handler = new UploadDocumentHandler(repository, unitOfWork, new FakeFileHashService("hash-a"));

        var response = await handler.Handle(CreateCommand(existing.Id), CancellationToken.None);

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
        var handler = new UploadDocumentHandler(repository, unitOfWork, new FakeFileHashService("hash-new"));

        var response = await handler.Handle(CreateCommand(existing.Id), CancellationToken.None);

        repository.DeletedIds.Should().ContainSingle().Which.Should().Be(existing.Id);
        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
        response.DocumentId.Should().NotBe(existing.Id);
        unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ReplaceIdProvidedButNoDuplicateFound_CreatesDocumentNormally()
    {
        var repository = new FakeDocumentRepository();
        var handler = new UploadDocumentHandler(repository, new FakeUnitOfWork(), new FakeFileHashService("hash-a"));
        var staleReplaceId = Guid.NewGuid();

        var response = await handler.Handle(CreateCommand(staleReplaceId), CancellationToken.None);

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
        var handler = new UploadDocumentHandler(repository, new FakeUnitOfWork(), new FakeFileHashService("hash-newer"));

        var act = () => handler.Handle(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DuplicateDocumentException>();
        exception.Which.ExistingDocumentId.Should().Be(newer.Id, "la comparaison doit cibler le document le plus récent portant ce nom");
        exception.Which.MatchType.Should().Be(DocumentMatchType.ExactDuplicate);
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter UploadDocumentHandlerTests`
Expected: FAIL — `UploadDocumentHandler.Handle` appelle encore `GetByFileNameAndHashAsync` (méthode qui n'existe plus depuis Task 2) et construit `DuplicateDocumentException` avec 3 arguments : erreurs de compilation, pas d'exécution.

- [ ] **Step 3: Implémenter la règle métier**

Remplacer le contenu de `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentHandler.cs` :

```csharp
using AIExperience.Rag.Application.Document.Exceptions;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Handler MediatR pour la commande <see cref="UploadDocumentCommand"/>.
/// Compare le fichier uploadé au document le plus récent portant le même nom : bloque la création
/// si aucun remplacement n'a été confirmé (doublon exact ou nouvelle version détectée), ou supprime
/// l'ancien document et crée le nouveau de façon atomique (une seule transaction) si confirmé.
/// </summary>
public sealed class UploadDocumentHandler(
    IDocumentRepository documentRepository, IUnitOfWork unitOfWork, IFileHashService fileHashService) : IRequestHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    /// <summary>
    /// Calcule le hash du fichier puis exécute la détection de doublon/nouvelle version et la création
    /// (ou le remplacement) du document.
    /// </summary>
    /// <param name="request">Commande contenant les données du fichier uploadé, son chemin et un éventuel ReplaceDocumentId.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Réponse contenant l'identifiant et le statut initial du document.</returns>
    /// <exception cref="DuplicateDocumentException">Un document de même nom existe déjà et n'a pas été confirmé comme remplaçable.</exception>
    public async Task<UploadDocumentResponse> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var contentHash = await fileHashService.ComputeSha256Async(request.FilePath, cancellationToken);

        var latestSameName = await documentRepository.GetLatestByFileNameAsync(
            request.UserId, request.FileName, cancellationToken);

        if (latestSameName is not null && latestSameName.Id != request.ReplaceDocumentId)
        {
            var matchType = latestSameName.ContentHash == contentHash
                ? DocumentMatchType.ExactDuplicate
                : DocumentMatchType.SameNameDifferentContent;

            throw new DuplicateDocumentException(
                latestSameName.Id, latestSameName.FileName, latestSameName.CreatedAt, matchType);
        }

        if (latestSameName is not null)
        {
            // Remplacement confirmé : suppression de l'ancien document avant création du nouveau,
            // le tout validé par un seul SaveChangesAsync ci-dessous (une seule transaction atomique).
            await documentRepository.DeleteAsync(latestSameName.Id, cancellationToken);
        }

        var document = Domain.Entities.Document.Create(
          request.FileName,
          request.ContentType,
          request.FileSizeBytes,
          request.UserId,
          request.DocumentMetadata,
          request.ChunkingStrategy,
          contentHash);

        await documentRepository.AddAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UploadDocumentResponse
        {
            DocumentId = document.Id,
            FileName = document.FileName,
            Status = document.Status,
            CreatedAt = document.CreatedAt
        };
    }
}
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter UploadDocumentHandlerTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Lancer toute la suite pour vérifier l'absence de régression (hors DocumentsController, corrigé en Task 5)**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: seule erreur restante attendue : `DocumentsController.cs` (appelle encore `GetByFileNameAndHashAsync` et construit `DuplicateDocumentResponse` avec 1 argument). Corrigé en Task 5.

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentHandler.cs" "Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs"
git commit -m "feat(rag): detecte la nouvelle version (meme nom, contenu different) dans UploadDocumentHandler"
```

---

### Task 4: DTOs — `MatchType` sur `CheckDuplicateResponse` et `DuplicateDocumentResponse`

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs`

**Interfaces:**
- Consumes: `DocumentMatchType` (Task 1).
- Produces: `CheckDuplicateResponse(bool IsDuplicate, ExistingDocumentInfo? ExistingDocument, DocumentMatchType? MatchType)`, `DuplicateDocumentResponse(ExistingDocumentInfo ExistingDocument, DocumentMatchType MatchType)` — consommés par `DocumentsController` (Task 5) et le front-end (Task 6).

- [ ] **Step 1: Ajouter `using` et étendre les DTOs**

Dans `Step 3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs`, ajouter en première ligne :

```csharp
using AIExperience.Rag.Domain.Enums;
```

Remplacer les deux dernières lignes du fichier (`CheckDuplicateResponse` et `DuplicateDocumentResponse`) :

```csharp
/// <summary>Réponse de la pré-vérification de doublon. MatchType est null si IsDuplicate est false.</summary>
public record CheckDuplicateResponse(bool IsDuplicate, ExistingDocumentInfo? ExistingDocument, DocumentMatchType? MatchType);

/// <summary>Corps de la réponse 409 Conflict renvoyée lorsque l'upload est bloqué par un doublon ou une nouvelle version non confirmée.</summary>
public record DuplicateDocumentResponse(ExistingDocumentInfo ExistingDocument, DocumentMatchType MatchType);
```

- [ ] **Step 2: Vérifier la compilation du projet Web.Api isolément**

Run: `dotnet build "Step 3/src/Back/AIExperience.Web.Api/AIExperience.Web.Api.csproj"`
Expected: **Échecs attendus** dans `DocumentsController.cs` (construit encore ces deux records avec l'ancienne arité). Corrigé en Task 5.

- [ ] **Step 3: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs"
git commit -m "feat(rag): ajoute MatchType aux DTOs de detection de doublon"
```

---

### Task 5: `DocumentsController` — propager `MatchType`

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs`

**Interfaces:**
- Consumes: `IDocumentRepository.GetLatestByFileNameAsync` (Task 2), `DuplicateDocumentException.MatchType` (Task 1), `CheckDuplicateResponse`/`DuplicateDocumentResponse` étendus (Task 4).
- Produces: `POST /api/documents/check-duplicate` et `POST /api/documents` (réponse 409) incluant désormais `MatchType` — consommé par le front-end (Task 6).

- [ ] **Step 1: Mettre à jour `CheckDuplicate`**

Dans `Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs`, remplacer le corps de la méthode `CheckDuplicate` :

```csharp
    [HttpPost("check-duplicate")]
    public async Task<ActionResult<CheckDuplicateResponse>> CheckDuplicate([FromBody] CheckDuplicateRequest request)
    {
        var existing = await documentRepository.GetLatestByFileNameAsync(DefaultUserId, request.FileName);
        if (existing is null)
            return Ok(new CheckDuplicateResponse(false, null, null));

        var matchType = existing.ContentHash == request.ContentHash
            ? DocumentMatchType.ExactDuplicate
            : DocumentMatchType.SameNameDifferentContent;

        return Ok(new CheckDuplicateResponse(
            true, new ExistingDocumentInfo(existing.Id, existing.FileName, existing.CreatedAt), matchType));
    }
```

- [ ] **Step 2: Mettre à jour le catch de doublon dans `Upload`**

Toujours dans `DocumentsController.cs`, remplacer le bloc `catch (DuplicateDocumentException ex)` :

```csharp
        catch (DuplicateDocumentException ex)
        {
            return Conflict(new DuplicateDocumentResponse(
                new ExistingDocumentInfo(ex.ExistingDocumentId, ex.ExistingFileName, ex.ExistingCreatedAt),
                ex.MatchType));
        }
```

- [ ] **Step 3: Vérifier la compilation de la solution complète**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build réussi, 0 erreur.

- [ ] **Step 4: Lancer toute la suite de tests back-end**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: PASS (tous les tests, y compris les 7 de `UploadDocumentHandlerTests` et ceux de `IngestDocumentHandlerTests`, `DocumentContentHashTests`, `FileHashServiceTests`).

- [ ] **Step 5: Vérification manuelle via Scalar**

Démarrer l'API (`dotnet run --project "Step 3/src/Back/AIExperience.Web.Api"`), ouvrir `http://localhost:50406/scalar/v1`, et :
1. Uploader un PDF quelconque via `POST /api/documents` → succès (201).
2. Ré-uploader **le même fichier** sans `replaceDocumentId` → `409 Conflict`, corps `{ "existingDocument": {...}, "matchType": "ExactDuplicate" }`.
3. Modifier légèrement le fichier (même nom, contenu différent) et l'uploader sans `replaceDocumentId` → `409 Conflict`, corps avec `"matchType": "SameNameDifferentContent"`.
4. Appeler `POST /api/documents/check-duplicate` avec `{ "fileName": "...", "contentHash": "..." }` (hash du fichier modifié) → `isDuplicate: true`, `matchType: "SameNameDifferentContent"`.
5. Ré-uploader avec `?replaceDocumentId=<id retourné à l'étape 3>` → succès (201), nouvel `Id`.

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs"
git commit -m "feat(rag): propage MatchType dans DocumentsController (check-duplicate et 409)"
```

---

### Task 6: Front-end — types et distinction de message dans la popup

**Files:**
- Modify: `Step 3/src/Front/src/types/index.ts:10-21`
- Modify: `Step 3/src/Front/src/pages/DocumentsPage.tsx`

**Interfaces:**
- Consumes: `CheckDuplicateResponse.matchType` (Task 5).
- Produces: type `DocumentMatchType` (TS), popup de remplacement affichant un message différent selon le type de correspondance — aucun consommateur ultérieur dans ce plan.

- [ ] **Step 1: Ajouter le type et étendre `CheckDuplicateResponse`**

Dans `Step 3/src/Front/src/types/index.ts`, remplacer les lignes 10-21 :

```typescript
/** Type de correspondance détecté avec un document existant de même nom. */
export type DocumentMatchType = 'ExactDuplicate' | 'SameNameDifferentContent';

/** Informations minimales sur un document existant en conflit (détection de doublon ou de nouvelle version). */
export interface ExistingDocumentInfo {
  id: string;
  fileName: string;
  createdAt: string;
}

/** Réponse de la pré-vérification de doublon avant upload. */
export interface CheckDuplicateResponse {
  isDuplicate: boolean;
  existingDocument: ExistingDocumentInfo | null;
  matchType: DocumentMatchType | null;
}
```

- [ ] **Step 2: Vérifier la compilation TypeScript (types seuls)**

Run: `cd "Step 3/src/Front" && npx tsc -b`
Expected: **Échec attendu** dans `DocumentsPage.tsx` — `setPendingDuplicate({ file, existing: check.existingDocument })` n'assigne pas encore `matchType`, incompatible avec le state qui sera étendu à l'étape suivante. Corrigé au Step 3.

- [ ] **Step 3: Étendre le state et distinguer le message dans `DocumentsPage.tsx`**

Dans `Step 3/src/Front/src/pages/DocumentsPage.tsx`, modifier l'import (ligne 4) :

```typescript
import type { DocumentMatchType, DocumentResponse, ExistingDocumentInfo } from '../types';
```

Remplacer la ligne 21 (state `pendingDuplicate`) :

```typescript
  // Doublon ou nouvelle version détecté(e) en attente de confirmation utilisateur.
  const [pendingDuplicate, setPendingDuplicate] = useState<{ file: File; existing: ExistingDocumentInfo; matchType: DocumentMatchType } | null>(null);
```

Remplacer le bloc `if (hash) { ... }` dans `handleUpload` (lignes 73-79) :

```typescript
      if (hash) {
        const check = await api.documents.checkDuplicate(file.name, hash);
        if (check.isDuplicate && check.existingDocument && check.matchType) {
          setPendingDuplicate({ file, existing: check.existingDocument, matchType: check.matchType });
          return;
        }
      }
```

Remplacer le bloc JSX de la popup `pendingDuplicate` (lignes 303-325) :

```tsx
      {pendingDuplicate && (
        <div className="modal-overlay" onClick={() => !uploading && handleCancelReplace()}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            {pendingDuplicate.matchType === 'ExactDuplicate' ? (
              <>
                <h2>Document déjà importé</h2>
                <p>
                  Un document nommé « {pendingDuplicate.existing.fileName} » a déjà été importé le{' '}
                  {new Date(pendingDuplicate.existing.createdAt).toLocaleDateString('fr-FR')}.
                </p>
                <p className="modal-warning">
                  Si vous continuez, ce document existant sera supprimé et remplacé par le nouveau fichier.
                </p>
              </>
            ) : (
              <>
                <h2>Nouvelle version détectée</h2>
                <p>
                  Une version différente de « {pendingDuplicate.existing.fileName} » a déjà été importée le{' '}
                  {new Date(pendingDuplicate.existing.createdAt).toLocaleDateString('fr-FR')}. Le contenu a changé.
                </p>
                <p className="modal-warning">
                  Si vous continuez, l'ancienne version sera supprimée et remplacée par ce nouveau fichier.
                </p>
              </>
            )}
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={handleCancelReplace} disabled={uploading}>
                Annuler
              </button>
              <button className="btn btn-danger" onClick={handleConfirmReplace} disabled={uploading}>
                {uploading && <span className="btn-spinner" />}
                {uploading ? 'Remplacement…' : 'Confirmer le remplacement'}
              </button>
            </div>
          </div>
        </div>
      )}
```

- [ ] **Step 4: Vérifier la compilation TypeScript**

Run: `cd "Step 3/src/Front" && npx tsc -b`
Expected: 0 erreur.

- [ ] **Step 5: Vérification manuelle dans le navigateur**

Démarrer back (`dotnet run --project "Step 3/src/Back/AIExperience.Web.Api"`) et front (`cd "Step 3/src/Front" && npm run dev`, ouvrir `http://localhost:5173`) :
1. Importer un document → apparaît normalement dans la liste.
2. Réimporter **le même fichier** → popup « Document déjà importé » (texte inchangé par rapport à l'existant).
3. Modifier le fichier (même nom, ex. ajouter une ligne dans un `.txt`) et le réimporter → popup **« Nouvelle version détectée »** avec le nouveau texte.
4. Cliquer **Confirmer le remplacement** sur le cas 3 → l'ancien document disparaît, un nouveau (nouvel Id, nouveau hash) apparaît à sa place.
5. Importer un fichier de nom différent → aucune popup, upload direct (pas de régression).

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Front/src/types/index.ts" "Step 3/src/Front/src/pages/DocumentsPage.tsx"
git commit -m "feat(rag): distingue doublon exact et nouvelle version dans la popup de remplacement"
```

---

## Vérification finale

- [ ] `dotnet test "Step 3/src/Back/AIExperience.slnx"` → tous les tests passent (existants + les 3 nouveaux de `UploadDocumentHandlerTests` : `Handle_SameNameDifferentContentWithoutReplaceId_...`, `Handle_SameNameDifferentContentWithMatchingReplaceId_...`, `Handle_MultipleHomonyms_...`).
- [ ] `dotnet build "Step 3/src/Back/AIExperience.slnx"` → 0 warning nouveau, 0 erreur.
- [ ] `cd "Step 3/src/Front" && npx tsc -b` → 0 erreur.
- [ ] Scénario manuel complet rejoué une dernière fois (doublon exact / nouvelle version / annulation / confirmation).
- [ ] Rappel : ne pas pousser/committer au-delà de ce qui a été explicitement validé par l'utilisateur — reconfirmer avant tout `git push`, et ne jamais utiliser `git commit` sans lister les fichiers explicitement (cf. Global Constraints).
