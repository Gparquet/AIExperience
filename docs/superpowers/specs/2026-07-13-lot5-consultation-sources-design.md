# Design — Lot 5 : Consultation des sources depuis les citations

Date : 2026-07-13
Périmètre : `Step3/src/Back` + `Step3/src/Front`
Référence plan : `Step3/PLAN-AMELIORATION-RAG.md`, Lot 5 (points 36-39, lignes 314-358), constats
I-24 (lignes 903-943) et R-19 (lignes 1273-1316)

---

## 1. Contexte et constat

La chaîne de métadonnées de citation (page, section, timestamps vidéo) est complète de bout en
bout depuis les Lots 1 et 2-bis, mais débouche sur une impasse :

- **I-24** : `WorkFileStore.SaveAsync` copie l'upload dans le répertoire de travail
  (`Ingestion:WorkDirectory`), et `IngestDocumentHandler` **supprime** ce fichier une fois le
  traitement terminé (succès ou échec). Aucune copie durable n'est conservée : après `Completed`,
  l'original n'existe plus nulle part sur disque. `Document.FileReference` ne pointe donc, en
  pratique, que vers un fichier de travail éphémère.
- **R-19** : le front affiche pour chaque citation le document, la page, la section et les
  timestamps vidéo (tout est déjà dans `CitationResponse`), mais rien n'est cliquable — aucun
  endpoint ne sert le contenu d'un document, et I-24 aggrave le tout : même en l'ajoutant, le
  fichier n'existerait plus.

C'est le **plus gros gain produit visible pour un coût modéré** identifié dans le plan : cliquer
une citation ouvre le PDF à la bonne page ou lance la vidéo au bon timestamp. Bénéfice opérationnel
induit : la ré-ingestion sans re-upload, déjà subie manuellement deux fois (préfixes `nomic` R-15,
timestamps inline I-21) et inévitable à chaque évolution du pipeline (OCR, chunker, modèle
d'embedding).

**Précision d'implémentation (revue avant conception)** : le plan document date du 13/07 et
suppose encore MediatR pour les commandes d'écriture. Le commit `d382727` a remplacé MediatR par un
CQRS maison (`ICommand<TResponse>` / `ICommandHandler<TCommand,TResponse>` / `ICommandDispatcher`,
scan d'assembly automatique via `AddCqrs`, validation via `ICommandValidator<TCommand>` +
`ValidationErrors`). Ce design suit ce pattern actuel, pas celui décrit dans le plan.

De même, le plan proposait pour Lot 2-ter un `Channel<Guid>` en mémoire comme file d'ingestion ;
l'implémentation réelle utilise la table **`outbox_messages`** (`IOutboxRepository`,
`IngestionWorker` en `BackgroundService` qui sonde la table et rejoue `IngestDocumentCommand`). La
ré-ingestion (point 39) réutilise donc ce mécanisme outbox existant, pas un canal en mémoire.

## 2. Décisions de cadrage (validées avec Geoffrey)

1. **Périmètre complet** : les 4 points du plan (36 stockage durable, 37 endpoint, 38 viewer front,
   39 ré-ingestion), plus une extension décidée pendant le cadrage : **une page d'administration**
   pilotant des paramètres applicatifs en base (point G).
2. **Pas de quota/rétention pour cette itération.** Le risque de volumétrie (conserver les vidéos
   coûte cher en disque) reste une dette documentée, arbitrable plus tard si le disque devient un
   problème réel — pas de politique de purge automatique ni de taille max codée en dur.
3. **Panneau latéral (drawer)**, pas de modale : consultation d'une source sans interrompre la
   conversation en cours.
4. **Formats sans viewer natif** (DOCX/XLSX/PPTX/TXT/MD/JSON/CSV, issus du Lot 2) : les formats
   texte s'affichent bruts dans le panneau ; les formats binaires (DOCX/XLSX/PPTX) proposent un
   téléchargement.
