# Design — Page de détail & suivi fin d'ingestion

**Date :** 2026-07-10
**Périmètre :** Step3 (back-end .NET + front React)
**Branche de travail :** `feature/lot2ter-ingestion-asynchrone`

---

## 1. Objectif

Depuis la page d'accueil (liste des documents), permettre de cliquer sur un document
(en particulier un document « en cours de traitement ») pour ouvrir une **page de détail**
présentant l'état complet de son ingestion : **où en est le pipeline, avec un suivi fin
(pourcentages et compteurs)**, en temps réel.

La page sert **tous les états** d'un document — pas uniquement « en cours » :

- **En cours** → suivi live du pipeline, étape par étape, avec `%` et compteurs.
- **Terminé** → statistiques finales (nb de chunks, langue détectée, durées, dates) + pour la
  vidéo un aperçu de la transcription.
- **Échec** → étape à laquelle le pipeline a échoué + message d'erreur utilisateur.

---

## 2. Contexte technique existant (Step3)

- Ingestion **déjà asynchrone** : `POST /api/documents` renvoie `202 Accepted`, un worker en
  arrière-plan (`IngestionWorker`, pattern Outbox) traite ensuite le document.
- Le worker traite **un seul document à la fois** (services Whisper/FFmpeg partagés, non
  réentrants) — cf. `IngestionWorker.ProcessPendingMessagesAsync`. **Conséquence clé : le volume
  d'écritures de progression est structurellement borné à un document à la fois.**
- Statuts actuels : `IngestionStatus { Pending, Processing, Completed, Failed }` — **grossiers**.
  Les sous-étapes du pipeline (extraction → chunking → embedding → stockage ; vidéo : extraction
  audio → transcription → chunking → embedding → stockage) existent dans le code mais ne sont ni
  suivies, ni persistées, ni exposées.
- Temps réel : hub SignalR `IngestionHub`, event descendant unique **`documentStatusChanged`**
  `{ documentId, status, errorMessage }`, poussé par `SignalRIngestionNotifier` via l'abstraction
  Domain `IIngestionNotifier`. Implémentation no-op côté Console (`NullIngestionNotifier`).
- Front : `IngestionNotificationsContext` gère la connexion hub (`createIngestionHubConnection`),
  un **repli en polling** sur `GET /api/documents/{id}` (le statut en base est la source de
  vérité), un pattern `subscribe(listener)` et des toasts de fin de traitement.
- Routes front actuelles : `/` (Documents), `/video`, `/chat` — **sans paramètre**.

---

## 3. Décisions de conception

| # | Décision | Justification |
|---|----------|---------------|
| D1 | Granularité **fine** (`%` + compteurs), pas seulement des étapes nommées | Choix explicite utilisateur |
| D2 | Progression **persistée en base** (throttlée) **et** poussée finement en live | Survit au reload / ouverture tardive ; volume négligeable (worker séquentiel) |
| D3 | Page dédiée **`/documents/:id`** (pas de modale) | « On tombe sur une page » |
| D4 | Page **tous états** (en cours / terminé / échec) | Page de détail réutilisable plutôt qu'un écran mono-usage |
| D5 | Stockage progression = **colonne JSONB `ingestion_progress`** sur `documents` | Une colonne, flexible ; vs 6 colonnes éparses ou table dédiée (surdimensionné) |
| D6 | Nouvel event SignalR **`documentProgressChanged`** distinct de `documentStatusChanged` | Ne pas polluer le canal statut grossier ; éviter d'imposer les ticks haute fréquence à la liste |

---

## 4. Modèle d'étapes (`IngestionStage`)

Enum canonique côté Domain. La page n'affiche que les étapes pertinentes selon le type de document.

```
Queued            // En file d'attente (message outbox pas encore dépilé)
ExtractingAudio   // Extraction audio FFmpeg (vidéo uniquement)
Transcribing      // Transcription Whisper (vidéo/audio)          — % réel
ExtractingText    // Extraction texte PDF/doc (non-vidéo)
Chunking          // Découpage                                    — atomique
Embedding         // Vectorisation                                — % réel (sous-lots)
Storing           // Upsert pgvector                              — atomique
Completed
Failed
```

Séquences par type :

