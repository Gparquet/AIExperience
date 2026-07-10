# Page de détail & suivi fin d'ingestion — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter une page `/documents/:id` qui affiche l'avancement fin (étapes, pourcentages, compteurs) de l'ingestion d'un document en temps réel, pour tous les états (en cours, terminé, échec).

**Architecture:** Un `IIngestionProgressReporter` (Domain) est appelé par le pipeline d'ingestion à chaque transition d'étape et pendant les étapes longues (transcription Whisper, embedding en sous-lots). Il **persiste** l'avancement dans une colonne JSONB `ingestion_progress` de `documents` (écriture ciblée, throttlée) **et notifie** le front via un nouvel event SignalR `documentProgressChanged`. Le front s'abonne à cet event depuis une page de détail dédiée et retombe sur `GET /api/documents/{id}` au chargement / en repli.

**Tech Stack:** .NET 10 / EF Core 10 (Npgsql, JSONB) / SignalR / MediatR ; React 19 + TypeScript + Vite + React Router v7 + `@microsoft/signalr`.

## Global Constraints

- **Langue** : tout le code et tous les commentaires en **français** (règle projet). Commentaires XML `///` en C#, JSDoc/inline en TS.
- **Périmètre code** : exclusivement `Step3/` (Step 1 et Step 2 ne sont pas touchés).
- **Entités** : setters privés, création via fabrique statique, jamais de constructeur public à paramètres.
- **CQRS** : commandes via MediatR ; lectures via service/repository direct. Ne jamais relire le repository dans un contrôleur pour reconstruire la réponse d'une écriture.
- **Options Pattern** : toute config = classe `*Options` liée à une section `appsettings.json`.
- **DI** : enregistrement dans les méthodes d'extension `AddInfrastructure()` / `AddApplication()`, jamais de `new` pour un service.
- **Vérification back-end** : `dotnet build "Step3/src/Back/AIExperience.slnx"` doit toujours passer. `dotnet test` peut être **bloqué par Smart App Control sur cette machine** ; si `dotnet test` ne s'exécute pas, considérer le build vert + la revue de code du test comme la vérification de repli, mais **écrire les tests malgré tout**.
- **Vérification front** : le front Step3 n'a **pas** de runner de tests ; la vérification se fait via `npm run build` (compilation TypeScript stricte) dans `Step3/src/Front` + smoke manuel. Ne pas introduire de framework de test front (hors périmètre).
- **Schéma DB** : pas de migrations EF Core dans ce projet — le schéma est géré à la main via `scripts/init.sql` + un script de migration additionnel.
- **Commits** : fréquents, un par tâche (ou par étape logique). Terminer les messages de commit par `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## Structure des fichiers

**Back-end — créés :**
- `Step3/src/Back/AIExperience.Rag.Domain/Enums/IngestionStage.cs` — enum canonique des étapes.
- `Step3/src/Back/AIExperience.Rag.Domain/Models/IngestionProgress.cs` — objet valeur (étape + % + compteurs).
- `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionProgressReporter.cs` — abstraction de report.
- `Step3/src/Back/AIExperience.Rag.Infrastructure/Notifications/IngestionProgressReporter.cs` — implémentation (persistance throttlée + notification).
- `Step3/scripts/migrate-ingestion-progress.sql` — ajout colonne JSONB.
- Tests : `Step3/src/Back/AIExperience.Tests/IngestionProgressReporterTests.cs`, `IngestionServiceEmbeddingProgressTests.cs`.

**Back-end — modifiés :**
- `Document.cs` (propriété lecture seule `IngestionProgress`), `IIngestionNotifier.cs` (+`NotifyProgressAsync`), `SignalRIngestionNotifier.cs`, `NullIngestionNotifier.cs`, `DocumentConfiguration.cs` (mapping JSONB), `IngestionOptions.cs` (+2 options), `IngestionService.cs` (étapes + embedding sous-lots), `ITranscriptionService.cs` + `WhisperTranscriptionService.cs` (callback segment), `ProcessVideoTranscriptionJobHandler.cs` (étapes vidéo), `DocumentDtos.cs` + `DocumentsController.cs` (DTO enrichi), `Infrastructure/DependencyInjection.cs` (enregistrement reporter), `scripts/init.sql` (colonne).

**Front — créés :**
- `Step3/src/Front/src/pages/DocumentDetailPage.tsx` — la page de détail.
- `Step3/src/Front/src/components/IngestionStepper.tsx` — le stepper vertical.

**Front — modifiés :**
- `types/index.ts`, `api/client.ts` (déjà `get`), `context/IngestionNotificationsContext.tsx` (+`subscribeProgress`), `App.tsx` (route), `pages/DocumentsPage.tsx` (liens cliquables), `index.css` (styles).

---

## TÂCHES BACK-END

### Task 1 : Enum `IngestionStage` + objet valeur `IngestionProgress`

**Files:**
- Create: `Step3/src/Back/AIExperience.Rag.Domain/Enums/IngestionStage.cs`
- Create: `Step3/src/Back/AIExperience.Rag.Domain/Models/IngestionProgress.cs`

**Interfaces:**
- Produces: `enum IngestionStage { Queued, ExtractingAudio, Transcribing, ExtractingText, Chunking, Embedding, Storing, Completed, Failed }` ; `sealed record IngestionProgressCounters(int? BatchIndex, int? BatchCount, int? ChunksDone, int? ChunksTotal, int? SegmentsDone, int? SegmentsTotal)` ; `sealed record IngestionProgress(IngestionStage Stage, int? Percent, IngestionProgressCounters Counters, DateTimeOffset UpdatedAt)` avec fabrique `IngestionProgress.Create(IngestionStage stage, int? percent = null, IngestionProgressCounters? counters = null)`.

- [ ] **Step 1 : Créer l'enum**

```csharp
namespace AIExperience.Rag.Domain.Enums;

/// <summary>
/// Étapes fines du pipeline d'ingestion, exposées à l'utilisateur pour lui montrer « où ça en est ».
/// Toutes les étapes ne s'appliquent pas à tous les types de documents : un PDF passe par
/// <see cref="ExtractingText"/>, une vidéo par <see cref="ExtractingAudio"/> puis <see cref="Transcribing"/>.
/// </summary>
public enum IngestionStage
{
    /// <summary>En file d'attente : le message outbox n'a pas encore été dépilé par le worker.</summary>
    Queued,
    /// <summary>Extraction de la piste audio d'une vidéo via FFmpeg (vidéo uniquement).</summary>
    ExtractingAudio,
    /// <summary>Transcription audio → texte via Whisper (vidéo/audio) — étape à pourcentage.</summary>
    Transcribing,
    /// <summary>Extraction du texte d'un document (PDF, etc.) — documents non vidéo.</summary>
    ExtractingText,
    /// <summary>Découpage du texte en chunks.</summary>
    Chunking,
    /// <summary>Vectorisation des chunks en sous-lots — étape à pourcentage.</summary>
    Embedding,
    /// <summary>Écriture des chunks + vecteurs dans pgvector.</summary>
    Storing,
    /// <summary>Pipeline terminé avec succès.</summary>
    Completed,
    /// <summary>Pipeline interrompu par une erreur.</summary>
    Failed
}
```

- [ ] **Step 2 : Créer l'objet valeur de progression**

```csharp
using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Compteurs optionnels décrivant l'avancement fin d'une étape. Chaque champ n'a de sens que pour
/// certaines étapes (les lots pour l'embedding, les segments pour la transcription) et reste
/// <c>null</c> ailleurs.
/// </summary>
public sealed record IngestionProgressCounters(
    int? BatchIndex = null,
    int? BatchCount = null,
    int? ChunksDone = null,
    int? ChunksTotal = null,
    int? SegmentsDone = null,
    int? SegmentsTotal = null)
{
    /// <summary>Instance sans aucun compteur renseigné (étapes atomiques).</summary>
    public static readonly IngestionProgressCounters Empty = new();
}

/// <summary>
/// État instantané de l'avancement d'une ingestion : l'étape courante, son pourcentage (si connu)
/// et des compteurs de détail. Persisté en base (colonne JSONB) et poussé au front en temps réel.
/// </summary>
public sealed record IngestionProgress(
    IngestionStage Stage,
    int? Percent,
    IngestionProgressCounters Counters,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Fabrique un instantané de progression horodaté à l'instant courant (UTC).
    /// </summary>
    /// <param name="stage">Étape en cours.</param>
    /// <param name="percent">Pourcentage de l'étape (0-100), ou <c>null</c> si indéterminé/atomique.</param>
    /// <param name="counters">Compteurs de détail, ou <c>null</c> pour aucun.</param>
    public static IngestionProgress Create(
        IngestionStage stage,
        int? percent = null,
        IngestionProgressCounters? counters = null)
        => new(stage, percent, counters ?? IngestionProgressCounters.Empty, DateTimeOffset.UtcNow);
}
```

- [ ] **Step 3 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: BUILD succeeded.

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Domain/Enums/IngestionStage.cs" "Step3/src/Back/AIExperience.Rag.Domain/Models/IngestionProgress.cs"
git commit -m "feat(domain): étape et objet valeur de progression d'ingestion"
```

---

### Task 2 : Exposer `IngestionProgress` en lecture sur l'entité `Document`