5. **Paramètres applicatifs en base, table générique, extensible sans migration future.** Il n'y
   aura pas qu'un seul réglage (`storage_enabled`) : d'autres suivront (quota, rétention par type,
   réglages du pipeline RAG aujourd'hui figés en `appsettings.json`...). **Note pour la suite : tout
   nouveau réglage administrable à chaud doit être ajouté dans cette même table
   `application_settings`**, pas dans une colonne dédiée ni une nouvelle table — c'est la raison
   d'être du modèle clé-valeur retenu ci-dessous plutôt qu'une colonne booléenne sur `documents`.
   Seul `storage_enabled` est implémenté dans cette itération ; les autres candidats (quota,
   rétention par type, bascule Reranker/Compression à chaud) sont explicitement hors périmètre
   (§6) mais la table est conçue pour les accueillir sans changement de schéma.
6. **Aucun effet rétroactif** à la désactivation du stockage : les fichiers déjà stockés restent
   consultables ; seules les futures ingestions cessent de persister l'original.
7. **Aucune restriction d'accès nouvelle** sur la page d'administration au-delà de l'existant
   (pas d'authentification dans le projet actuellement) — risque déjà connu, pas aggravé
   spécifiquement par ce lot.

## 3. Architecture

### 3.1. Stockage durable des originaux (point 36)

```
IFileStorage (Domain/Interfaces/Services/)
        │ implémenté par
        ▼
LocalFileStorage (Infrastructure) ── StorageOptions.Directory (config, options pattern)
```

```csharp
// Domain/Interfaces/Services/IFileStorage.cs
/// <summary>Stockage durable des fichiers originaux des documents ingérés.</summary>
public interface IFileStorage
{
    /// <summary>Déplace un fichier du répertoire de travail vers le stockage définitif ; retourne
    /// le chemin de stockage définitif à persister sur <see cref="Document.StoragePath"/>.</summary>
    Task<string> PersistAsync(string workFilePath, Guid documentId, CancellationToken ct);

    /// <summary>Copie (jamais déplace : l'original stocké doit rester intact) l'original stocké
    /// vers un nouveau fichier de travail, pour une ré-ingestion (point 39).</summary>
    Task<string> CopyToWorkDirectoryAsync(string storagePath, Guid documentId, string workDirectory, CancellationToken ct);

    /// <summary>Ouvre l'original en lecture (streaming, jamais chargé en mémoire).</summary>
    Stream OpenRead(string storagePath);

    /// <summary>Vrai si le fichier existe encore physiquement (protection contre une ligne orpheline
    /// en base — suppression manuelle du répertoire de stockage, disque externe débranché...).</summary>
    bool Exists(string storagePath);

    /// <summary>Supprime l'original (appelé à la suppression du document).</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct);
}
```

`LocalFileStorage` (Infrastructure) : `File.Move` pour `PersistAsync` (même volume que
`Storage:Directory`, donc déplacement atomique et instantané, pas de copie), `File.Copy` pour
`CopyToWorkDirectoryAsync`. Nommage déterministe `{documentId}{extension}`, même convention que
`WorkFileStore`. Enregistré en `Singleton` (sans état, même famille que `ContentTypeResolver`) via
une méthode `AddFileStorage()` ajoutée à la chaîne de `AddInfrastructure()`.

```csharp
// Infrastructure/Options/StorageOptions.cs — même pattern que IngestionOptions
public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    /// <summary>Répertoire de stockage définitif des originaux, distinct du répertoire de travail.</summary>
    public required string Directory { get; set; }
}
```

**`Document` (Domain)** gagne une propriété miroir de `FileReference` :

```csharp
/// <summary>Chemin de stockage définitif de l'original, une fois l'ingestion terminée avec succès
/// et le stockage activé (voir <c>storage_enabled</c>, §3.3). <c>null</c> si jamais persisté —
/// document ingéré avant ce lot, stockage désactivé au moment de l'ingestion, ou échec.</summary>
public string? StoragePath { get; private set; }

/// <summary>Enregistre le chemin de stockage définitif de l'original.</summary>
public void SetStoragePath(string storagePath)
{
    StoragePath = storagePath;
    UpdatedAt = DateTimeOffset.UtcNow;
}
```

`ContentType` existe déjà sur `Document` (renseigné à l'upload via `IContentTypeResolver`) et est
réutilisé tel quel pour l'en-tête `Content-Type` de l'endpoint de consultation (§3.6) — pas de
colonne supplémentaire nécessaire pour ça.

### 3.2. Schéma SQL

- Colonne `storage_path VARCHAR(500) NULL` sur `documents`.
- Table `application_settings` (voir §3.3).
- Script `Step3/scripts/migrate-lot5-file-storage.sql` (suit le pattern des migrations
  précédentes : `migrate-temporal-chunks.sql`, `migrate-lot2ter-async-ingestion.sql`...) **et**
  `init.sql` mis à jour dans le même changement (leçon I-18 : `init.sql` avait dérivé du schéma réel
  après une migration appliquée à la main sans être reportée dedans).

```sql
-- migrate-lot5-file-storage.sql
ALTER TABLE documents ADD COLUMN IF NOT EXISTS storage_path VARCHAR(500) NULL;

CREATE TABLE IF NOT EXISTS application_settings (
    key        VARCHAR(100) PRIMARY KEY,
    value      VARCHAR(500) NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO application_settings (key, value)
VALUES ('storage_enabled', 'true')
ON CONFLICT (key) DO NOTHING;
```

### 3.3. Paramètres applicatifs pilotables à chaud (nouveau, décision #5)

```
application_settings (clé/valeur, générique)
        │
ISettingsRepository (Domain, lecture/écriture brute)
        │ implémenté par
        ▼
EfSettingsRepository (Infrastructure)
        │
ISettingsService (Application, lecture typée) ──── UpdateStorageSettingCommand (écriture, CQRS)
```

```csharp
// Domain/Interfaces/Repositories/ISettingsRepository.cs
/// <summary>Accès brut clé/valeur à la table application_settings — générique, pour accueillir tout
/// futur réglage administrable à chaud sans nouvelle migration (voir décision #5 du design).</summary>
public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
```

```csharp
// Application/Services/SettingsService.cs
public interface ISettingsService
{
    Task<bool> GetStorageEnabledAsync(CancellationToken ct = default);
}

public sealed class SettingsService(ISettingsRepository repository) : ISettingsService
{
    internal const string StorageEnabledKey = "storage_enabled";

    public async Task<bool> GetStorageEnabledAsync(CancellationToken ct = default)
    {
        var raw = await repository.GetAsync(StorageEnabledKey, ct);
        return ParseBool(raw, defaultValue: true);
    }

    /// <summary>Pure, testable indépendamment du repository : une valeur absente (clé jamais
    /// initialisée, ex. base créée avant le seed) ou corrompue retombe sur le défaut plutôt que de
    /// faire échouer l'ingestion.</summary>
    internal static bool ParseBool(string? raw, bool defaultValue) =>
        bool.TryParse(raw, out var value) ? value : defaultValue;
}
```

Écriture via le CQRS maison (convention : écriture = commande) :

```csharp
public sealed record UpdateStorageSettingCommand : ICommand<bool>
{
    public required bool Enabled { get; init; }
}

public sealed class UpdateStorageSettingHandler(ISettingsRepository repository)
    : ICommandHandler<UpdateStorageSettingCommand, bool>
{
    public async Task<bool> HandleAsync(UpdateStorageSettingCommand request, CancellationToken ct)
    {
        await repository.SetAsync(SettingsService.StorageEnabledKey, request.Enabled.ToString(), ct);
        return request.Enabled;
    }
}
```

### 3.4. Intégration dans `IngestDocumentHandler`

Point d'intégration unique, minimal par rapport au code actuel : entre l'appel d'ingestion et le
passage à `Completed`, avant que `statusUpdater.MarkCompletedAsync` ne persiste le document (pour
que `StoragePath` soit inclus dans la même sauvegarde) :

```csharp
await ingestionService.IngestAsync(...); // ou IngestVideoOrAudioAsync, inchangé

if (await settingsService.GetStorageEnabledAsync(cancellationToken))
{
    var storagePath = await fileStorage.PersistAsync(filePath, document.Id, cancellationToken);
    document.SetStoragePath(storagePath);
}
// Si désactivé : comportement actuel inchangé, y compris la suppression du fichier de travail
// juste après (le File.Move de PersistAsync a déjà vidé filePath quand le stockage est actif,
// donc le File.Exists(filePath) en fin de méthode devient naturellement un no-op — aucun risque
// de double traitement à gérer explicitement).

await statusUpdater.MarkCompletedAsync(document, cancellationToken);
await progressReporter.ClearAsync(document.Id, cancellationToken);
```

`IngestDocumentHandler` gagne `IFileStorage fileStorage` et `ISettingsService settingsService` en
paramètres de constructeur. Le chemin d'échec (catch `Exception`) et `OperationCanceledException`
restent inchangés — le fichier de travail n'est jamais promu en stockage définitif dans ces cas.

### 3.5. `DeleteDocumentHandler` — suppression du fichier stocké

```csharp
public sealed class DeleteDocumentHandler(
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteDocumentCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteDocumentCommand request, CancellationToken ct)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, ct);
        if (document is null) return false;

        await documentRepository.DeleteAsync(request.DocumentId, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // La donnée métier (le document) est déjà supprimée à ce stade : un échec de suppression
        // du fichier physique orpheline le fichier sur disque mais ne doit pas faire échouer la
        // commande — best-effort, loggé côté Infrastructure.
        if (document.StoragePath is not null)
            await fileStorage.DeleteAsync(document.StoragePath, ct);

        return true;
    }
}
```

### 3.6. Endpoint de consultation (point 37)

Lecture directe (pas de commande CQRS, convention lecture = service direct respectée) :

```csharp
[HttpGet("{id:guid}/content")]
public async Task<IActionResult> GetContent(Guid id, CancellationToken ct)
{
    var doc = await documentRepository.GetByIdAsync(id, ct);
    if (doc?.StoragePath is null || !fileStorage.Exists(doc.StoragePath))
        return NotFound();

    // enableRangeProcessing : le navigateur peut demander des plages d'octets — indispensable
    // pour que <video>/<audio> fasse un seek sans re-télécharger tout le fichier.
    return File(fileStorage.OpenRead(doc.StoragePath), doc.ContentType, enableRangeProcessing: true);
}
```

### 3.7. Viewer front (point 38)

**Back — DTO enrichi** : `Citation` (Domain) porte déjà `DocumentId` en interne ; il suffit de
l'ajouter au DTO et au mapping, tous deux dans `AIExperience.Web.Api` :

```csharp
// DTOs/ChatDtos.cs
public record CitationResponse(
    Guid DocumentId, string DocumentName, int? PageNumber, string Excerpt, double Score,
    string? SectionTitle, int ChunkIndex, double? StartTimeSeconds, double? EndTimeSeconds);
```

```csharp
// ChatController.cs — mapping existant, un seul argument ajouté
.Select(c => new CitationResponse(c.DocumentId, c.DocumentName, c.PageNumber, c.Excerpt, c.Score,
    c.SectionTitle, c.ChunkIndex, c.StartTime?.TotalSeconds, c.EndTime?.TotalSeconds))
```

**Front — type enrichi** (`types/index.ts`) :

```typescript
export interface CitationResponse {
  documentId: string;             // NOUVEAU — clé de résolution vers le document déjà chargé
  documentName: string;
  pageNumber: number | null;
  excerpt: string;
  score: number;
  sectionTitle?: string | null;
  chunkIndex?: number;
  startTimeSeconds?: number | null;  // NOUVEAU
  endTimeSeconds?: number | null;    // NOUVEAU
}
```

**Résolution du type de contenu sans appel réseau supplémentaire** : `ChatPage.tsx` charge déjà
`api.documents.list()` dans un état `documents` (utilisé par le sélecteur de document existant).
Le panneau résout `contentType` par `documents.find(d => d.id === citation.documentId)?.contentType`
— pas de nouvel appel API pour ça.

**Nouveau composant `SourcePanel`** (drawer latéral), ouvert au clic sur une citation (mode RAG et
mode Classique) :

```tsx
// Classification pure, testable sans dépendance — même esprit que les helpers back purs du projet
// (RagStrategyResolver, TokenBudgetSelector...).
type ViewerKind = 'pdf' | 'media' | 'text' | 'download';

function getViewerKind(contentType: string | undefined): ViewerKind {
  if (contentType === 'application/pdf') return 'pdf';
  if (contentType?.startsWith('video/') || contentType?.startsWith('audio/')) return 'media';
  if (contentType?.startsWith('text/') || contentType === 'application/json') return 'text';
  return 'download';
}
```

- `pdf` → `<iframe src={`${API_BASE}/api/documents/${documentId}/content#page=${pageNumber ?? 1}`} />`
  (ancre `#page=` honorée par Chromium/Firefox, zéro dépendance).
- `media` → `<video>`/`<audio>` natif selon `contentType`, `currentTime = startTimeSeconds ?? 0`,
  lecture automatique au montage.
- `text` → contenu récupéré par `fetch(.../content)` et affiché dans un `<pre>` (évite le rendu brut
  parfois disgracieux d'un `<iframe>` sur du texte selon les navigateurs).
- `download` → `<a href={`.../content`} download>Télécharger {documentName}</a>` (DOCX/XLSX/PPTX,
  pas de viewer navigateur natif).

### 3.8. Ré-ingestion (point 39)

```csharp
public sealed record ReingestDocumentCommand : ICommand<ReingestDocumentResponse>
{
    public required Guid DocumentId { get; init; }
}

public sealed record ReingestDocumentResponse
{
    public required Guid DocumentId { get; init; }
    public required IngestionStatus Status { get; init; }
}
```

```csharp
public sealed class ReingestDocumentHandler(
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService,
    IFileStorage fileStorage,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    IOptions<IngestionOptions> ingestionOptions) : ICommandHandler<ReingestDocumentCommand, ReingestDocumentResponse>
{
    public async Task<ReingestDocumentResponse> HandleAsync(ReingestDocumentCommand request, CancellationToken ct)
    {
        var document = await documentRepository.GetByIdAsync(request.DocumentId, ct)
            ?? throw new InvalidOperationException($"Document introuvable : {request.DocumentId}");

        if (document.StoragePath is null)
            throw new DocumentNotReingestibleException(document.Id);
            // → mappé en 409 Conflict côté API (pas d'original stocké : document antérieur à ce
            // lot, ou stockage désactivé au moment de l'ingestion initiale).

        // Purge les chunks existants : régénérés par le pipeline standard, comme un upload initial.
        await vectorStoreService.DeleteByDocumentIdAsync(document.Id, ct);

        var workPath = await fileStorage.CopyToWorkDirectoryAsync(
            document.StoragePath, document.Id, ingestionOptions.Value.WorkDirectory, ct);
        document.SetFileReference(workPath);
        document.ResetToPending(); // méthode déjà existante sur Document
        await documentRepository.UpdateAsync(document, ct);

        // Même plomberie outbox que l'upload (UploadDocumentHandler) : le worker existant
        // (IngestionWorker) rejoue IngestDocumentCommand sans code de traitement dédié.
        var payload = JsonSerializer.Serialize(new IngestionJobPayload { DocumentId = document.Id });
        outboxRepository.Add(OutboxMessage.Create(IngestionEventTypes.DocumentIngestionRequested, payload));

        await unitOfWork.SaveChangesAsync(ct);

        return new ReingestDocumentResponse { DocumentId = document.Id, Status = document.Status };
    }
}
```

Endpoint : `POST /api/documents/{id:guid}/reingest` — appelle `dispatcher.SendAsync(new
ReingestDocumentCommand { DocumentId = id }, ct)` puis `ingestionSignal.Pulse()` (même geste que
`Upload`, pour réveiller le worker immédiatement plutôt que d'attendre le prochain tour de sonde).

### 3.9. Page d'administration (nouveau, décision #5)

```csharp
[ApiController]
[Route("api/admin/settings")]
public class AdminSettingsController(
    ICommandDispatcher dispatcher, ISettingsService settingsService) : ControllerBase
{
    [HttpGet("storage")]
    public async Task<ActionResult<StorageSettingResponse>> GetStorage(CancellationToken ct)
        => Ok(new StorageSettingResponse(await settingsService.GetStorageEnabledAsync(ct)));

    [HttpPut("storage")]
    public async Task<ActionResult<StorageSettingResponse>> SetStorage(
        [FromBody] UpdateStorageSettingRequest request, CancellationToken ct)
    {
        var enabled = await dispatcher.SendAsync(new UpdateStorageSettingCommand { Enabled = request.Enabled }, ct);
        return Ok(new StorageSettingResponse(enabled));
    }
}
```

Front : nouvelle route `/admin` (`AdminPage.tsx`), onglet ajouté à la topbar. Un toggle switch avec
libellé explicite sur l'absence d'effet rétroactif (décision #6) :

> « Conserver les originaux pour permettre la consultation des sources — désactiver arrête la
> sauvegarde des **nouveaux** documents, sans effet sur ceux déjà stockés. »

## 4. Cas limites & robustesse

| Cas | Comportement |
|-----|--------------|
| `storage_enabled = false` | Comportement actuel inchangé (I-24 non corrigé pour ces documents) : fichier de travail supprimé, `StoragePath` reste `null`, viewer affiche "source non disponible" |
| Ligne `documents.storage_path` renseignée mais fichier absent du disque (suppression manuelle, disque externe débranché) | `IFileStorage.Exists` → `404 NotFound` sur l'endpoint de consultation, pas d'exception non gérée |
| `ReingestDocumentCommand` sur un document sans `StoragePath` | `DocumentNotReingestibleException` → `409 Conflict`, message explicite |
| `ReingestDocumentCommand` sur un document déjà `Pending`/`Processing` | Autorisé (idempotent côté chunks : `DeleteByDocumentIdAsync` sur un document déjà vide ne fait rien) — pas de garde-fou supplémentaire jugé nécessaire pour cette itération |
| `application_settings` : clé jamais seedée (base créée avant ce lot puis migration appliquée) | Le script de migration seed `storage_enabled = 'true'` avec `ON CONFLICT DO NOTHING` — toujours présente après migration ; `ParseBool` retombe sur `true` par sécurité si absente quand même |
| Formats sans viewer ni téléchargement praticable (ex. citation sur un document supprimé entre-temps) | Clic ouvre quand même le panneau, l'appel `.../content` renvoie `404`, le panneau affiche un message d'erreur au lieu de rester silencieux |

## 5. Hors périmètre (YAGNI, à reconfirmer plus tard si besoin)

- Quota/taille max de stockage, politique de rétention par type de fichier (décision #2).
- Purge rétroactive des originaux à la désactivation du toggle (décision #6).
- Autres réglages administrables à chaud (bascule Reranker/ContextCompression, `RagOptions` divers)
  — la table `application_settings` les accueillera plus tard sans migration supplémentaire
  (décision #5), mais aucun n'est câblé dans cette itération.
- Surlignage de l'extrait cité dans le PDF (imposerait `pdf.js` + recherche de texte).
- Miniatures/aperçus des documents dans la liste.
- Authentification/autorisation sur `/admin` ou sur l'endpoint de consultation — dette déjà connue,
  pas aggravée spécifiquement par ce lot mais pas résolue non plus.

## 6. Risques & dette technique signalés

- **Volumétrie du stockage des originaux** : conserver les vidéos uploadées peut coûter cher en
  disque, aucune limite pour cette itération (décision #2, mitigée par le toggle `storage_enabled`
  qui permet de désactiver la sauvegarde à chaud sans redéploiement si le disque devient un
  problème).
- **`GET /api/documents/{id}/content` = nouvelle surface d'exposition** : à vérifier l'appartenance
  du document à l'utilisateur le jour où l'authentification existera — non filtré aujourd'hui,
  cohérent avec le reste de l'API (`DevAuth`).
- **Page d'administration sans restriction d'accès** : accessible à qui atteint le front, comme le
  reste de l'API en l'absence d'authentification — signalé, pas résolu ici.
- **Isolation par utilisateur absente de toute la chaîne de lecture** (pgvector, full-text,
  consultation de sources) — dette transverse déjà connue du plan, non spécifique à ce lot.

## 7. Vérification manuelle

- Upload PDF → question → clic citation → le PDF s'ouvre dans le panneau à la page citée.
- Upload vidéo → question → clic citation → la vidéo démarre au timestamp cité ; seek dans la
  vidéo (valide les Range requests de l'endpoint).
- Upload DOCX/TXT → question → clic citation → texte brut affiché (TXT) ou téléchargement proposé
  (DOCX).
- Suppression d'un document → fichier stocké supprimé du disque (vérifier `Storage:Directory`).
- `POST /api/documents/{id}/reingest` sur un document existant → statut repasse par
  `Pending`→`Processing`→`Completed`, chunks régénérés sans doublons, notification SignalR reçue
  comme pour un upload.
- `POST /api/documents/{id}/reingest` sur un document sans `StoragePath` → `409 Conflict`.
- Page `/admin` : désactiver `storage_enabled` → uploader un nouveau document → `StoragePath` reste
  `null` en base, viewer indisponible pour ce document, mais un document uploadé **avant** la
  désactivation reste consultable (pas d'effet rétroactif, décision #6).

## 8. Tests

- **`SettingsServiceTests.ParseBool`** (pur, sans mock) : valeur `"true"`/`"false"` valide, valeur
  `null`/absente → défaut, valeur corrompue (ex. `"oui"`) → défaut.
- **`LocalFileStorageTests`** : `PersistAsync` déplace effectivement le fichier (le chemin source
  n'existe plus, le chemin retourné existe) ; `CopyToWorkDirectoryAsync` copie sans supprimer la
  source ; `Exists`/`DeleteAsync` sur un répertoire temporaire réel (pas de mock — I/O réelle sur
  `Path.GetTempPath()`, nettoyée en fin de test), même esprit que les tests déjà présents sur les
  chunkers (pas de dépendance externe simulée).
- Pas de nouveau test d'intégration sur `IngestDocumentHandler`/`DeleteDocumentHandler`/
  `ReingestDocumentHandler` (nombreuses dépendances injectées) — même arbitrage que pour
  `RagPipelineService` (R-16/R-9, Lot 0-bis) et le budget de tokens (Lot 3/R-11) : vérifié
  manuellement (§7) plutôt que mocké in extenso.
- Front : pas d'infrastructure de test automatisé existante sur ce projet (aucun framework front
  configuré) — `SourcePanel`/`getViewerKind` vérifiés manuellement (§7), cohérent avec le reste du
  front Step 3.
