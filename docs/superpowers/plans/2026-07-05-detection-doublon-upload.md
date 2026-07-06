# Détection de doublon à l'upload de document — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Empêcher la création silencieuse de documents en double : si un utilisateur réimporte un fichier déjà présent (même nom **et** même contenu), afficher une popup ; sur confirmation, l'ancien document est supprimé et remplacé par le nouveau.

**Architecture:** La règle métier (bloquer / remplacer / créer normalement) vit dans `UploadDocumentHandler` (couche Application), pas dans le contrôleur. Le hash SHA-256 est calculé côté serveur (source de vérité) et, en plus, côté navigateur (pré-vérification UX légère via un nouvel endpoint `check-duplicate`). Suppression + recréation se font en une seule transaction EF Core.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core / MediatR / FluentValidation (back), React 19 + TypeScript + Vite (front), xUnit + FluentAssertions (tests, projet `AIExperience.Tests` déjà existant en Step 3).

**Spec source :** `docs/superpowers/specs/2026-07-05-detection-doublon-upload-design.md`

## Global Constraints

- Toutes les réponses et TOUS les commentaires de code sont en **français** (`///` XML pour C#, `//`/JSDoc pour TypeScript) — règle CLAUDE.md.
- Clean Architecture stricte : Domain → rien, Application → Domain, Infrastructure → Domain+Application, Web.Api → tout. Une couche interne ne référence jamais une couche externe.
- CQRS : écritures via MediatR (`IRequest<T>` + handler + validator), lectures via appel direct au service/repository (pas de MediatR) — le nouvel endpoint `check-duplicate` est une lecture, donc appel direct.
- Entités : setters **privés**, création via méthode factory statique `Entity.Create(...)`, jamais de `new Entity { ... }`.
- Pas de migration EF Core automatisée — schéma géré manuellement via `Step 3/scripts/init.sql` (+ script de migration séparé pour les bases déjà existantes, sur le modèle du précédent `Step 3/scripts/migrate-temporal-chunks.sql`).
- Aucune bibliothèque de mocking dans `AIExperience.Tests` (xUnit + FluentAssertions uniquement) — utiliser des faux écrits à la main (classes privées implémentant les interfaces) pour les tests de `UploadDocumentHandler`.
- Pas de nouveau composant `Modal`/`Dialog` partagé — reproduire le pattern inline déjà présent dans `DocumentsPage.tsx` (classes CSS `modal-overlay`/`modal`/`modal-actions`).
- Toutes les commandes s'exécutent depuis `Step 3/src/Back` ou `Step 3/src/Front` selon le contexte (chemins ci-dessous déjà préfixés `Step 3/`).
- **Ne rien committer sans confirmation explicite de l'utilisateur** — l'utilisateur a demandé de ne rien committer "pour le moment" lors du brainstorming ; à reconfirmer avant d'exécuter les étapes de commit de ce plan.

---

### Task 1: `ContentHash` sur l'entité `Document` (Domain)

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs:36-79`
- Test: `Step 3/src/Back/AIExperience.Tests/DocumentContentHashTests.cs` (nouveau fichier)

**Interfaces:**
- Consumes: rien (Domain, zéro dépendance).
- Produces: `Document.ContentHash` (`string`, propriété publique à setter privé) ; `Document.Create(fileName, contentType, fileSizeBytes, userId, metadata, chunkingStrategy = ChunkingStrategy.Recursive, contentHash = "")` — nouveau paramètre optionnel `contentHash` en dernière position (ne casse pas les appels existants).

- [ ] **Step 1: Écrire le test qui échoue**

Créer `Step 3/src/Back/AIExperience.Tests/DocumentContentHashTests.cs` :

```csharp
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour la propriété <see cref="Document.ContentHash"/> (détection de doublon à l'upload).
/// </summary>
public sealed class DocumentContentHashTests
{
    [Fact]
    public void Create_WithContentHash_SetsContentHash()
    {
        var document = Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport"),
            chunkingStrategy: ChunkingStrategy.Recursive,
            contentHash: "abc123");

        document.ContentHash.Should().Be("abc123");
    }

    [Fact]
    public void Create_WithoutContentHash_DefaultsToEmptyString()
    {
        var document = Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport"));

        document.ContentHash.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter DocumentContentHashTests`
Expected: FAIL — `error CS1739: 'contentHash' n'est pas un paramètre nommé valide` (ou `ContentHash` introuvable sur `Document`).

- [ ] **Step 3: Implémenter**

Dans `Step 3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs`, ajouter la propriété après `FileSizeBytes` (ligne 22) :

```csharp
    /// <summary>Empreinte SHA-256 (hex, 64 caractères) du contenu du fichier, utilisée pour la détection de doublon.</summary>
    public string ContentHash { get; private set; } = string.Empty;
```

Remplacer la méthode `Create` (lignes 62-79) par :

```csharp
    public static Document Create(
        string fileName,
        string contentType,
        long fileSizeBytes,
        string userId,
        DocumentMetadata metadata,
        ChunkingStrategy chunkingStrategy = ChunkingStrategy.Recursive,
        string contentHash = "")
    {
        return new Document
        {
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            UserId = userId,
            Metadata = metadata,
            ChunkingStrategy = chunkingStrategy,
            ContentHash = contentHash
        };
    }
```

Mettre à jour le commentaire XML au-dessus de `Create` pour ajouter :
```csharp
    /// <param name="contentHash">Empreinte SHA-256 du contenu du fichier (détection de doublon).</param>
```

- [ ] **Step 4: Lancer le test pour vérifier qu'il passe**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter DocumentContentHashTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Lancer toute la suite de tests pour vérifier l'absence de régression**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: PASS (tous les tests existants, notamment `DocumentTests.cs`, restent au vert car `contentHash` a une valeur par défaut).

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs" "Step 3/src/Back/AIExperience.Tests/DocumentContentHashTests.cs"
git commit -m "feat(rag): ajoute ContentHash a l'entite Document pour la detection de doublon"
```

---

### Task 2: `GetByFileNameAndHashAsync` sur le repository

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IDocumentRepository.cs:8-48`
- Modify: `Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/DocumentRepository.cs:12-70`

**Interfaces:**
- Consumes: `Document.ContentHash` (Task 1).
- Produces: `IDocumentRepository.GetByFileNameAndHashAsync(string userId, string fileName, string contentHash, CancellationToken ct = default) : Task<Document?>` — utilisé par `UploadDocumentHandler` (Task 5) et `DocumentsController` (Task 7).

**Note** : pas de test automatisé pour cette méthode — aucun précédent de test d'intégration base de données dans `AIExperience.Tests` (tous les tests existants sont des tests unitaires purs). La méthode est exercée indirectement par les tests du handler (Task 5) via un faux repository respectant le même contrat. Vérification manuelle prévue en Task 9.

- [ ] **Step 1: Ajouter la méthode à l'interface**

Dans `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IDocumentRepository.cs`, ajouter avant la fermeture de l'interface (après `DeleteAsync`, ligne 40) :

```csharp
    /// <summary>Recherche un document existant par nom de fichier ET hash de contenu, pour un utilisateur donné (détection de doublon).</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="fileName">Nom du fichier à comparer.</param>
    /// <param name="contentHash">Empreinte SHA-256 du contenu à comparer.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<Document?> GetByFileNameAndHashAsync(string userId, string fileName, string contentHash, CancellationToken ct = default);
```

- [ ] **Step 2: Implémenter dans `DocumentRepository`**

Dans `Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/DocumentRepository.cs`, ajouter après `DeleteAsync` (avant la fermeture de la classe, ligne 69) :

```csharp
    /// <inheritdoc/>
    public async Task<Document?> GetByFileNameAndHashAsync(string userId, string fileName, string contentHash, CancellationToken ct = default)
        => await context.Documents
            .FirstOrDefaultAsync(d => d.UserId == userId && d.FileName == fileName && d.ContentHash == contentHash, ct);
```

- [ ] **Step 3: Vérifier la compilation**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build réussi, 0 erreur (l'interface et son implémentation sont cohérentes).

- [ ] **Step 4: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Repositories/IDocumentRepository.cs" "Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Repositories/DocumentRepository.cs"
git commit -m "feat(rag): ajoute GetByFileNameAndHashAsync au repository Document"
```

---

### Task 3: Mapping EF Core + schéma SQL

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/DocumentConfiguration.cs:19-51`
- Modify: `Step 3/scripts/init.sql:18-40`
- Create: `Step 3/scripts/migrate-content-hash.sql`

**Interfaces:**
- Consumes: `Document.ContentHash` (Task 1).
- Produces: colonne `content_hash` mappée en base, exploitable par la requête EF Core de Task 2.

- [ ] **Step 1: Mapper la colonne dans `DocumentConfiguration.cs`**

Dans `Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/DocumentConfiguration.cs`, ajouter après la ligne `builder.Property(d => d.FileSizeBytes).HasColumnName("file_size_bytes");` (ligne 28) :

```csharp
        builder.Property(d => d.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
```

Et ajouter un index composite après les index existants (après `builder.HasIndex(d => d.CreatedAt);`, ligne 51) :

```csharp
        builder.HasIndex(d => new { d.UserId, d.FileName, d.ContentHash });
```

- [ ] **Step 2: Ajouter la colonne dans `init.sql` (pour les installations neuves)**

Dans `Step 3/scripts/init.sql`, dans la définition de `CREATE TABLE IF NOT EXISTS documents` (ligne 18), ajouter la colonne après `file_size_bytes` (ligne 22) :

```sql
    content_hash         VARCHAR(64)  NOT NULL DEFAULT '',
```

Ajouter l'index après les index existants sur `documents` (après `CREATE INDEX IF NOT EXISTS ix_documents_created_at ON documents (created_at);`, ligne 40) :

```sql
CREATE INDEX IF NOT EXISTS ix_documents_user_filename_hash ON documents (user_id, file_name, content_hash);
```

- [ ] **Step 3: Créer le script de migration pour les bases déjà existantes**

Créer `Step 3/scripts/migrate-content-hash.sql` (même convention que le précédent `migrate-temporal-chunks.sql`) :

```sql
-- ============================================================
-- Migration : ajout de content_hash pour la détection de doublon
-- À exécuter manuellement sur une base Step 3 existante
-- (init.sql ne s'exécute qu'au premier démarrage d'un volume vide)
-- ============================================================

ALTER TABLE documents
    ADD COLUMN IF NOT EXISTS content_hash VARCHAR(64) NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS ix_documents_user_filename_hash ON documents (user_id, file_name, content_hash);
```

- [ ] **Step 4: Vérifier la compilation**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build réussi, 0 erreur.

- [ ] **Step 5: Appliquer la migration sur la base de développement locale**

Run (base Postgres Step 3 déjà démarrée via `docker-compose up -d` dans `Step 3/`, conteneur nommé `rag-postgres` d'après `Step 3/docker-compose.yml`) :
```bash
docker exec -i rag-postgres psql -U postgres -d ragdocumentchat -f - < "Step 3/scripts/migrate-content-hash.sql"
```
Expected: `ALTER TABLE` puis `CREATE INDEX` confirmés sans erreur.

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/DocumentConfiguration.cs" "Step 3/scripts/init.sql" "Step 3/scripts/migrate-content-hash.sql"
git commit -m "feat(rag): mappe content_hash en base (init.sql + migration + config EF Core)"
```

---

### Task 4: `DuplicateDocumentException` + extension de `UploadDocumentCommand`

**Files:**
- Create: `Step 3/src/Back/AIExperience.Rag.Application/Document/Exceptions/DuplicateDocumentException.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentCommand.cs:6-14`

**Interfaces:**
- Consumes: rien de nouveau.
- Produces: `DuplicateDocumentException` avec propriétés `ExistingDocumentId` (Guid), `ExistingFileName` (string), `ExistingCreatedAt` (DateTimeOffset) — levée par `UploadDocumentHandler` (Task 5), attrapée par `DocumentsController` (Task 7). `UploadDocumentCommand.ContentHash` (string, required) et `ReplaceDocumentId` (Guid?) — consommés par `UploadDocumentHandler` (Task 5).

- [ ] **Step 1: Créer l'exception**

Créer `Step 3/src/Back/AIExperience.Rag.Application/Document/Exceptions/DuplicateDocumentException.cs` :

```csharp
namespace AIExperience.Rag.Application.Document.Exceptions;

/// <summary>
/// Levée par <see cref="Command.UploadDocumentHandler"/> lorsqu'un document identique
/// (même nom de fichier et même hash de contenu) existe déjà et que l'appelant n'a pas confirmé
/// son remplacement via <see cref="Command.UploadDocumentCommand.ReplaceDocumentId"/>.
/// </summary>
public sealed class DuplicateDocumentException(Guid existingDocumentId, string existingFileName, DateTimeOffset existingCreatedAt)
    : Exception($"Un document identique (\"{existingFileName}\") existe déjà (Id: {existingDocumentId}).")
{
    /// <summary>Identifiant du document existant en conflit.</summary>
    public Guid ExistingDocumentId { get; } = existingDocumentId;

    /// <summary>Nom de fichier du document existant en conflit.</summary>
    public string ExistingFileName { get; } = existingFileName;

    /// <summary>Date de création du document existant en conflit.</summary>
    public DateTimeOffset ExistingCreatedAt { get; } = existingCreatedAt;
}
```

- [ ] **Step 2: Étendre `UploadDocumentCommand`**

Remplacer le contenu de `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentCommand.cs` :

```csharp
using AIExperience.Rag.Domain.Enums;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

public sealed record UploadDocumentCommand : IRequest<UploadDocumentResponse>
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string UserId { get; init; }
    public required DocumentMetadata DocumentMetadata { get; init; }
    public ChunkingStrategy ChunkingStrategy { get; init; } = ChunkingStrategy.Recursive;

    /// <summary>Empreinte SHA-256 du contenu du fichier, calculée côté serveur (détection de doublon).</summary>
    public required string ContentHash { get; init; }

    /// <summary>Identifiant du document existant à remplacer, si l'utilisateur a confirmé le remplacement d'un doublon.</summary>
    public Guid? ReplaceDocumentId { get; init; }
}
```

- [ ] **Step 3: Vérifier la compilation**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Échec attendu — `UploadDocumentHandler` et `DocumentsController` instancient encore `UploadDocumentCommand` sans `ContentHash` (propriété `required`). C'est normal : Task 5 et Task 7 corrigent ces deux appelants. Noter les erreurs de compilation pour confirmer qu'elles ne concernent que ces deux fichiers.

- [ ] **Step 4: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Document/Exceptions/DuplicateDocumentException.cs" "Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentCommand.cs"
git commit -m "feat(rag): ajoute DuplicateDocumentException et etend UploadDocumentCommand (ContentHash, ReplaceDocumentId)"
```

---

### Task 4b: Corriger le second appelant de `UploadDocumentCommand` dans `AIExperience.App.Console`

> **Ajouté pendant l'exécution** (non prévu dans la conception initiale) : `UploadDocumentCommand.ContentHash` étant devenu `required` (Task 4), l'implémenteur de Task 4 a détecté que `AIExperience.App.Console/Program.cs` construit aussi une `UploadDocumentCommand` (racine de composition DI console, indépendante du contrôleur web) et casse la compilation. Ce caller n'était pas couvert par le plan initial — cette tâche comble ce trou avant de continuer.

**Files:**
- Modify: `Step 3/src/Back/AIExperience.App.Console/Program.cs:90-104` (construction de la commande) et ajout d'une fonction locale près de `GetContentTypeOfFileName` (ligne 219).

**Interfaces:**
- Consumes: `UploadDocumentCommand.ContentHash` (Task 4).
- Produces: rien (racine de composition, aucun consommateur ultérieur). Le `catch (Exception ex)` déjà existant (ligne 117-120) attrape déjà `DuplicateDocumentException` (sous-classe d'`Exception`) sans modification — un doublon rencontré dans un dossier d'ingestion batch affiche simplement le message d'erreur français de l'exception et passe au fichier suivant, comportement déjà correct pour ce cas d'usage (pas de UX de remplacement en console, hors périmètre).

- [ ] **Step 1: Ajouter le calcul de hash**

Dans `Step 3/src/Back/AIExperience.App.Console/Program.cs`, remplacer les lignes 94-104 :

```csharp
            var fileInfo = new FileInfo(filePath);
            var contentHash = await ComputeSha256Async(filePath);

            var uploadDocumentResponse = await senderService.Send(new UploadDocumentCommand
            {
                FileName = fileInfo.Name,
                ContentType = GetContentTypeOfFileName(fileInfo.Name),
                FileSizeBytes = fileInfo.Length,
                UserId = UserId,
                DocumentMetadata = new DocumentMetadata { Title = fileInfo.Name },
                ChunkingStrategy = ChunkingStrategy.Recursive,
                ContentHash = contentHash
            });
```

Ajouter la fonction locale après `GetContentTypeOfFileName` (après la ligne 225) :

```csharp
/// <summary>Calcule le SHA-256 (hex minuscule) du fichier — même logique que DocumentsController (détection de doublon).</summary>
async Task<string> ComputeSha256Async(string filePath)
{
    await using var stream = File.OpenRead(filePath);
    var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
}
```

- [ ] **Step 2: Vérifier la compilation de la solution complète**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: seule erreur restante attendue : `DocumentsController.cs` (corrigé en Task 7, pas encore exécutée). Confirmer qu'aucune autre erreur n'apparaît, notamment dans `AIExperience.App.Console`.

- [ ] **Step 3: Commit**

```bash
git add "Step 3/src/Back/AIExperience.App.Console/Program.cs"
git commit -m "fix(rag): App.Console calcule le hash pour le nouvel UploadDocumentCommand.ContentHash"
```

---

### Task 5: Règle métier dans `UploadDocumentHandler` (TDD)

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentHandler.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs` (nouveau fichier)

**Interfaces:**
- Consumes: `IDocumentRepository.GetByFileNameAndHashAsync` (Task 2), `DuplicateDocumentException` et `UploadDocumentCommand.ContentHash`/`ReplaceDocumentId` (Task 4), `Document.Create(..., contentHash:)` (Task 1).
- Produces: `UploadDocumentHandler.Handle` avec la règle complète (bloquer / remplacer / créer) — consommé par `DocumentsController` (Task 7).

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs` :

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
/// Tests TDD pour <see cref="UploadDocumentHandler"/> : vérifie la règle de détection de doublon
/// (nom + hash) et le remplacement atomique lorsqu'un ReplaceDocumentId valide est fourni.
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

        public Task<Document?> GetByFileNameAndHashAsync(string userId, string fileName, string contentHash, CancellationToken ct = default)
            => Task.FromResult(Documents.FirstOrDefault(d =>
                d.UserId == userId && d.FileName == fileName && d.ContentHash == contentHash));

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

    private static UploadDocumentCommand CreateCommand(string contentHash, Guid? replaceDocumentId = null) => new()
    {
        FileName = "rapport.pdf",
        ContentType = "application/pdf",
        FileSizeBytes = 1024,
        UserId = "user-1",
        DocumentMetadata = DocumentMetadata.Create(title: "Rapport"),
        ContentHash = contentHash,
        ReplaceDocumentId = replaceDocumentId
    };

    [Fact]
    public async Task Handle_NoExistingDocument_CreatesDocumentNormally()
    {
        var repository = new FakeDocumentRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new UploadDocumentHandler(repository, unitOfWork);

        var response = await handler.Handle(CreateCommand("hash-a"), CancellationToken.None);

        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
        unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_DuplicateExistsWithoutReplaceId_ThrowsDuplicateDocumentException()
    {
        var repository = new FakeDocumentRepository();
        var existing = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-a");
        repository.Documents.Add(existing);
        var handler = new UploadDocumentHandler(repository, new FakeUnitOfWork());

        var act = () => handler.Handle(CreateCommand("hash-a"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DuplicateDocumentException>();
        exception.Which.ExistingDocumentId.Should().Be(existing.Id);
        repository.Documents.Should().ContainSingle(); // aucune création, aucune suppression
    }

    [Fact]
    public async Task Handle_DuplicateExistsWithMatchingReplaceId_DeletesOldAndCreatesNewInSingleTransaction()
    {
        var repository = new FakeDocumentRepository();
        var existing = Document.Create("rapport.pdf", "application/pdf", 1024, "user-1",
            DocumentMetadata.Create(title: "Rapport"), contentHash: "hash-a");
        repository.Documents.Add(existing);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new UploadDocumentHandler(repository, unitOfWork);

        var response = await handler.Handle(CreateCommand("hash-a", existing.Id), CancellationToken.None);

        repository.DeletedIds.Should().ContainSingle().Which.Should().Be(existing.Id);
        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
        response.DocumentId.Should().NotBe(existing.Id);
        unitOfWork.SaveChangesCallCount.Should().Be(1, "suppression et création doivent être atomiques en une seule transaction");
    }

    [Fact]
    public async Task Handle_ReplaceIdProvidedButNoDuplicateFound_CreatesDocumentNormally()
    {
        var repository = new FakeDocumentRepository();
        var handler = new UploadDocumentHandler(repository, new FakeUnitOfWork());
        var staleReplaceId = Guid.NewGuid();

        var response = await handler.Handle(CreateCommand("hash-a", staleReplaceId), CancellationToken.None);

        repository.DeletedIds.Should().BeEmpty();
        repository.Documents.Should().ContainSingle(d => d.Id == response.DocumentId);
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter UploadDocumentHandlerTests`
Expected: FAIL — `IDocumentRepository` n'a pas encore... (en fait Task 2 l'a déjà ajouté) mais `UploadDocumentHandler.Handle` ne contient pas encore la logique de doublon : `Handle_DuplicateExistsWithoutReplaceId_ThrowsDuplicateDocumentException` et `Handle_DuplicateExistsWithMatchingReplaceId_...` échouent (aucune exception levée, aucune suppression).

- [ ] **Step 3: Implémenter la règle métier**

Remplacer le contenu de `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentHandler.cs` :

```csharp
using AIExperience.Rag.Application.Document.Exceptions;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Handler MediatR pour la commande <see cref="UploadDocumentCommand"/>.
/// Applique la règle de détection de doublon (nom de fichier + hash de contenu) : bloque la création
/// si un doublon existe sans confirmation de remplacement, ou supprime l'ancien document et crée
/// le nouveau de façon atomique (une seule transaction) si le remplacement est confirmé.
/// </summary>
public sealed class UploadDocumentHandler(
    IDocumentRepository documentRepository, IUnitOfWork unitOfWork) : IRequestHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    /// <summary>
    /// Exécute la détection de doublon puis la création (ou le remplacement) du document.
    /// </summary>
    /// <param name="request">Commande contenant les données du fichier uploadé, son hash et un éventuel ReplaceDocumentId.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Réponse contenant l'identifiant et le statut initial du document.</returns>
    /// <exception cref="DuplicateDocumentException">Un doublon existe et n'a pas été confirmé comme remplaçable.</exception>
    public async Task<UploadDocumentResponse> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var existingDuplicate = await documentRepository.GetByFileNameAndHashAsync(
            request.UserId, request.FileName, request.ContentHash, cancellationToken);

        if (existingDuplicate is not null && existingDuplicate.Id != request.ReplaceDocumentId)
        {
            throw new DuplicateDocumentException(
                existingDuplicate.Id, existingDuplicate.FileName, existingDuplicate.CreatedAt);
        }

        if (existingDuplicate is not null)
        {
            // Remplacement confirmé : suppression de l'ancien document avant création du nouveau,
            // le tout validé par un seul SaveChangesAsync ci-dessous (une seule transaction atomique).
            await documentRepository.DeleteAsync(existingDuplicate.Id, cancellationToken);
        }

        var document = Domain.Entities.Document.Create(
          request.FileName,
          request.ContentType,
          request.FileSizeBytes,
          request.UserId,
          request.DocumentMetadata,
          request.ChunkingStrategy,
          request.ContentHash);

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
Expected: PASS (4 tests).

- [ ] **Step 5: Lancer toute la suite (le build échouait depuis Task 4, il doit maintenant réussir pour ce fichier)**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Seule erreur restante attendue : `DocumentsController.cs` (corrigé en Task 7). Confirmer qu'aucune autre erreur n'apparaît.

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentHandler.cs" "Step 3/src/Back/AIExperience.Tests/UploadDocumentHandlerTests.cs"
git commit -m "feat(rag): implemente la regle de detection/remplacement de doublon dans UploadDocumentHandler"
```

---

### Task 6: DTOs pour le contrôleur

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs:1-18`

**Interfaces:**
- Consumes: rien.
- Produces: `CheckDuplicateRequest(string FileName, string ContentHash)`, `ExistingDocumentInfo(Guid Id, string FileName, DateTimeOffset CreatedAt)`, `CheckDuplicateResponse(bool IsDuplicate, ExistingDocumentInfo? ExistingDocument)`, `DuplicateDocumentResponse(ExistingDocumentInfo ExistingDocument)` — tous consommés par `DocumentsController` (Task 7).

- [ ] **Step 1: Ajouter les DTOs**

Dans `Step 3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs`, ajouter à la fin du fichier (après `ReembedCorpusResponse`, ligne 17) :

```csharp

/// <summary>Requête de pré-vérification de doublon (nom de fichier + hash calculé côté navigateur).</summary>
public record CheckDuplicateRequest(string FileName, string ContentHash);

/// <summary>Informations minimales sur un document existant, utilisées pour informer l'utilisateur d'un doublon.</summary>
public record ExistingDocumentInfo(Guid Id, string FileName, DateTimeOffset CreatedAt);

/// <summary>Réponse de la pré-vérification de doublon.</summary>
public record CheckDuplicateResponse(bool IsDuplicate, ExistingDocumentInfo? ExistingDocument);

/// <summary>Corps de la réponse 409 Conflict renvoyée lorsque l'upload est bloqué par un doublon non confirmé.</summary>
public record DuplicateDocumentResponse(ExistingDocumentInfo ExistingDocument);
```

- [ ] **Step 2: Vérifier la compilation**

Run: `dotnet build "Step 3/src/Back/AIExperience.Web.Api/AIExperience.Web.Api.csproj"`
Expected: Build réussi pour ce fichier isolément (le contrôleur n'utilise pas encore ces DTOs — corrigé en Task 7 — donc pas d'erreur nouvelle ici).

- [ ] **Step 3: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs"
git commit -m "feat(rag): ajoute les DTOs de detection de doublon (CheckDuplicate*, DuplicateDocumentResponse)"
```

---

### Task 7: `DocumentsController` — hash serveur, 409, endpoint `check-duplicate`

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs`

**Interfaces:**
- Consumes: `UploadDocumentCommand.ContentHash`/`ReplaceDocumentId` (Task 4), `DuplicateDocumentException` (Task 4), `IDocumentRepository.GetByFileNameAndHashAsync` (Task 2), DTOs `CheckDuplicateRequest`/`CheckDuplicateResponse`/`ExistingDocumentInfo`/`DuplicateDocumentResponse` (Task 6).
- Produces: `POST /api/documents?replaceDocumentId=<guid?>` (paramètre ajouté), `POST /api/documents/check-duplicate` (nouvel endpoint) — consommés par le front-end (Task 8-9).

- [ ] **Step 1: Modifier le contrôleur**

Remplacer le contenu de `Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs` :

```csharp
using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Document.Exceptions;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Web.Api.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System.Security.Cryptography;

namespace AIExperience.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(
    ISender sender,
    IIngestionService ingestionService,
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork) : ControllerBase
{
    private const string DefaultUserId = "1ea95468-3f27-4a6d-8fb3-25fdd1530023";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentResponse>>> GetAll()
    {
        // GetAllAsync retourne tous les documents sans filtre utilisateur (auth non implémentée)
        var documents = await documentRepository.GetAllAsync();
        return Ok(documents.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentResponse>> GetById(Guid id)
    {
        var doc = await documentRepository.GetByIdAsync(id);
        return doc is null ? NotFound() : Ok(ToResponse(doc));
    }

    /// <summary>
    /// Pré-vérification de doublon (nom + hash calculé côté navigateur), appelée par le front avant
    /// d'envoyer le fichier complet. Purement informatif — l'autorité finale reste <see cref="Upload"/>.
    /// </summary>
    [HttpPost("check-duplicate")]
    public async Task<ActionResult<CheckDuplicateResponse>> CheckDuplicate([FromBody] CheckDuplicateRequest request)
    {
        var existing = await documentRepository.GetByFileNameAndHashAsync(DefaultUserId, request.FileName, request.ContentHash);
        return Ok(existing is null
            ? new CheckDuplicateResponse(false, null)
            : new CheckDuplicateResponse(true, new ExistingDocumentInfo(existing.Id, existing.FileName, existing.CreatedAt)));
    }

    [HttpPost]
    public async Task<ActionResult<DocumentResponse>> Upload(
        IFormFile file,
        [FromQuery] ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        [FromQuery] Guid? replaceDocumentId = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Fichier manquant.");

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(file.FileName));

        await using (var stream = System.IO.File.Create(tempPath))
            await file.CopyToAsync(stream);

        try
        {
            var contentHash = await ComputeSha256Async(tempPath);

            UploadDocumentResponse uploadResponse;
            try
            {
                uploadResponse = await sender.Send(new UploadDocumentCommand
                {
                    FileName = file.FileName,
                    ContentType = GetContentType(file.FileName),
                    FileSizeBytes = file.Length,
                    UserId = DefaultUserId,
                    DocumentMetadata = new DocumentMetadata { Title = file.FileName },
                    ChunkingStrategy = strategy,
                    ContentHash = contentHash,
                    ReplaceDocumentId = replaceDocumentId
                });
            }
            catch (DuplicateDocumentException ex)
            {
                return Conflict(new DuplicateDocumentResponse(
                    new ExistingDocumentInfo(ex.ExistingDocumentId, ex.ExistingFileName, ex.ExistingCreatedAt)));
            }

            var doc = await documentRepository.GetByIdAsync(uploadResponse.DocumentId);
            if (doc is null) return StatusCode(500, "Document introuvable après création.");

            try
            {
                await ingestionService.IngestAsync(tempPath, uploadResponse.DocumentId,
                    new DocumentMetadata { Title = file.FileName });
                doc.MarkAsCompleted();
            }
            catch (Exception ex)
            {
                doc.MarkAsFailed(ex.Message);
            }

            await documentRepository.UpdateAsync(doc);
            await unitOfWork.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = doc.Id }, ToResponse(doc));
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await sender.Send(new DeleteDocumentCommand { DocumentId = id });
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// À appeler une fois après activation de <c>AI.EmbeddingTaskPrefixes</c> : les vecteurs stockés sans préfixe sont incompatibles
    /// avec des questions désormais préfixées "search_query: ". N'extrait/ne re-chunk rien —
    /// seul l'embedding de chaque chunk existant est recalculé.
    /// </summary>
    [HttpPost("reembed-corpus")]
    public async Task<ActionResult<ReembedCorpusResponse>> ReembedCorpus(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReembedCorpusCommand(), cancellationToken);
        return Ok(new ReembedCorpusResponse(result.ChunksReembedded, result.DurationMs));
    }

    private static DocumentResponse ToResponse(AIExperience.Rag.Domain.Entities.Document d) =>
        new(d.Id, d.FileName, d.ContentType, d.FileSizeBytes, d.Status.ToString(), d.CreatedAt);

    private static string GetContentType(string fileName)
    {
        var provider = new FileExtensionContentTypeProvider();
        return provider.TryGetContentType(fileName, out var ct) ? ct : "application/octet-stream";
    }

    /// <summary>Calcule le SHA-256 (hex minuscule) du fichier temporaire déjà reçu — source de vérité pour la détection de doublon.</summary>
    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = System.IO.File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
```

- [ ] **Step 2: Vérifier la compilation**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build réussi, 0 erreur.

- [ ] **Step 3: Lancer toute la suite de tests back-end**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: PASS (tous les tests, y compris ceux des Tasks 1 et 5).

- [ ] **Step 4: Vérification manuelle via Scalar**

Démarrer l'API (`dotnet run --project "Step 3/src/Back/AIExperience.Web.Api"`), ouvrir `http://localhost:50406/scalar/v1`, et :
1. Uploader un PDF quelconque via `POST /api/documents` → succès (201), noter le `fileName`.
2. Ré-uploader **le même fichier** sans `replaceDocumentId` → `409 Conflict` avec le corps `{ "existingDocument": { "id": "...", "fileName": "...", "createdAt": "..." } }`.
3. Ré-uploader avec `?replaceDocumentId=<id retourné à l'étape 2>` → succès (201) avec un **nouvel** `Id` différent de l'original.
4. Appeler `POST /api/documents/check-duplicate` avec `{ "fileName": "...", "contentHash": "..." }` (utiliser le hash affiché dans la réponse 409 si besoin, ou recalculer via `sha256sum`) → `isDuplicate: true` avec les infos du document courant.

- [ ] **Step 5: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs"
git commit -m "feat(rag): controller upload calcule le hash serveur, gere le 409 et expose check-duplicate"
```

---

### Task 8: Front-end — types et client API

**Files:**
- Modify: `Step 3/src/Front/src/types/index.ts:1-9`
- Modify: `Step 3/src/Front/src/api/client.ts:1-27`

**Interfaces:**
- Consumes: `POST /api/documents/check-duplicate`, `POST /api/documents?replaceDocumentId=` (Task 7).
- Produces: `ExistingDocumentInfo` (type TS), `CheckDuplicateResponse` (type TS), `api.documents.checkDuplicate(fileName, contentHash)`, `api.documents.upload(file, strategy?, replaceDocumentId?)` — consommés par `DocumentsPage.tsx` (Task 9).

- [ ] **Step 1: Ajouter les types**

Dans `Step 3/src/Front/src/types/index.ts`, ajouter après l'interface `DocumentResponse` (ligne 8) :

```typescript
/** Informations minimales sur un document existant en conflit (détection de doublon). */
export interface ExistingDocumentInfo {
  id: string;
  fileName: string;
  createdAt: string;
}

/** Réponse de la pré-vérification de doublon avant upload. */
export interface CheckDuplicateResponse {
  isDuplicate: boolean;
  existingDocument: ExistingDocumentInfo | null;
}
```

- [ ] **Step 2: Étendre le client API**

Dans `Step 3/src/Front/src/api/client.ts`, modifier l'import (ligne 1) :

```typescript
import type { AskQuestionRequest, AskQuestionResponse, CheckDuplicateResponse, DocumentResponse, StreamEvent, SystemPromptsResponse, TranscribeVideoResponse } from '../types';
```

Remplacer le bloc `documents` (lignes 16-27) :

```typescript
  documents: {
    list: () => request<DocumentResponse[]>('/api/documents'),
    // Pré-vérification légère avant upload : hash calculé côté navigateur, pas d'envoi du fichier complet.
    checkDuplicate: (fileName: string, contentHash: string) =>
      request<CheckDuplicateResponse>('/api/documents/check-duplicate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileName, contentHash }),
      }),
    upload: (file: File, strategy = 'Recursive', replaceDocumentId?: string) => {
      const body = new FormData();
      body.append('file', file);
      const params = new URLSearchParams({ strategy });
      if (replaceDocumentId) params.set('replaceDocumentId', replaceDocumentId);
      return request<DocumentResponse>(`/api/documents?${params}`, {
        method: 'POST',
        body,
      });
    },
    delete: (id: string) => request<void>(`/api/documents/${id}`, { method: 'DELETE' }),
  },
```

- [ ] **Step 3: Vérifier la compilation TypeScript**

Run: `cd "Step 3/src/Front" && npx tsc -b`
Expected: 0 erreur de type (aucun appelant de `api.documents.upload` ne casse, car les nouveaux paramètres sont optionnels).

- [ ] **Step 4: Commit**

```bash
git add "Step 3/src/Front/src/types/index.ts" "Step 3/src/Front/src/api/client.ts"
git commit -m "feat(rag): ajoute checkDuplicate et replaceDocumentId au client API front-end"
```

---

### Task 9: Front-end — popup de confirmation dans `DocumentsPage.tsx`

**Files:**
- Modify: `Step 3/src/Front/src/pages/DocumentsPage.tsx`

**Interfaces:**
- Consumes: `api.documents.checkDuplicate`, `api.documents.upload(file, strategy, replaceDocumentId)` (Task 8), type `ExistingDocumentInfo` (Task 8).
- Produces: flux utilisateur complet (aucun consommateur ultérieur dans ce plan).

- [ ] **Step 1: Ajouter l'import du type et l'état du composant**

Dans `Step 3/src/Front/src/pages/DocumentsPage.tsx`, modifier l'import (ligne 4) :

```typescript
import type { DocumentResponse, ExistingDocumentInfo } from '../types';
```

Ajouter un nouvel état après `const [toast, setToast] = useState<string | null>(null);` (ligne 19) :

```typescript
  // Doublon détecté en attente de confirmation utilisateur (fichier + infos du document existant).
  const [pendingDuplicate, setPendingDuplicate] = useState<{ file: File; existing: ExistingDocumentInfo } | null>(null);
```

- [ ] **Step 2: Ajouter la fonction de calcul de hash**

Ajouter avant `handleUpload` (avant la ligne 47) :

```typescript
  /** Calcule le SHA-256 (hex minuscule) d'un fichier côté navigateur, pour la pré-vérification de doublon. */
  async function computeSha256(file: File): Promise<string> {
    const buffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest('SHA-256', buffer);
    return Array.from(new Uint8Array(hashBuffer))
      .map(b => b.toString(16).padStart(2, '0'))
      .join('');
  }
```

- [ ] **Step 3: Remplacer `handleUpload` et ajouter les handlers de confirmation/annulation**

Remplacer `handleUpload` (lignes 47-61) par :

```typescript
  async function handleUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true);
    setError(null);
    try {
      // Si crypto.subtle est indisponible (contexte non sécurisé), on saute la pré-vérification :
      // le 409 renvoyé par le back-end (garde-fou serveur) reste la protection en dernier recours.
      let hash: string | null = null;
      try {
        hash = await computeSha256(file);
      } catch {
        hash = null;
      }

      if (hash) {
        const check = await api.documents.checkDuplicate(file.name, hash);
        if (check.isDuplicate && check.existingDocument) {
          setPendingDuplicate({ file, existing: check.existingDocument });
          return;
        }
      }

      await api.documents.upload(file);
      await loadDocuments();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = '';
    }
  }

  /** Confirme le remplacement du document existant par le nouveau fichier (doublon détecté). */
  async function handleConfirmReplace() {
    if (!pendingDuplicate) return;
    const { file, existing } = pendingDuplicate;
    setUploading(true);
    setError(null);
    try {
      await api.documents.upload(file, 'Recursive', existing.id);
      await loadDocuments();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setUploading(false);
      setPendingDuplicate(null);
      if (fileRef.current) fileRef.current.value = '';
    }
  }

  /** Annule le remplacement : ferme la popup sans appel réseau. */
  function handleCancelReplace() {
    setPendingDuplicate(null);
    if (fileRef.current) fileRef.current.value = '';
  }
```

- [ ] **Step 4: Ajouter la popup JSX**

Ajouter après le bloc du modal de suppression existant (après la fermeture `)}` qui suit `modal-actions` du modal de suppression, juste avant le bloc `{toast && (...)}`) :

```tsx
      {pendingDuplicate && (
        <div className="modal-overlay" onClick={() => !uploading && handleCancelReplace()}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>Document déjà importé</h2>
            <p>
              Un document nommé « {pendingDuplicate.existing.fileName} » a déjà été importé le{' '}
              {new Date(pendingDuplicate.existing.createdAt).toLocaleDateString('fr-FR')}.
            </p>
            <p className="modal-warning">
              Si vous continuez, ce document existant sera supprimé et remplacé par le nouveau fichier.
            </p>
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

- [ ] **Step 5: Vérifier la compilation TypeScript**

Run: `cd "Step 3/src/Front" && npx tsc -b`
Expected: 0 erreur.

- [ ] **Step 6: Vérification manuelle dans le navigateur**

Démarrer back (`dotnet run --project "Step 3/src/Back/AIExperience.Web.Api"`) et front (`cd "Step 3/src/Front" && npm run dev`, puis ouvrir `http://localhost:5173`) :
1. Importer un document quelconque → apparaît dans la liste normalement.
2. Réimporter **le même fichier** → la popup « Document déjà importé » apparaît, avec le bon nom et la bonne date.
3. Cliquer **Annuler** → popup se ferme, aucun nouveau document créé, liste inchangée.
4. Réimporter **le même fichier** à nouveau, cliquer **Confirmer le remplacement** → l'ancien document disparaît de la liste, un nouveau document (nouvel Id, visible en rechargeant ou via le statut qui repasse par Pending→Processing→Completed) apparaît à sa place.
5. Importer un fichier avec un **nom différent** → aucune popup, upload direct comme avant (pas de régression).

- [ ] **Step 7: Commit**

```bash
git add "Step 3/src/Front/src/pages/DocumentsPage.tsx"
git commit -m "feat(rag): popup de confirmation de remplacement de document en doublon"
```

---

## Vérification finale

- [ ] `dotnet test "Step 3/src/Back/AIExperience.slnx"` → tous les tests passent (existants + nouveaux : `DocumentContentHashTests`, `UploadDocumentHandlerTests`).
- [ ] `dotnet build "Step 3/src/Back/AIExperience.slnx"` → 0 warning nouveau, 0 erreur.
- [ ] `cd "Step 3/src/Front" && npx tsc -b` → 0 erreur.
- [ ] Scénario manuel complet rejoué une dernière fois (upload normal / doublon annulé / doublon confirmé / migration SQL appliquée sur la base de dev).
- [ ] Rappel : ne pas pousser/committer au-delà de ce qui a été explicitement validé par l'utilisateur — reconfirmer avant tout `git push`.