Le reporter écrira la colonne via une mise à jour ciblée (Task 5) ; l'entité n'a donc besoin que de
**lire** la valeur (pour l'API `GET /documents/{id}`). Aucune méthode de mutation domaine.

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs`
- Test: `Step3/src/Back/AIExperience.Tests/DocumentTests.cs` (créer si absent)

**Interfaces:**
- Consumes: `IngestionProgress` (Task 1).
- Produces: `Document.IngestionProgress` (get; private set;) — nullable.

- [ ] **Step 1 : Écrire le test (défaut : nouvelle propriété nulle à la création)**

Créer `Step3/src/Back/AIExperience.Tests/DocumentTests.cs` :

```csharp
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace AIExperience.Tests;

public class DocumentTests
{
    [Fact]
    public void Create_LaisseLaProgressionNulle()
    {
        // Un document fraîchement créé n'a pas encore d'avancement d'ingestion.
        var doc = Document.Create("fichier.pdf", "application/pdf", 1234, "user-1", DocumentMetadata.Empty);

        doc.IngestionProgress.Should().BeNull();
        doc.Status.Should().Be(IngestionStatus.Pending);
    }
}
```

- [ ] **Step 2 : Lancer le test — échec attendu (propriété inexistante)**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter FullyQualifiedName~DocumentTests`
Expected: échec de compilation « 'Document' does not contain a definition for 'IngestionProgress' ». (Si `dotnet test` est bloqué : `dotnet build` échoue au même endroit — c'est le signal d'échec attendu.)

- [ ] **Step 3 : Ajouter la propriété**

Dans `Document.cs`, après la propriété `Chunks` (ligne ~64), ajouter :

```csharp
    /// <summary>
    /// Instantané de l'avancement fin du pipeline d'ingestion (étape courante, pourcentage,
    /// compteurs). Alimenté hors entité par le reporter de progression via une écriture ciblée ;
    /// exposé ici en lecture seule pour l'API de détail. <c>null</c> tant qu'aucun avancement n'a
    /// été rapporté, ou après remise à zéro en fin de traitement réussi.
    /// </summary>
    public Models.IngestionProgress? IngestionProgress { get; private set; }
```

(Ajouter `using AIExperience.Rag.Domain.Models;` en tête n'est pas nécessaire si l'on qualifie `Models.IngestionProgress` ; garder la qualification explicite pour éviter tout conflit de nom.)

- [ ] **Step 4 : Lancer le test — succès attendu**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter FullyQualifiedName~DocumentTests`
Expected: PASS. (Repli si bloqué : `dotnet build` vert.)

- [ ] **Step 5 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs" "Step3/src/Back/AIExperience.Tests/DocumentTests.cs"
git commit -m "feat(domain): propriété IngestionProgress en lecture sur Document"
```

---

### Task 3 : Interfaces `IIngestionProgressReporter` + `IIngestionNotifier.NotifyProgressAsync`

**Files:**
- Create: `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionProgressReporter.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionNotifier.cs`

**Interfaces:**
- Consumes: `IngestionStage`, `IngestionProgress`, `IngestionProgressCounters` (Task 1).
- Produces:
  - `IIngestionProgressReporter.EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default)`
  - `IIngestionProgressReporter.ReportAsync(Guid documentId, IngestionStage stage, int? percent, IngestionProgressCounters counters, CancellationToken ct = default)`
  - `IIngestionProgressReporter.ClearAsync(Guid documentId, CancellationToken ct = default)`
  - `IIngestionNotifier.NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default)`

- [ ] **Step 1 : Créer l'interface de report**

```csharp
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Signale l'avancement fin du pipeline d'ingestion. Deux responsabilités combinées derrière une
/// seule abstraction pour que le pipeline n'ait qu'un interlocuteur : persister l'avancement (pour
/// qu'un rechargement de page le retrouve) et le pousser en temps réel au front. Best-effort comme
/// <see cref="IIngestionNotifier"/> : un incident de report ne doit jamais faire échouer l'ingestion.
/// </summary>
public interface IIngestionProgressReporter
{
    /// <summary>
    /// Signale l'entrée dans une nouvelle étape. Toujours persisté immédiatement (une transition
    /// d'étape est un jalon rare et important) et notifié.
    /// </summary>
    Task EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default);

    /// <summary>
    /// Signale l'avancement fin de l'étape courante. Toujours notifié en temps réel ; persisté
    /// seulement selon la politique de throttling de l'implémentation (pour ne pas marteler la base).
    /// </summary>
    /// <param name="percent">Pourcentage de l'étape (0-100), ou <c>null</c> si indéterminé.</param>
    /// <param name="counters">Compteurs de détail de l'étape.</param>
    Task ReportAsync(Guid documentId, IngestionStage stage, int? percent,
        IngestionProgressCounters counters, CancellationToken ct = default);

    /// <summary>
    /// Efface l'avancement persisté (remet la colonne à <c>null</c>). Appelé en fin de traitement
    /// réussi : les statistiques finales du document suffisent alors, l'avancement n'a plus d'objet.
    /// </summary>
    Task ClearAsync(Guid documentId, CancellationToken ct = default);
}
```

- [ ] **Step 2 : Étendre `IIngestionNotifier`**

Dans `IIngestionNotifier.cs`, ajouter le `using` et la méthode :

```csharp
using AIExperience.Rag.Domain.Models;
```

Puis, après `NotifyStatusChangedAsync` :

```csharp
    /// <summary>Signale l'avancement fin (étape/pourcentage/compteurs) d'une ingestion en cours.</summary>
    /// <param name="documentId">Identifiant du document concerné.</param>
    /// <param name="progress">Instantané d'avancement à pousser au front.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default);
```

- [ ] **Step 3 : Compiler — échec attendu**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: ÉCHEC — `SignalRIngestionNotifier` et `NullIngestionNotifier` n'implémentent pas encore `NotifyProgressAsync` (corrigé aux tasks 5 et 6). C'est le signal attendu ; ne pas commiter tant que le build n'est pas vert (le commit se fait en fin de Task 6, une fois les implémentations en place).

---

### Task 4 : Mapping EF Core JSONB + schéma SQL

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/DocumentConfiguration.cs`
- Modify: `Step3/scripts/init.sql`
- Create: `Step3/scripts/migrate-ingestion-progress.sql`

**Interfaces:**
- Consumes: `Document.IngestionProgress` (Task 2), `IngestionProgress` (Task 1).

- [ ] **Step 1 : Ajouter le mapping JSONB (converter, comme `Metadata.Tags`)**

Dans `DocumentConfiguration.Configure`, après la ligne `builder.Property(d => d.UpdatedAt)...` (ligne ~31) et avant `builder.OwnsOne(...)`, ajouter :

```csharp
        // Avancement fin d'ingestion sérialisé en JSONB (même approche que metadata_tags).
        // Écrit hors tracking par le reporter (UPDATE ciblé) ; lu ici pour l'API de détail.
        builder.Property(d => d.IngestionProgress)
            .HasColumnName("ingestion_progress")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<AIExperience.Rag.Domain.Models.IngestionProgress>(v, JsonSerializerOptions.Default))
            .HasColumnType("jsonb");
```

(`System.Text.Json` est déjà importé dans ce fichier.)

- [ ] **Step 2 : Ajouter la colonne au schéma d'init**

Dans `Step3/scripts/init.sql`, dans la définition de la table `documents`, ajouter une colonne (avant la parenthèse fermante de la table) :

```sql
    ingestion_progress jsonb NULL,
```

(Placer la virgule correctement selon la dernière colonne existante.)

- [ ] **Step 3 : Créer le script de migration pour bases existantes**

Créer `Step3/scripts/migrate-ingestion-progress.sql` :

```sql
-- Ajoute la colonne d'avancement fin d'ingestion aux bases Step3 déjà créées.
-- Idempotent : ne fait rien si la colonne existe déjà.
ALTER TABLE documents
    ADD COLUMN IF NOT EXISTS ingestion_progress jsonb NULL;
```

- [ ] **Step 4 : Appliquer la migration à la base de dev**

Run (PostgreSQL Step3 démarré via `docker-compose up -d` dans `Step3/`, port 5433) :
```bash
docker exec -i $(docker ps -qf "ancestor=pgvector/pgvector:pg17") psql -U postgres -d ragdocumentchat -f - < "Step3/scripts/migrate-ingestion-progress.sql"
```
Expected: `ALTER TABLE`. (Adapter le nom/l'image du conteneur si nécessaire ; l'important est d'exécuter le SQL sur la base `ragdocumentchat` du port 5433.)

- [ ] **Step 5 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: le mapping compile (le build global peut rester rouge à cause de la Task 3 non finie — vérifier au moins l'absence d'erreur dans `DocumentConfiguration.cs`).

- [ ] **Step 6 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/Persistence/Configuration/DocumentConfiguration.cs" "Step3/scripts/init.sql" "Step3/scripts/migrate-ingestion-progress.sql"
git commit -m "feat(db): colonne jsonb ingestion_progress + mapping EF Core"
```

---