| PDF / texte | Vidéo | Audio |
|---|---|---|
| Queued | Queued | Queued |
| ExtractingText | ExtractingAudio | — |
| — | Transcribing `%` | Transcribing `%` |
| Chunking | Chunking | Chunking |
| Embedding `%` | Embedding `%` | Embedding `%` |
| Storing | Storing | Storing |
| Completed | Completed | Completed |

- **`%` réel** :
  - `Transcribing` → `segment.End / duréeTotale` (callback par segment de Whisper.net).
  - `Embedding` → `lotCourant / nombreDeLots` (découpage de l'embedding en sous-lots).
- **Étapes atomiques** (`Chunking`, `Storing`, `ExtractingAudio`, `ExtractingText`) → affichées
  en ●/○ (fait / en cours / à venir), spinner sur l'étape active, **sans** barre `%`.
  - `ExtractingText` : `%` par page **hors périmètre** de cette itération (nécessiterait
    d'instrumenter l'extracteur). Traitée comme atomique pour l'instant.

---

## 5. Modèle de données de progression

Forme JSON persistée dans `documents.ingestion_progress` (nullable) et transportée par SignalR :

```jsonc
{
  "stage": "Embedding",        // valeur de IngestionStage
  "percent": 37,               // % de l'étape courante ; null si étape atomique/indéterminée
  "counters": {                // tous optionnels selon l'étape
    "batchIndex": 3,
    "batchCount": 8,
    "chunksDone": 42,
    "chunksTotal": 120,
    "segmentsDone": 128,
    "segmentsTotal": 540
  },
  "updatedAt": "2026-07-10T14:33:12Z"
}
```

- L'étape à laquelle un échec s'est produit = **dernière `stage` persistée** au moment du passage
  en `Failed` (on ne remet pas la progression à zéro à l'échec → « où ça a cassé »).
- La liste des étapes « déjà franchies » côté UI est **dérivée** de `stage` + du type de document
  (ordre canonique connu), pas stockée.

---

## 6. Architecture back-end

### 6.1 Domain

- `enum IngestionStage` (section 4).
- `IngestionProgress` — objet valeur immuable : `Stage`, `Percent` (nullable), `Counters`,
  `UpdatedAt`. Fabrique statique `Create(...)`.
- `IIngestionProgressReporter` — abstraction injectée dans le pipeline :

```csharp
public interface IIngestionProgressReporter
{
    /// Signale l'entrée dans une nouvelle étape (persistée immédiatement + notifiée).
    Task EnterStageAsync(Guid documentId, IngestionStage stage, CancellationToken ct = default);

    /// Signale l'avancement fin de l'étape courante (notifié à chaque appel,
    /// persisté seulement selon la politique de throttling).
    Task ReportAsync(Guid documentId, IngestionStage stage, int? percent,
        IngestionProgressCounters counters, CancellationToken ct = default);
}
```

- Ajout sur l'entité `Document` : `IngestionProgress? IngestionProgress { get; private set; }`
  + méthode `UpdateIngestionProgress(IngestionProgress progress)`. La progression est **remise à
  `null`** par `MarkAsCompleted()` (les stats finales suffisent) mais **conservée** par
  `MarkAsFailed()` (pour afficher l'étape d'échec). Alternative retenue : conserver la dernière
  progression aussi en `Completed` — **non** : on privilégie des stats finales propres.

### 6.2 Application

- **Instrumentation** de `IngestDocumentHandler`, du handler vidéo
  (`ProcessVideoTranscriptionJobCommand`) et de `IngestionService` :
  appels `EnterStageAsync` aux transitions, `ReportAsync` pendant les étapes fines.
- **Embedding en sous-lots** : `IngestionService.IngestAsync` / `IngestTextAsync` /
  `IngestFromSegmentsAsync` découpent l'appel `EmbedBatchAsync` en sous-lots de taille
  configurable (`IngestionOptions.EmbeddingBatchSize`, défaut ex. 16) et appellent `ReportAsync`
  après chaque sous-lot (`batchIndex/batchCount`, `chunksDone/chunksTotal`). Le stockage
  `UpsertBatchAsync` reste un batch unique (rapide).
- **Whisper** : le service de transcription expose un callback de progression par segment ;
  `IngestVideoOrAudioAsync` le relaie en `ReportAsync` (`segmentsDone/segmentsTotal`, `percent`).
  > Point à valider en implémentation : l'API exacte de callback exposée par `Whisper.net` et la
  > façon d'obtenir la durée totale en amont (sinon `%` basé sur `segment.End` rapporté à la durée
  > connue via FFprobe/métadonnées). Repli : progression par nombre de segments sans `%` absolu.

### 6.3 Infrastructure / Web.Api

- Implémentation `IngestionProgressReporter` (Infrastructure) :
  - `EnterStageAsync` → charge le document, `UpdateIngestionProgress`, persiste, notifie.
  - `ReportAsync` → notifie **toujours** (SignalR) ; persiste **seulement** si le dernier
    checkpoint persisté date de plus de `IngestionOptions.ProgressPersistThrottle` (défaut ~1,5 s)
    **ou** si l'étape a changé. Throttling par document en mémoire process (worker mono-instance).
  - Persistance via un **scope/DbContext dédié** au reporter (le pipeline tourne déjà dans un scope
    worker) — attention à ne pas entrer en conflit avec le tracking EF du document en cours.
    Choix : le reporter utilise `IServiceScopeFactory` et une mise à jour ciblée
    (`UPDATE documents SET ingestion_progress = ... WHERE id = ...`) pour éviter les collisions de
    tracking. À confirmer en implémentation.
- **Notifier** : étendre l'abstraction temps réel avec `NotifyProgressAsync(Guid documentId,
  IngestionProgress progress)`. `SignalRIngestionNotifier` pousse **`documentProgressChanged`**
  `{ documentId, stage, percent, counters }`. `NullIngestionNotifier` (Console) → no-op.
- **DTO** : enrichir `DocumentResponse` avec `IngestionProgressResponse? IngestionProgress`
  (stage string, percent nullable, counters). `GET /api/documents/{id}` le renvoie déjà (un seul
  appel au chargement de la page ; le polling de repli existant le récupère aussi).
- **Schéma** : ajout colonne `ingestion_progress JSONB NULL` sur `documents` dans `init.sql`
  + script `scripts/migrate-ingestion-progress.sql` (pas d'EF migrations dans ce projet).
  Mapping EF Core : conversion valeur JSONB ↔ `IngestionProgress` (value converter, à l'image des
  colonnes vidéo `start_time_seconds`).

### 6.4 Options

`IngestionOptions` (existant) enrichi :

```csharp
public int EmbeddingBatchSize { get; init; } = 16;        // sous-lots d'embedding
public double ProgressPersistThrottleSeconds { get; init; } = 1.5; // throttle persistance
```

---

## 7. Architecture front

- **Route** : `/documents/:id` → `DocumentDetailPage`.
- **Types** (`types/index.ts`) : `IngestionStage` (union de string), `IngestionProgress`
  (`stage`, `percent`, `counters`), `DocumentProgressChangedEvent`
  (`documentId`, `stage`, `percent`, `counters`) ; `DocumentResponse.ingestionProgress?`.
- **Realtime** : `IngestionNotificationsContext` expose une souscription dédiée
  `subscribeProgress(documentId, listener)` (filtrée par id) branchée sur l'event
  `documentProgressChanged`. **Local à la page de détail** : la liste ne s'abonne pas aux ticks
  fins (pas de re-render haute fréquence côté liste). Repli : si le hub est indisponible, la page
  re-`GET /api/documents/{id}` périodiquement (réutilise le mécanisme existant).
- **`DocumentsPage`** :
  - Le **badge de statut** et le **nom de fichier** deviennent cliquables → `navigate('/documents/'+id)`.
  - La sélection multiple passe **uniquement par la checkbox** (on retire le clic-ligne-pour-
    sélectionner qui entrerait en conflit avec la navigation).
- **`DocumentDetailPage`** :
  - Chargement initial : `GET /api/documents/{id}` (doc + progression courante).
  - Abonnements : `subscribe` (statut grossier) + `subscribeProgress` (progression fine).
  - **En-tête** : nom, badge statut live, type/taille, dates création/màj.
  - **Stepper vertical** des étapes pertinentes (section 4) : état ●/○/spinner/✓/✗ par étape,
    barre `%` + compteurs sur l'étape active à pourcentage
    (« Vectorisation — lot 3/8 · 42/120 chunks », « Transcription — segment 128/540 · 71 % »).
  - **Métadonnées** : titre, langue détectée, stratégie de chunking, nb de chunks final.
  - **Vidéo terminée** : aperçu transcription via `GET /api/video/{id}/transcription` (existant).
  - **Échec** : étape d'échec (dernière `stage` persistée) mise en évidence + `errorMessage`.
  - **Terminé** : bouton « Interroger dans le Chat → » (réutilise la navigation par `state` existante).

---

## 8. Contrat temps réel (récapitulatif)

| Event SignalR | Charge utile | Émis quand | Consommé par |
|---|---|---|---|
| `documentStatusChanged` (existant) | `{ documentId, status, errorMessage }` | Transition de statut grossier | Liste (badges), toasts |
| `documentProgressChanged` (nouveau) | `{ documentId, stage, percent, counters }` | Entrée d'étape + ticks fins | Page de détail uniquement |

---

## 9. Gestion des erreurs & cas limites

- **Échec d'ingestion** : `MarkAsFailed` conserve la dernière progression → l'UI affiche l'étape
  fautive. Message d'erreur reste **générique** côté client (le détail/stack va aux logs serveur,
  politique existante conservée).
- **Notification best-effort** : une erreur de transport SignalR ne doit jamais faire échouer
  l'ingestion (même contrat que `PersistAndNotifyAsync` existant).
- **Document supprimé** pendant qu'on regarde sa page : le `GET` renvoie 404 → l'UI affiche un
  état « document introuvable » et propose de revenir à la liste.
- **ID inconnu / invalide** dans l'URL : idem, état « introuvable ».
- **Reprise après redémarrage** : l'étape persistée reflète le dernier point atteint ; le worker
  reprend le message outbox non traité et réémet des events depuis le début du pipeline — l'UI se
  resynchronise au premier tick.
- **Étape à `%` indéterminé** (ex. Whisper sans durée totale connue) : `percent = null`, l'UI
  affiche un indéterminé (spinner + compteur de segments) plutôt qu'une barre.

---

## 10. Stratégie de tests

**Back-end (xUnit + FluentAssertions) :**
- `IngestionProgressReporter` : throttling (persiste au changement d'étape et après le délai, pas
  entre-temps) ; notifie à chaque `ReportAsync`.
- Séquence d'étapes émise par `IngestDocumentHandler` et le handler vidéo (reporter mocké) :
  ordre attendu selon le type de document.
- Embedding en sous-lots : `batchCount` correct, `ReportAsync` appelé une fois par sous-lot,
  `chunksDone` monotone croissant jusqu'à `chunksTotal`.
- Mapping du callback Whisper → compteurs/percent.
- `MarkAsCompleted` remet la progression à null ; `MarkAsFailed` la conserve.

**Front :**
- Rendu du stepper selon le type (PDF vs vidéo vs audio) et l'état (en cours / terminé / échec).
- Mise à jour du `%`/compteurs sur réception d'un `documentProgressChanged`.

---

## 11. Hors périmètre (YAGNI)

- Progression `%` **par page** de l'extraction texte PDF (traitée comme atomique).
- Progression de l'étape `Storing` (batch unique rapide → atomique).
- Historique/journal détaillé horodaté de chaque étape (on ne garde que l'état courant).
- Annulation d'une ingestion en cours depuis la page.
- Segmentation SignalR par utilisateur (pas d'authentification à ce stade — cohérent avec
  l'existant).

---

## 12. Découpage indicatif de l'implémentation

1. Domain : `IngestionStage`, `IngestionProgress`, `IIngestionProgressReporter`, évolution de
   l'entité `Document` + `IIngestionNotifier.NotifyProgressAsync`.
2. Schéma : `init.sql` + `migrate-ingestion-progress.sql` + mapping EF Core.
3. Infrastructure : `IngestionProgressReporter` (persistance throttlée), `SignalRIngestionNotifier`
   (event `documentProgressChanged`), `NullIngestionNotifier`.
4. Application : instrumentation des handlers/`IngestionService`, embedding en sous-lots, relais du
   callback Whisper, `IngestionOptions`.
5. Web.Api : enrichissement `DocumentResponse` + `GET /api/documents/{id}`.
6. Front : types, `subscribeProgress` dans le contexte, route + `DocumentDetailPage`, liens
   cliquables dans `DocumentsPage`, stepper + barres + compteurs, styles.
7. Tests back-end + front.