### Task 5 : Options + implémentation `IngestionProgressReporter` (persistance throttlée + notification)

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Application/Jobs/IngestionOptions.cs`
- Create: `Step3/src/Back/AIExperience.Rag.Infrastructure/Notifications/IngestionProgressReporter.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs`
- Test: `Step3/src/Back/AIExperience.Tests/IngestionProgressReporterTests.cs`

**Interfaces:**
- Consumes: `IIngestionProgressReporter`, `IIngestionNotifier` (Task 3) ; `AppDbContext` ; `IngestionOptions`.
- Produces: `IngestionProgressReporter` (Singleton) enregistré ; `IngestionOptions.EmbeddingBatchSize` (int, défaut 16), `IngestionOptions.ProgressPersistThrottleSeconds` (double, défaut 1.5).

- [ ] **Step 1 : Ajouter les deux options**

Dans `IngestionOptions.cs`, après `MaxRetryAttempts` :

```csharp
    /// <summary>
    /// Taille des sous-lots d'embedding. Découper l'appel d'embedding en sous-lots permet de
    /// rapporter un avancement fin (« lot 3/8 ») plutôt qu'un unique appel opaque. Défaut prudent.
    /// </summary>
    public int EmbeddingBatchSize { get; set; } = 16;

    /// <summary>
    /// Intervalle minimal, en secondes, entre deux persistances d'avancement fin en base. Les ticks
    /// intermédiaires sont poussés en temps réel (SignalR) mais pas écrits, pour ne pas marteler la base.
    /// </summary>
    public double ProgressPersistThrottleSeconds { get; set; } = 1.5;
```

- [ ] **Step 2 : Écrire le test de throttling**

`Step3/src/Back/AIExperience.Tests/IngestionProgressReporterTests.cs` :

```csharp
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIExperience.Tests;

public class IngestionProgressReporterTests
{
    // Faux notifier qui compte les notifications reçues.
    private sealed class CountingNotifier : IIngestionNotifier
    {
        public int ProgressCount { get; private set; }
        public Task NotifyStatusChangedAsync(Guid id, string status, string? err, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyProgressAsync(Guid id, IngestionProgress p, CancellationToken ct = default)
        {
            ProgressCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ReportAsync_NotifieAChaqueAppel()
    {
        // Le canal temps réel n'est pas throttlé : chaque tick doit être poussé.
        var notifier = new CountingNotifier();
        var reporter = CreateReporter(notifier, out _);
        var id = Guid.NewGuid();

        await reporter.ReportAsync(id, IngestionStage.Embedding, 10, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 20, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 30, IngestionProgressCounters.Empty);

        notifier.ProgressCount.Should().Be(3);
    }
}
```

> **Note d'implémentation pour la persistance :** la persistance réelle nécessite un `AppDbContext`
> (donc PostgreSQL). Pour un test unitaire de throttling sans base, le reporter délègue l'écriture
> à une fonction interne surchargeable via un point d'extension `protected virtual Task
> PersistAsync(...)`. Le helper `CreateReporter` ci-dessous instancie une sous-classe de test qui
> **compte** les persistances au lieu d'écrire en base — voir Step 4.

- [ ] **Step 3 : Écrire l'implémentation**

`Step3/src/Back/AIExperience.Rag.Infrastructure/Notifications/IngestionProgressReporter.cs` :

```csharp
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AIExperience.Rag.Infrastructure.Notifications;

/// <summary>
/// Reporter d'avancement : pousse chaque tick au front en temps réel et persiste l'avancement en
/// base de façon throttlée (une transition d'étape est toujours écrite ; les ticks intermédiaires
/// ne le sont qu'au-delà d'un intervalle minimal). L'écriture est <b>ciblée</b> (UPDATE de la seule
/// colonne <c>ingestion_progress</c>, hors tracking EF) pour ne jamais écraser un changement de
/// statut fait en parallèle par le worker. Singleton : conserve en mémoire l'horodatage de la
/// dernière persistance par document (le worker traite les documents un par un, pas de contention).
/// </summary>
public class IngestionProgressReporter(
    IServiceScopeFactory scopeFactory,
    IIngestionNotifier notifier,
    IOptions<IngestionOptions> options,
    ILogger<IngestionProgressReporter> logger) : IIngestionProgressReporter
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastPersist = new();
    private TimeSpan Throttle => TimeSpan.FromSeconds(options.Value.ProgressPersistThrottleSeconds);

    /// <inheritdoc/>
    public async Task EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default)
    {
        var progress = IngestionProgress.Create(stage);
        // Une transition d'étape est un jalon important : toujours persistée.
        await PersistAndTrackAsync(documentId, progress, ct);
        await SafeNotifyAsync(documentId, progress, ct);
    }

    /// <inheritdoc/>
    public async Task ReportAsync(Guid documentId, IngestionStage stage, int? percent,
        IngestionProgressCounters counters, CancellationToken ct = default)
    {
        var progress = IngestionProgress.Create(stage, percent, counters);

        // Persistance throttlée : on n'écrit que si l'intervalle minimal est écoulé depuis la dernière.
        var last = _lastPersist.TryGetValue(documentId, out var t) ? t : DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - last >= Throttle)
            await PersistAndTrackAsync(documentId, progress, ct);

        // Le temps réel, lui, n'est jamais throttlé : chaque tick est poussé.
        await SafeNotifyAsync(documentId, progress, ct);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(Guid documentId, CancellationToken ct = default)
    {
        _lastPersist.TryRemove(documentId, out _);
        await PersistAsync(documentId, null, ct);
    }

    // Persiste puis mémorise l'instant de persistance (pour le throttling).
    private async Task PersistAndTrackAsync(Guid documentId, IngestionProgress progress, CancellationToken ct)
    {
        await PersistAsync(documentId, progress, ct);
        _lastPersist[documentId] = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Écrit la seule colonne <c>ingestion_progress</c> via un UPDATE ciblé, dans un scope dédié.
    /// Surchargeable pour les tests (évite le besoin d'une base réelle).
    /// </summary>
    protected virtual async Task PersistAsync(Guid documentId, IngestionProgress? progress, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Documents
                .Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IngestionProgress, progress), ct);
        }
        catch (Exception ex)
        {
            // Best-effort : un échec de persistance de l'avancement ne doit jamais interrompre
            // l'ingestion elle-même (le statut en base reste la source de vérité).
            logger.LogWarning(ex, "Échec de persistance de l'avancement du document {DocumentId}.", documentId);
        }
    }

    private async Task SafeNotifyAsync(Guid documentId, IngestionProgress progress, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyProgressAsync(documentId, progress, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Échec de notification d'avancement du document {DocumentId}.", documentId);
        }
    }
}
```

- [ ] **Step 4 : Compléter le test avec la sous-classe qui compte les persistances**

Ajouter dans `IngestionProgressReporterTests.cs` le helper et un test de throttling :

```csharp
    // Sous-classe de test : remplace l'écriture en base par un simple compteur.
    private sealed class TestReporter(
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory sf,
        IIngestionNotifier n,
        IOptions<IngestionOptions> o,
        Microsoft.Extensions.Logging.ILogger<AIExperience.Rag.Infrastructure.Notifications.IngestionProgressReporter> l)
        : AIExperience.Rag.Infrastructure.Notifications.IngestionProgressReporter(sf, n, o, l)
    {
        public int PersistCount { get; private set; }
        protected override Task PersistAsync(Guid documentId, IngestionProgress? progress, CancellationToken ct)
        {
            PersistCount++;
            return Task.CompletedTask;
        }
    }

    private static TestReporter CreateReporter(IIngestionNotifier notifier, out IOptions<IngestionOptions> options)
    {
        options = Options.Create(new IngestionOptions
        {
            WorkDirectory = "unused",
            ProgressPersistThrottleSeconds = 60 // grand : garantit le throttling pendant le test
        });
        var scopeFactory = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider().GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AIExperience.Rag.Infrastructure.Notifications.IngestionProgressReporter>.Instance;
        return new TestReporter(scopeFactory, notifier, options, logger);
    }

    [Fact]
    public async Task ReportAsync_ThrottleLesPersistances()
    {
        // Avec un throttle de 60s, seule la première persistance passe ; les suivantes sont ignorées.
        var reporter = CreateReporter(new CountingNotifier(), out _);
        var id = Guid.NewGuid();

        await reporter.ReportAsync(id, IngestionStage.Embedding, 10, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 20, IngestionProgressCounters.Empty);
        await reporter.ReportAsync(id, IngestionStage.Embedding, 30, IngestionProgressCounters.Empty);

        reporter.PersistCount.Should().Be(1);
    }

    [Fact]
    public async Task EnterStageAsync_PersisteToujours()
    {
        // Chaque transition d'étape doit être persistée, throttle ou pas.
        var reporter = CreateReporter(new CountingNotifier(), out _);
        var id = Guid.NewGuid();

        await reporter.EnterStageAsync(id, IngestionStage.Chunking);
        await reporter.EnterStageAsync(id, IngestionStage.Embedding);

        reporter.PersistCount.Should().Be(2);
    }
```

> Ajuster la signature de `CreateReporter` déjà déclarée au Step 2 (elle renvoie désormais `TestReporter`).

- [ ] **Step 5 : Enregistrer le reporter en DI**

Dans `Infrastructure/DependencyInjection.cs`, méthode `AddIngestionWorkerInfrastructure`, après la ligne `services.TryAddSingleton<IIngestionNotifier, NullIngestionNotifier>();` :

```csharp
        // Singleton : conserve en mémoire l'état de throttling par document. Résout AppDbContext
        // via un scope à chaque persistance (le worker est mono-instance, pas de contention).
        services.AddSingleton<IIngestionProgressReporter, IngestionProgressReporter>();
```

Ajouter le `using` nécessaire en tête du fichier si absent : `using AIExperience.Rag.Domain.Interfaces.Services;` (déjà présent) — la classe `IngestionProgressReporter` est dans `AIExperience.Rag.Infrastructure.Notifications`, déjà couvert par `using AIExperience.Rag.Infrastructure.Notifications;` (présent).

- [ ] **Step 6 : Lancer les tests**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter FullyQualifiedName~IngestionProgressReporterTests`
Expected: 3 tests PASS. (Repli si bloqué : `dotnet build "Step3/src/Back/AIExperience.slnx"` vert — hors erreurs restantes de Task 3 corrigées en Task 6.)

- [ ] **Step 7 : Commit** (après Task 6 pour un build vert global — voir Task 6 Step 4)

---

### Task 6 : Notifieurs — `SignalRIngestionNotifier` + `NullIngestionNotifier`

**Files:**
- Modify: `Step3/src/Back/AIExperience.Web.Api/Notifications/SignalRIngestionNotifier.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/Notifications/NullIngestionNotifier.cs`

**Interfaces:**
- Consumes: `IIngestionNotifier.NotifyProgressAsync` (Task 3), `IngestionProgress` (Task 1).
- Produces: event SignalR `documentProgressChanged` avec `{ documentId, stage, percent, counters }`.

- [ ] **Step 1 : Implémenter la notification SignalR**

Remplacer le contenu de `SignalRIngestionNotifier.cs` par :

```csharp
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;
using AIExperience.Web.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AIExperience.Web.Api.Notifications;

/// <summary>
/// Diffuse les changements de statut ET l'avancement fin d'ingestion à tous les clients connectés
/// au hub. Volontairement simple (pas de segmentation par utilisateur) tant qu'il n'y a pas
/// d'authentification — à revoir quand elle arrivera.
/// </summary>
public sealed class SignalRIngestionNotifier(IHubContext<IngestionHub> hubContext) : IIngestionNotifier
{
    public async Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        => await hubContext.Clients.All.SendAsync("documentStatusChanged",
            new { documentId, status, errorMessage }, ct);

    public async Task NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default)
        => await hubContext.Clients.All.SendAsync("documentProgressChanged",
            new
            {
                documentId,
                stage = progress.Stage.ToString(),
                percent = progress.Percent,
                counters = progress.Counters
            }, ct);
}
```

- [ ] **Step 2 : Implémenter le no-op console**

Remplacer le corps de `NullIngestionNotifier.cs` par :

```csharp
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models;

namespace AIExperience.Rag.Infrastructure.Notifications;

/// <summary>
/// Notifieur par défaut, sans effet, utilisé par les composition roots sans canal temps réel
/// (ex. l'application console). Le Web.Api enregistre sa propre implémentation SignalR, prioritaire.
/// </summary>
public sealed class NullIngestionNotifier : IIngestionNotifier
{
    public Task NotifyStatusChangedAsync(Guid documentId, string status, string? errorMessage, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyProgressAsync(Guid documentId, IngestionProgress progress, CancellationToken ct = default)
        => Task.CompletedTask;
}
```

- [ ] **Step 3 : Compiler — build vert global attendu**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: BUILD succeeded (toutes les implémentations de `IIngestionNotifier` sont désormais complètes).

- [ ] **Step 4 : Commit (regroupe Tasks 3, 5 et 6 pour un build cohérent)**

```bash
git add "Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionProgressReporter.cs" \
        "Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IIngestionNotifier.cs" \
        "Step3/src/Back/AIExperience.Rag.Application/Jobs/IngestionOptions.cs" \
        "Step3/src/Back/AIExperience.Rag.Infrastructure/Notifications/IngestionProgressReporter.cs" \
        "Step3/src/Back/AIExperience.Rag.Infrastructure/Notifications/NullIngestionNotifier.cs" \
        "Step3/src/Back/AIExperience.Web.Api/Notifications/SignalRIngestionNotifier.cs" \
        "Step3/src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs" \
        "Step3/src/Back/AIExperience.Tests/IngestionProgressReporterTests.cs"
git commit -m "feat(back): reporter d'avancement d'ingestion (persistance throttlée + SignalR)"
```

---

### Task 7 : Instrumenter `IngestionService` — étapes + embedding en sous-lots

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs`
- Test: `Step3/src/Back/AIExperience.Tests/IngestionServiceEmbeddingProgressTests.cs`

**Interfaces:**
- Consumes: `IIngestionProgressReporter` (Task 3), `IngestionStage` (Task 1), `IngestionOptions.EmbeddingBatchSize` (Task 5), `IEmbeddingService.EmbedBatchAsync`.
- Produces: séquence d'étapes émise par `IngestAsync` (`ExtractingText → Chunking → Embedding → Storing`), par `IngestFromSegmentsAsync` (`Chunking → Embedding → Storing`), par `IngestVideoOrAudioAsync` (`ExtractingAudio → Transcribing → …`) ; méthode privée `EmbedInBatchesAsync(...)` factorisée.

- [ ] **Step 1 : Injecter le reporter et les options**

Modifier le constructeur primaire de `IngestionService` pour ajouter deux dépendances :

```csharp
public sealed class IngestionService(
    ICompositeTextExtractor compositeTextExtractor,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService,
    ITemporalChunker temporalChunker,
    ITextChunker textChunker,
    ILanguageDetectionService languageDetectionService,
    IVideoProcessorService videoProcessorService,
    ITranscriptionService transcriptionService,
    IIngestionProgressReporter progressReporter,
    Microsoft.Extensions.Options.IOptions<AIExperience.Rag.Application.Jobs.IngestionOptions> ingestionOptions) : IIngestionService
```

Ajouter le `using AIExperience.Rag.Domain.Enums;` (déjà présent) et `using AIExperience.Rag.Domain.Models;`.

- [ ] **Step 2 : Écrire le test d'avancement embedding en sous-lots**

`Step3/src/Back/AIExperience.Tests/IngestionServiceEmbeddingProgressTests.cs`. Ce test vérifie le
comportement de découpage via une méthode utilitaire pure `IngestionBatching.Split(total, batchSize)`
(extraite pour être testable sans monter tout le service) :

```csharp
using AIExperience.Rag.Application.Services;
using FluentAssertions;
using Xunit;

namespace AIExperience.Tests;

public class IngestionServiceEmbeddingProgressTests
{
    [Theory]
    [InlineData(0, 16, 0)]
    [InlineData(1, 16, 1)]
    [InlineData(16, 16, 1)]
    [InlineData(17, 16, 2)]
    [InlineData(120, 16, 8)]
    public void Split_CalculeLeBonNombreDeLots(int total, int batchSize, int expectedBatches)
    {
        // Le nombre de lots conditionne l'affichage « lot i/n » côté UI.
        var batches = IngestionBatching.Split(total, batchSize);
        batches.Count.Should().Be(expectedBatches);
    }

    [Fact]
    public void Split_CouvreTousLesIndicesSansTrou()
    {
        // Les lots doivent couvrir [0, total) exactement une fois, dans l'ordre.
        var batches = IngestionBatching.Split(50, 16);
        batches.SelectMany(b => Enumerable.Range(b.Start, b.Count))
            .Should().BeEquivalentTo(Enumerable.Range(0, 50), o => o.WithStrictOrdering());
    }
}
```

- [ ] **Step 3 : Créer l'utilitaire de découpage**

`Step3/src/Back/AIExperience.Rag.Application/Services/IngestionBatching.cs` :

```csharp
namespace AIExperience.Rag.Application.Services;

/// <summary>Un sous-lot d'éléments contigus : index de départ (inclus) et nombre d'éléments.</summary>
public readonly record struct IngestionBatch(int Start, int Count);

/// <summary>
/// Découpe utilitaire, pure et testable, pour l'embedding en sous-lots. Isolée du service pour
/// pouvoir vérifier le calcul « lot i/n » sans monter tout le pipeline d'ingestion.
/// </summary>
public static class IngestionBatching
{
    /// <summary>Découpe <paramref name="total"/> éléments en sous-lots d'au plus <paramref name="batchSize"/>.</summary>
    public static IReadOnlyList<IngestionBatch> Split(int total, int batchSize)
    {
        if (batchSize < 1) batchSize = 1;
        var batches = new List<IngestionBatch>();
        for (var start = 0; start < total; start += batchSize)
            batches.Add(new IngestionBatch(start, Math.Min(batchSize, total - start)));
        return batches;
    }
}
```

- [ ] **Step 4 : Lancer le test — succès attendu**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter FullyQualifiedName~IngestionServiceEmbeddingProgressTests`
Expected: tests PASS.

- [ ] **Step 5 : Factoriser l'embedding en sous-lots avec report d'avancement**

Ajouter dans `IngestionService` une méthode privée réutilisée par les trois pipelines :

```csharp
    /// <summary>
    /// Vectorise les chunks en sous-lots (taille configurable) en rapportant l'avancement « lot i/n »
    /// après chaque sous-lot. Retourne les vecteurs dans l'ordre des textes d'entrée.
    /// </summary>
    private async Task<IReadOnlyList<float[]>> EmbedInBatchesAsync(
        Guid documentId, IReadOnlyList<string> texts, CancellationToken ct)
    {
        await progressReporter.EnterStageAsync(documentId, IngestionStage.Embedding, ct);

        var batches = IngestionBatching.Split(texts.Count, ingestionOptions.Value.EmbeddingBatchSize);
        var result = new float[texts.Count][];

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var slice = new List<string>(batch.Count);
            for (var j = 0; j < batch.Count; j++)
                slice.Add(texts[batch.Start + j]);

            var vectors = await embeddingService.EmbedBatchAsync(slice, EmbeddingTaskType.Document, ct);
            for (var j = 0; j < batch.Count; j++)
                result[batch.Start + j] = vectors[j];

            var chunksDone = batch.Start + batch.Count;
            var percent = texts.Count == 0 ? 100 : (int)(100.0 * chunksDone / texts.Count);
            await progressReporter.ReportAsync(documentId, IngestionStage.Embedding, percent,
                new IngestionProgressCounters(
                    BatchIndex: i + 1, BatchCount: batches.Count,
                    ChunksDone: chunksDone, ChunksTotal: texts.Count), ct);
        }

        return result;
    }
```

- [ ] **Step 6 : Câbler les étapes dans `IngestAsync`**

Dans `IngestAsync`, remplacer l'appel unique à `EmbedBatchAsync` et encadrer d'étapes. Concrètement :

1. Avant `ExtractPagesAsync` : `await progressReporter.EnterStageAsync(documentId, IngestionStage.ExtractingText, ct);`
2. Avant `textChunker.ChunkPages(pages)` : `await progressReporter.EnterStageAsync(documentId, IngestionStage.Chunking, ct);`
3. Remplacer :
   ```csharp
   var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);
   ```
   par :
   ```csharp
   var embeddings = await EmbedInBatchesAsync(documentId, textChunks.Select(c => c.Content).ToList(), ct);
   ```
4. Avant `vectorStoreService.UpsertBatchAsync(items, ct)` : `await progressReporter.EnterStageAsync(documentId, IngestionStage.Storing, ct);`

(La vérification de cohérence `embeddings.Count != textChunks.Count` reste inchangée et valable.)

- [ ] **Step 7 : Câbler les étapes dans `IngestTextAsync` et `IngestFromSegmentsAsync`**

Dans chacune :
1. Avant le chunking : `await progressReporter.EnterStageAsync(documentId, IngestionStage.Chunking, ct);`
2. Remplacer l'appel `EmbedBatchAsync(...)` par `await EmbedInBatchesAsync(documentId, textChunks.Select(c => c.Content).ToList(), ct);`
3. Avant `UpsertBatchAsync` : `await progressReporter.EnterStageAsync(documentId, IngestionStage.Storing, ct);`

- [ ] **Step 8 : Câbler les étapes vidéo dans `IngestVideoOrAudioAsync`**

Dans `IngestVideoOrAudioAsync` :
1. Juste avant l'extraction audio (`videoProcessorService.ExtractAudioAsync`), pour les vidéos :
   `if (isVideo) await progressReporter.EnterStageAsync(documentId, IngestionStage.ExtractingAudio, ct);`
2. Avant `transcriptionService.TranscribeAsync(...)` :
   `await progressReporter.EnterStageAsync(documentId, IngestionStage.Transcribing, ct);`
   et passer le callback de progression segment (défini en Task 8) :
   ```csharp
   var totalDuration = await videoProcessorService.TryGetMediaDurationAsync(audioPath, ct) ?? TimeSpan.Zero;
   var result = await transcriptionService.TranscribeAsync(audioPath, language,
       onSegment: seg => ReportTranscriptionProgress(documentId, seg, totalDuration), ct);
   ```
   > Le helper `ReportTranscriptionProgress` et le paramètre `onSegment` sont introduits en Task 8.
   > Réaliser les Steps 8 de cette task **après** la Task 8 (dépendance de signature), ou laisser
   > l'appel de transcription inchangé ici et n'ajouter que l'`EnterStageAsync(Transcribing)` pour
   > l'instant, le `%` fin étant branché en Task 8.

- [ ] **Step 9 : Compiler + tests**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"` puis `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter FullyQualifiedName~IngestionServiceEmbeddingProgressTests`
Expected: BUILD succeeded ; tests PASS.

- [ ] **Step 10 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs" \
        "Step3/src/Back/AIExperience.Rag.Application/Services/IngestionBatching.cs" \
        "Step3/src/Back/AIExperience.Tests/IngestionServiceEmbeddingProgressTests.cs"
git commit -m "feat(ingestion): étapes + embedding en sous-lots avec report d'avancement"
```

---

### Task 8 : Progression de transcription Whisper (callback par segment)

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/Video/ITranscriptionService.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/Video/IVideoProcessorService.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Transcription/WhisperTranscriptionService.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Video/FFmpegVideoProcessorService.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Application/Video/Command/ProcessVideoTranscriptionJobHandler.cs`

**Interfaces:**
- Consumes: `IIngestionProgressReporter`, `IngestionStage.Transcribing`, `TranscriptionSegment`.
- Produces: `ITranscriptionService.TranscribeAsync(string audioPath, string language, Action<TranscriptionSegment>? onSegment, CancellationToken ct)` (surcharge additive) ; `IVideoProcessorService.TryGetMediaDurationAsync(string path, CancellationToken ct)` → `Task<TimeSpan?>` ; helper `IngestionService.ReportTranscriptionProgress(Guid, TranscriptionSegment, TimeSpan)`.

> **Rappel Clean Architecture :** la durée totale (pour le `%` de transcription) est sondée via
> l'interface Domain `IVideoProcessorService` — **jamais** en appelant `FFMpegCore` depuis la couche
> Application (FFMpegCore est une dépendance Infrastructure). Les deux appelants (`IngestionService`,
> `ProcessVideoTranscriptionJobHandler`) injectent déjà `IVideoProcessorService`.

- [ ] **Step 0 : Ajouter le sondage de durée à `IVideoProcessorService`**

Dans `IVideoProcessorService.cs`, après `bool IsSupported(string filePath);` :

```csharp
        /// <summary>
        /// Sonde la durée totale d'un média (vidéo ou audio). Utilisée pour convertir l'avancement
        /// de transcription en pourcentage. Best-effort : renvoie <c>null</c> si la durée ne peut
        /// pas être déterminée (fichier illisible, sondage en échec) — l'avancement devient alors
        /// indéterminé côté UI plutôt que de faire échouer le traitement.
        /// </summary>
        Task<TimeSpan?> TryGetMediaDurationAsync(string path, CancellationToken cancellationToken = default);
```

Dans `FFmpegVideoProcessorService.cs`, implémenter avec FFProbe (FFMpegCore, déjà référencé côté Infrastructure) :

```csharp
    /// <inheritdoc/>
    public async Task<TimeSpan?> TryGetMediaDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await FFMpegCore.FFProbe.AnalyseAsync(path, cancellationToken: cancellationToken);
            return info.Duration > TimeSpan.Zero ? info.Duration : null;
        }
        catch
        {
            // Best-effort : la durée n'est qu'un confort d'affichage (pourcentage), pas une donnée critique.
            return null;
        }
    }
```

(Ajouter `using FFMpegCore;` en tête de `FFmpegVideoProcessorService.cs` si absent.)

- [ ] **Step 1 : Ajouter le paramètre de callback à l'interface**

Remplacer la signature dans `ITranscriptionService.cs` par (le paramètre est optionnel → rétro-compatible) :

```csharp
        Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            string language = "fr",
            Action<TranscriptionSegment>? onSegment = null,
            CancellationToken cancellationToken = default);
```

Mettre à jour le commentaire XML pour documenter `onSegment` : « rappelé à chaque segment transcrit,
pour rapporter l'avancement en temps réel ; <c>null</c> pour ne rien rapporter. »

- [ ] **Step 2 : Invoquer le callback dans Whisper**

Dans `WhisperTranscriptionService.TranscribeAsync`, mettre à jour la signature (ajouter
`Action<TranscriptionSegment>? onSegment = null` avant `CancellationToken`), puis, dans la boucle
`await foreach`, après `segments.Add(ts);` :

```csharp
            // Rapporte le segment tout juste transcrit à l'appelant (avancement temps réel).
            onSegment?.Invoke(ts);
```

- [ ] **Step 3 : Ajouter le helper de report dans `IngestionService`**

```csharp
    /// <summary>
    /// Convertit un segment Whisper en avancement de transcription : pourcentage basé sur la fin du
    /// segment rapportée à la durée totale (si connue), sinon <c>null</c> (indéterminé). Best-effort,
    /// synchrone côté appelant Whisper : on ne bloque pas la boucle de transcription.
    /// </summary>
    private void ReportTranscriptionProgress(Guid documentId, Domain.Models.Video.TranscriptionSegment segment, TimeSpan totalDuration)
    {
        int? percent = totalDuration > TimeSpan.Zero
            ? Math.Clamp((int)(100.0 * segment.End.TotalSeconds / totalDuration.TotalSeconds), 0, 99)
            : null;

        // Fire-and-forget contrôlé : le report est best-effort et ne doit pas ralentir Whisper.
        _ = progressReporter.ReportAsync(documentId, IngestionStage.Transcribing, percent,
            new IngestionProgressCounters(SegmentsDone: null, SegmentsTotal: null));
    }
```

> **Durée totale** : dans `IngestVideoOrAudioAsync`, sonder la durée en amont via l'interface Domain :
> `var totalDuration = await videoProcessorService.TryGetMediaDurationAsync(audioPath, ct) ?? TimeSpan.Zero;`
> (le `%` devient indéterminé si `TimeSpan.Zero` — repli assumé par la spec).
> Passer cette durée à `TranscribeAsync(..., onSegment: seg => ReportTranscriptionProgress(documentId, seg, totalDuration), ct)`.

- [ ] **Step 4 : Câbler le callback dans le handler vidéo dédié**

Dans `ProcessVideoTranscriptionJobHandler.Handle`, ce handler n'a pas accès à `IngestionService` en
tant que reporter — il faut lui injecter `IIngestionProgressReporter` et émettre les étapes :

1. Ajouter `IIngestionProgressReporter progressReporter` au constructeur primaire.
2. Après `MarkProcessingAsync` : si vidéo, `await progressReporter.EnterStageAsync(document.Id, IngestionStage.ExtractingAudio, cancellationToken);` (avant `ExtractAudioAsync`).
3. Avant `TranscribeAsync` : `await progressReporter.EnterStageAsync(document.Id, IngestionStage.Transcribing, cancellationToken);` puis sonder la durée via l'interface Domain (`var totalDuration = await videoProcessor.TryGetMediaDurationAsync(audioPath, cancellationToken) ?? TimeSpan.Zero;`) et appeler :
   ```csharp
   var result = await transcriptionService.TranscribeAsync(audioPath, document.Metadata.Language,
       onSegment: seg =>
       {
           int? pct = totalDuration > TimeSpan.Zero
               ? Math.Clamp((int)(100.0 * seg.End.TotalSeconds / totalDuration.TotalSeconds), 0, 99)
               : null;
           _ = progressReporter.ReportAsync(document.Id, IngestionStage.Transcribing, pct, new IngestionProgressCounters());
       },
       cancellationToken);
   ```
   (Les étapes Chunking/Embedding/Storing suivantes sont déjà émises par `IngestFromSegmentsAsync`, appelé plus bas — ne pas les dupliquer.)
4. `using AIExperience.Rag.Domain.Enums;`, `using AIExperience.Rag.Domain.Interfaces.Services;`, `using AIExperience.Rag.Domain.Models;` à ajouter en tête.

- [ ] **Step 5 : Finaliser l'appel Transcribing dans `IngestVideoOrAudioAsync`** (compléter Task 7 Step 8)

Remplacer l'appel de transcription par la version avec `onSegment` + durée sondée (voir Step 3 note).

- [ ] **Step 6 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: BUILD succeeded. Vérifier qu'aucun autre appelant de `TranscribeAsync` ne casse (le paramètre est optionnel).

- [ ] **Step 7 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/Video/ITranscriptionService.cs" \
        "Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Transcription/WhisperTranscriptionService.cs" \
        "Step3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs" \
        "Step3/src/Back/AIExperience.Rag.Application/Video/Command/ProcessVideoTranscriptionJobHandler.cs"
git commit -m "feat(transcription): report d'avancement Whisper par segment"
```

---

### Task 9 : Remise à zéro de l'avancement en fin de traitement réussi

Sur succès, l'avancement n'a plus d'objet (les stats finales suffisent) → on appelle `ClearAsync`.
Sur échec, on **conserve** le dernier avancement persisté (« où ça a cassé »).

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Application/Document/Command/IngestDocumentHandler.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Application/Video/Command/ProcessVideoTranscriptionJobHandler.cs`

**Interfaces:**
- Consumes: `IIngestionProgressReporter.ClearAsync` (Task 3).

- [ ] **Step 1 : `IngestDocumentHandler` — injecter le reporter et nettoyer sur succès**

1. Ajouter `IIngestionProgressReporter progressReporter` au constructeur primaire.
2. Juste après `await statusUpdater.MarkCompletedAsync(document, cancellationToken);` (dans le `try`) :
   `await progressReporter.ClearAsync(document.Id, cancellationToken);`
3. Ne rien ajouter dans le `catch` (l'avancement d'échec reste persisté).

- [ ] **Step 2 : `ProcessVideoTranscriptionJobHandler` — idem**

Après `await statusUpdater.MarkCompletedAsync(document, cancellationToken);` :
`await progressReporter.ClearAsync(document.Id, cancellationToken);`
(Le reporter est déjà injecté en Task 8 Step 4.)

- [ ] **Step 3 : Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: BUILD succeeded.

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Rag.Application/Document/Command/IngestDocumentHandler.cs" \
        "Step3/src/Back/AIExperience.Rag.Application/Video/Command/ProcessVideoTranscriptionJobHandler.cs"
git commit -m "feat(ingestion): remise à zéro de l'avancement en fin de traitement réussi"
```

---

### Task 10 : Enrichir `DocumentResponse` avec l'avancement

**Files:**
- Modify: `Step3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs`
- Modify: `Step3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs`

**Interfaces:**
- Consumes: `Document.IngestionProgress` (Task 2), `IngestionProgress`/`IngestionProgressCounters` (Task 1).
- Produces: `DocumentResponse.IngestionProgress` (nullable) exposé par `GET /api/documents` et `GET /api/documents/{id}`.

- [ ] **Step 1 : Étendre le DTO**

Dans `DocumentDtos.cs`, ajouter deux records et enrichir `DocumentResponse` :

```csharp
/// <summary>Compteurs de détail de l'avancement d'ingestion (voir IngestionProgressCounters).</summary>
public record IngestionProgressCountersResponse(
    int? BatchIndex, int? BatchCount, int? ChunksDone, int? ChunksTotal, int? SegmentsDone, int? SegmentsTotal);

/// <summary>Avancement fin d'ingestion exposé au front (étape textuelle + pourcentage + compteurs).</summary>
public record IngestionProgressResponse(
    string Stage, int? Percent, IngestionProgressCountersResponse Counters, DateTimeOffset UpdatedAt);
```

Et remplacer la déclaration de `DocumentResponse` par :

```csharp
public record DocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Status,
    DateTimeOffset CreatedAt,
    string? ErrorMessage = null,
    IngestionProgressResponse? IngestionProgress = null);
```

- [ ] **Step 2 : Mapper dans le contrôleur**

Dans `DocumentsController.cs`, remplacer `ToResponse` par :

```csharp
    private static DocumentResponse ToResponse(AIExperience.Rag.Domain.Entities.Document d) =>
        new(d.Id, d.FileName, d.ContentType, d.FileSizeBytes, d.Status.ToString(), d.CreatedAt, d.ErrorMessage,
            d.IngestionProgress is null
                ? null
                : new IngestionProgressResponse(
                    d.IngestionProgress.Stage.ToString(),
                    d.IngestionProgress.Percent,
                    new IngestionProgressCountersResponse(
                        d.IngestionProgress.Counters.BatchIndex,
                        d.IngestionProgress.Counters.BatchCount,
                        d.IngestionProgress.Counters.ChunksDone,
                        d.IngestionProgress.Counters.ChunksTotal,
                        d.IngestionProgress.Counters.SegmentsDone,
                        d.IngestionProgress.Counters.SegmentsTotal),
                    d.IngestionProgress.UpdatedAt));
```

- [ ] **Step 3 : Compiler + vérification manuelle rapide**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: BUILD succeeded.

Vérification manuelle (base + LM Studio requis) : lancer l'API, uploader un PDF, appeler
`GET http://localhost:50406/api/documents/{id}` pendant le traitement → le champ `ingestionProgress`
doit apparaître avec une `stage` et un `percent` évoluant, puis redevenir `null` une fois `Completed`.

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Back/AIExperience.Web.Api/DTOs/DocumentDtos.cs" "Step3/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs"
git commit -m "feat(api): exposer l'avancement d'ingestion dans DocumentResponse"
```

---

## TÂCHES FRONT

### Task 11 : Types TypeScript de progression

**Files:**
- Modify: `Step3/src/Front/src/types/index.ts`

**Interfaces:**
- Produces: `IngestionStage` (union), `IngestionProgressCounters`, `IngestionProgress`, `DocumentProgressChangedEvent`, `DocumentResponse.ingestionProgress?`.

- [ ] **Step 1 : Ajouter les types**

Dans `types/index.ts`, après la définition de `DocumentStatusChangedEvent` :

```typescript
/** Étapes fines du pipeline d'ingestion (miroir de l'enum back-end IngestionStage). */
export type IngestionStage =
  | 'Queued' | 'ExtractingAudio' | 'Transcribing' | 'ExtractingText'
  | 'Chunking' | 'Embedding' | 'Storing' | 'Completed' | 'Failed';

/** Compteurs de détail d'une étape (tous optionnels selon l'étape). */
export interface IngestionProgressCounters {
  batchIndex?: number | null;
  batchCount?: number | null;
  chunksDone?: number | null;
  chunksTotal?: number | null;
  segmentsDone?: number | null;
  segmentsTotal?: number | null;
}

/** Instantané d'avancement fin d'une ingestion. */
export interface IngestionProgress {
  stage: IngestionStage;
  percent: number | null;
  counters: IngestionProgressCounters;
  updatedAt: string;
}

/** Événement SignalR d'avancement fin, distinct de DocumentStatusChangedEvent. */
export interface DocumentProgressChangedEvent {
  documentId: string;
  stage: IngestionStage;
  percent: number | null;
  counters: IngestionProgressCounters;
}
```

Puis, dans `DocumentResponse`, ajouter le champ optionnel :

```typescript
  /** Avancement fin d'ingestion (présent surtout pendant/juste après le traitement). */
  ingestionProgress?: IngestionProgress | null;
```

- [ ] **Step 2 : Compiler**

Run (dans `Step3/src/Front`): `npm run build`
Expected: compilation TypeScript sans erreur.

- [ ] **Step 3 : Commit**

```bash
git add "Step3/src/Front/src/types/index.ts"
git commit -m "feat(front): types de progression d'ingestion"
```

---

### Task 12 : Abonnement `subscribeProgress` dans le contexte de notifications

**Files:**
- Modify: `Step3/src/Front/src/context/IngestionNotificationsContext.tsx`

**Interfaces:**
- Consumes: `DocumentProgressChangedEvent` (Task 11), `createIngestionHubConnection`.
- Produces: `useIngestionNotifications().subscribeProgress(documentId: string, listener: (e: DocumentProgressChangedEvent) => void): () => void`.

- [ ] **Step 1 : Ajouter le canal de progression**

Dans `IngestionNotificationsContext.tsx` :

1. Importer le type : `import type { DocumentProgressChangedEvent, DocumentStatusChangedEvent } from '../types';`
2. Ajouter au type du contexte :
   ```typescript
     /**
      * S'abonne à l'avancement fin d'UN document (filtré par id). Réservé à la page de détail :
      * les ticks haute fréquence ne concernent pas la liste, qui ne s'abonne qu'au statut grossier.
      */
     subscribeProgress: (documentId: string, listener: (event: DocumentProgressChangedEvent) => void) => () => void;
   ```
3. Ajouter une `ref` de listeners de progression, à côté de `listenersRef` :
   ```typescript
     // Listeners d'avancement fin, indexés par documentId (un seul document suivi à la fois en pratique).
     const progressListenersRef = useRef(new Map<string, Set<(e: DocumentProgressChangedEvent) => void>>());
   ```
4. Dans le `useEffect` de connexion, après `connection.on('documentStatusChanged', handleStatusChanged);`, brancher le nouvel event :
   ```typescript
     // Dispatch de l'avancement fin uniquement aux abonnés du document concerné.
     connection.on('documentProgressChanged', (event: DocumentProgressChangedEvent) => {
       progressListenersRef.current.get(event.documentId)?.forEach(listener => listener(event));
     });
   ```
5. Définir `subscribeProgress` (mémoïsé) :
   ```typescript
     const subscribeProgress = useCallback((documentId: string, listener: (event: DocumentProgressChangedEvent) => void) => {
       const map = progressListenersRef.current;
       if (!map.has(documentId)) map.set(documentId, new Set());
       map.get(documentId)!.add(listener);
       return () => {
         const set = map.get(documentId);
         set?.delete(listener);
         if (set && set.size === 0) map.delete(documentId);
       };
     }, []);
   ```
6. Exposer `subscribeProgress` dans la `value` du Provider (aux côtés de `registerPendingDocument`, `subscribe`).

- [ ] **Step 2 : Compiler**

Run (dans `Step3/src/Front`): `npm run build`
Expected: compilation sans erreur.

- [ ] **Step 3 : Commit**

```bash
git add "Step3/src/Front/src/context/IngestionNotificationsContext.tsx"
git commit -m "feat(front): canal d'abonnement à l'avancement fin par document"
```

---

### Task 13 : Composant `IngestionStepper`

**Files:**
- Create: `Step3/src/Front/src/components/IngestionStepper.tsx`

**Interfaces:**
- Consumes: `IngestionStage`, `IngestionProgress`, `DocumentStatus` (Task 11 / types existants).
- Produces: `export default function IngestionStepper(props: { status: DocumentStatus; contentType: string; fileName: string; progress: IngestionProgress | null }): JSX.Element`.

- [ ] **Step 1 : Écrire le composant**

```tsx
import type { DocumentStatus, IngestionProgress, IngestionStage } from '../types';

// Détermine la séquence d'étapes pertinente selon le type de fichier.
function stagesFor(fileName: string): IngestionStage[] {
  const lower = fileName.toLowerCase();
  const isVideo = /\.(mp4|mkv|webm|avi|mov)$/.test(lower);
  const isAudio = /\.(wav|mp3|m4a|ogg|flac)$/.test(lower);
  if (isVideo) return ['Queued', 'ExtractingAudio', 'Transcribing', 'Chunking', 'Embedding', 'Storing', 'Completed'];
  if (isAudio) return ['Queued', 'Transcribing', 'Chunking', 'Embedding', 'Storing', 'Completed'];
  return ['Queued', 'ExtractingText', 'Chunking', 'Embedding', 'Storing', 'Completed'];
}

// Libellés lisibles par étape.
const STAGE_LABELS: Record<IngestionStage, string> = {
  Queued: 'En file d’attente',
  ExtractingAudio: 'Extraction audio',
  Transcribing: 'Transcription',
  ExtractingText: 'Extraction du texte',
  Chunking: 'Découpage',
  Embedding: 'Vectorisation',
  Storing: 'Stockage',
  Completed: 'Terminé',
  Failed: 'Échec',
};

// Ordre canonique pour comparer l'avancement (les étapes avant l'étape courante sont « faites »).
const STAGE_ORDER: IngestionStage[] = [
  'Queued', 'ExtractingAudio', 'Transcribing', 'ExtractingText', 'Chunking', 'Embedding', 'Storing', 'Completed',
];

type StepState = 'done' | 'active' | 'pending' | 'failed';

/**
 * Stepper vertical du pipeline d'ingestion. Chaque étape est marquée faite/active/à venir/échouée ;
 * l'étape active à pourcentage affiche une barre de progression et ses compteurs.
 */
export default function IngestionStepper(
  { status, fileName, progress }: { status: DocumentStatus; contentType: string; fileName: string; progress: IngestionProgress | null },
) {
  const stages = stagesFor(fileName);
  const currentStage = progress?.stage ?? (status === 'Completed' ? 'Completed' : 'Queued');
  const currentIndex = STAGE_ORDER.indexOf(currentStage);

  // Détermine l'état d'affichage d'une étape donnée.
  function stateOf(stage: IngestionStage): StepState {
    if (status === 'Completed') return 'done';
    if (status === 'Failed') {
      if (stage === currentStage) return 'failed';
      return STAGE_ORDER.indexOf(stage) < currentIndex ? 'done' : 'pending';
    }
    if (stage === currentStage) return 'active';
    return STAGE_ORDER.indexOf(stage) < currentIndex ? 'done' : 'pending';
  }

  // Construit la ligne de détail (compteurs) de l'étape active.
  function detail(stage: IngestionStage): string | null {
    if (!progress || stage !== progress.stage) return null;
    const c = progress.counters;
    if (stage === 'Embedding' && c.batchCount) {
      return `lot ${c.batchIndex}/${c.batchCount}${c.chunksTotal ? ` · ${c.chunksDone}/${c.chunksTotal} chunks` : ''}`;
    }
    if (stage === 'Transcribing') {
      return progress.percent != null ? `${progress.percent} %` : 'en cours…';
    }
    return null;
  }

  return (
    <ol className="ingestion-stepper">
      {stages.map(stage => {
        const st = stateOf(stage);
        const showBar = st === 'active' && progress?.percent != null;
        return (
          <li key={stage} className={`stepper-item stepper-${st}`}>
            <span className="stepper-marker" aria-hidden />
            <div className="stepper-body">
              <span className="stepper-label">{STAGE_LABELS[stage]}</span>
              {detail(stage) && <span className="stepper-detail">{detail(stage)}</span>}
              {showBar && (
                <div className="progress-bar">
                  <div className="progress-bar-fill" style={{ width: `${progress!.percent}%` }} />
                </div>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
```

- [ ] **Step 2 : Compiler**

Run (dans `Step3/src/Front`): `npm run build`
Expected: compilation sans erreur (le composant n'est pas encore monté, mais doit typer).

- [ ] **Step 3 : Commit**

```bash
git add "Step3/src/Front/src/components/IngestionStepper.tsx"
git commit -m "feat(front): composant stepper d'avancement d'ingestion"
```

---

### Task 14 : Page `DocumentDetailPage` + route

**Files:**
- Create: `Step3/src/Front/src/pages/DocumentDetailPage.tsx`
- Modify: `Step3/src/Front/src/App.tsx`

**Interfaces:**
- Consumes: `api.documents.get` (existant), `useIngestionNotifications().subscribe` + `subscribeProgress` (Task 12), `IngestionStepper` (Task 13), `api.video.getTranscription` (existant).

- [ ] **Step 1 : Écrire la page**

```tsx
import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import IngestionStepper from '../components/IngestionStepper';
import { useIngestionNotifications } from '../context/IngestionNotificationsContext';
import type { DocumentResponse, IngestionProgress } from '../types';

const statusLabel: Record<string, string> = {
  Pending: 'En file d’attente', Processing: 'Traitement en cours…', Completed: 'Terminé', Failed: 'Échec',
};
const statusColor: Record<string, string> = {
  Pending: 'badge-warning', Processing: 'badge-info', Completed: 'badge-success', Failed: 'badge-error',
};

function formatBytes(n: number) {
  if (n < 1024) return `${n} o`;
  if (n < 1_048_576) return `${(n / 1024).toFixed(1)} Ko`;
  return `${(n / 1_048_576).toFixed(1)} Mo`;
}

/** Page de détail d'un document : suivi fin d'ingestion en temps réel + métadonnées + transcription. */
export default function DocumentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { subscribe, subscribeProgress } = useIngestionNotifications();

  const [doc, setDoc] = useState<DocumentResponse | null>(null);
  const [progress, setProgress] = useState<IngestionProgress | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [transcription, setTranscription] = useState<string | null>(null);

  // Chargement initial : document + avancement persisté à l'instant t.
  useEffect(() => {
    if (!id) return;
    setLoading(true);
    api.documents.get(id)
      .then(d => { setDoc(d); setProgress(d.ingestionProgress ?? null); })
      .catch(() => setNotFound(true))
      .finally(() => setLoading(false));
  }, [id]);

  // Statut grossier en temps réel (patch en place).
  useEffect(() => {
    if (!id) return;
    return subscribe(event => {
      if (event.documentId !== id) return;
      setDoc(prev => prev ? { ...prev, status: event.status, errorMessage: event.errorMessage } : prev);
    });
  }, [id, subscribe]);

  // Avancement fin en temps réel (filtré par id).
  useEffect(() => {
    if (!id) return;
    return subscribeProgress(id, event => {
      setProgress({ stage: event.stage, percent: event.percent, counters: event.counters, updatedAt: new Date().toISOString() });
    });
  }, [id, subscribeProgress]);

  // Une fois terminé pour une vidéo/audio, on récupère la transcription pour l'aperçu.
  useEffect(() => {
    if (!id || !doc || doc.status !== 'Completed') return;
    api.video.getTranscription(id)
      .then(t => setTranscription(t.cleanedTranscription ?? t.rawTranscription ?? null))
      .catch(() => { /* document non vidéo : pas de transcription, on ignore */ });
  }, [id, doc]);

  if (loading) return <div className="page"><div className="spinner" /></div>;
  if (notFound || !doc) {
    return (
      <div className="page">
        <div className="empty-state">
          <span>🔍</span>
          <p>Document introuvable.</p>
          <button className="btn btn-ghost" onClick={() => navigate('/')}>← Retour aux documents</button>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <button className="btn btn-ghost btn-sm" onClick={() => navigate('/')}>← Documents</button>
          <h1 className="detail-title">{doc.fileName}</h1>
        </div>
        <span className={`badge ${statusColor[doc.status]}`}>{statusLabel[doc.status]}</span>
      </div>

      <div className="detail-meta">
        <div><span className="detail-meta-label">Type</span><span>{doc.contentType}</span></div>
        <div><span className="detail-meta-label">Taille</span><span>{formatBytes(doc.fileSizeBytes)}</span></div>
        <div><span className="detail-meta-label">Ajouté le</span><span>{new Date(doc.createdAt).toLocaleString('fr-FR')}</span></div>
      </div>

      <section className="detail-section">
        <h2>Pipeline d'ingestion</h2>
        <IngestionStepper status={doc.status} contentType={doc.contentType} fileName={doc.fileName} progress={progress} />
      </section>

      {doc.status === 'Failed' && doc.errorMessage && (
        <div className="alert alert-error">{doc.errorMessage}</div>
      )}

      {doc.status === 'Completed' && transcription && (
        <section className="detail-section">
          <h2>Transcription</h2>
          <pre className="transcription-preview">{transcription}</pre>
        </section>
      )}

      {doc.status === 'Completed' && (
        <button
          className="btn btn-primary"
          onClick={() => navigate('/chat', { state: { documentId: doc.id, documentName: doc.fileName } })}
        >
          Interroger ce document dans le Chat →
        </button>
      )}
    </div>
  );
}
```

- [ ] **Step 2 : Déclarer la route**

Dans `App.tsx`, importer et ajouter la route (garder l'ordre : la route paramétrée après les fixes) :

```tsx
import DocumentDetailPage from './pages/DocumentDetailPage';
```

Dans `<Routes>`, ajouter :

```tsx
            <Route path="/documents/:id" element={<DocumentDetailPage />} />
```

- [ ] **Step 3 : Compiler**

Run (dans `Step3/src/Front`): `npm run build`
Expected: compilation sans erreur.

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Front/src/pages/DocumentDetailPage.tsx" "Step3/src/Front/src/App.tsx"
git commit -m "feat(front): page de détail d'ingestion + route /documents/:id"
```

---

### Task 15 : Rendre le statut et le nom cliquables dans `DocumentsPage`

**Files:**
- Modify: `Step3/src/Front/src/pages/DocumentsPage.tsx`

**Interfaces:**
- Consumes: route `/documents/:id` (Task 14), `useNavigate` (déjà importé).

- [ ] **Step 1 : Naviguer au clic sur le nom et le badge ; retirer le clic-ligne-sélection**

Dans `DocumentsPage.tsx` :

1. Sur la `<tr>` (ligne ~268), **retirer** `onClick={() => toggleSelect(doc.id)}` (la sélection reste possible via la checkbox uniquement, pour éviter le conflit avec la navigation).
2. Rendre la cellule du nom cliquable :
   ```tsx
   <td className="filename filename-link" onClick={() => navigate(`/documents/${doc.id}`)}>{doc.fileName}</td>
   ```
3. Rendre le badge de statut cliquable (c'est l'entrée décrite par l'utilisateur) :
   ```tsx
   <td>
     <span
       className={`badge badge-link ${statusColor[doc.status]}`}
       title={doc.errorMessage ?? 'Voir le détail de l’ingestion'}
       onClick={() => navigate(`/documents/${doc.id}`)}
     >
       {statusLabel[doc.status]}
     </span>
   </td>
   ```

- [ ] **Step 2 : Compiler**

Run (dans `Step3/src/Front`): `npm run build`
Expected: compilation sans erreur.

- [ ] **Step 3 : Vérification manuelle**

Lancer front + back, uploader un document, cliquer sur son statut « Traitement en cours… » dans la
liste → arrivée sur `/documents/:id`, le stepper progresse en temps réel (étapes, %, compteurs),
puis passe à « Terminé » sans rechargement. Rafraîchir la page en plein traitement → l'étape
courante s'affiche immédiatement (persistée) et le % se réaffine au tick suivant.

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Front/src/pages/DocumentsPage.tsx"
git commit -m "feat(front): accès à la page de détail depuis le statut et le nom du document"
```

---

### Task 16 : Styles

**Files:**
- Modify: `Step3/src/Front/src/index.css`

- [ ] **Step 1 : Ajouter les styles du stepper et de la page de détail**

Ajouter à la fin de `index.css` :

```css
/* --- Page de détail d'ingestion --- */
.detail-title { margin: 0.25rem 0 0; }
.detail-meta { display: flex; gap: 2rem; flex-wrap: wrap; margin: 1rem 0 1.5rem; }
.detail-meta > div { display: flex; flex-direction: column; }
.detail-meta-label { font-size: 0.75rem; opacity: 0.6; text-transform: uppercase; letter-spacing: 0.04em; }
.detail-section { margin: 1.5rem 0; }
.transcription-preview { white-space: pre-wrap; max-height: 320px; overflow: auto; padding: 1rem; border-radius: 8px; background: rgba(127,127,127,0.08); font-size: 0.85rem; }

/* Liens cliquables dans la liste */
.filename-link, .badge-link { cursor: pointer; }
.filename-link:hover { text-decoration: underline; }
.badge-link:hover { filter: brightness(1.08); }

/* --- Stepper vertical --- */
.ingestion-stepper { list-style: none; padding: 0; margin: 0; }
.stepper-item { display: flex; gap: 0.75rem; padding-bottom: 1.25rem; position: relative; }
.stepper-item:not(:last-child)::before {
  content: ''; position: absolute; left: 7px; top: 18px; bottom: 0; width: 2px; background: rgba(127,127,127,0.25);
}
.stepper-marker { width: 16px; height: 16px; border-radius: 50%; flex: 0 0 auto; margin-top: 2px; border: 2px solid rgba(127,127,127,0.4); background: transparent; z-index: 1; }
.stepper-done .stepper-marker { background: #22c55e; border-color: #22c55e; }
.stepper-active .stepper-marker { background: #3b82f6; border-color: #3b82f6; animation: stepper-pulse 1.2s ease-in-out infinite; }
.stepper-failed .stepper-marker { background: #ef4444; border-color: #ef4444; }
.stepper-body { display: flex; flex-direction: column; gap: 0.35rem; flex: 1; }
.stepper-label { font-weight: 500; }
.stepper-pending .stepper-label { opacity: 0.5; }
.stepper-detail { font-size: 0.8rem; opacity: 0.7; }
@keyframes stepper-pulse { 0%,100% { box-shadow: 0 0 0 0 rgba(59,130,246,0.5); } 50% { box-shadow: 0 0 0 5px rgba(59,130,246,0); } }
```

(Si `.progress-bar` / `.progress-bar-fill` n'existent pas déjà — elles sont utilisées par
`DocumentsPage` donc probablement présentes — les ajouter :)

```css
.progress-bar { height: 6px; border-radius: 3px; background: rgba(127,127,127,0.2); overflow: hidden; }
.progress-bar-fill { height: 100%; background: #3b82f6; transition: width 0.3s ease; }
```

> Avant d'ajouter `.progress-bar`, vérifier avec une recherche qu'elles ne sont pas déjà définies
> (elles sont référencées dans `DocumentsPage.tsx`) pour ne pas les dupliquer.

- [ ] **Step 2 : Compiler + smoke visuel**

Run (dans `Step3/src/Front`): `npm run build`
Expected: build OK. Vérifier visuellement le stepper (marqueurs faits/actif/à venir, barre animée).

- [ ] **Step 3 : Commit**

```bash
git add "Step3/src/Front/src/index.css"
git commit -m "style(front): styles du stepper et de la page de détail d'ingestion"
```

---

## Vérification finale (end-to-end)

- [ ] Back : `dotnet build "Step3/src/Back/AIExperience.slnx"` vert ; `dotnet test "Step3/src/Back/AIExperience.slnx"` (si Smart App Control le permet) vert.
- [ ] Front : `npm run build` vert dans `Step3/src/Front`.
- [ ] Migration appliquée : colonne `ingestion_progress` présente dans `documents` (base port 5433).
- [ ] Smoke PDF : upload → clic statut → stepper `Extraction du texte → Découpage → Vectorisation (lot i/n, %) → Stockage → Terminé` en temps réel.
- [ ] Smoke vidéo : upload d'un `.mp4` → stepper `Extraction audio → Transcription (%) → … → Terminé`, aperçu transcription affiché.
- [ ] Reload en plein traitement → l'étape courante s'affiche immédiatement (persistée).
- [ ] Échec simulé (ex. fichier corrompu) → l'étape fautive est marquée en rouge + message d'erreur.
- [ ] Terminé → `ingestionProgress` redevient `null` dans `GET /api/documents/{id}`.
```
