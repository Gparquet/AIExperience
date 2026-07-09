# ADR-002 — Ingestion et transcription asynchrones via pattern Outbox + BackgroundService

**Date :** 2026-07-08  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Jusqu'à Step 2, l'ingestion d'un document se déroulait **dans le fil de la requête HTTP** :
`POST /api/documents` extrayait le texte, découpait en chunks, calculait les embeddings
et écrivait dans pgvector, puis retournait `200 OK` avec un statut forcé à `Completed`.

Ce modèle synchrone pose plusieurs problèmes en Step 3, où les traitements deviennent lourds :

- **La transcription vidéo** (FFmpeg + Whisper) peut durer plusieurs dizaines de secondes,
  voire minutes. Une requête HTTP maintenue ouverte aussi longtemps est fragile (timeouts
  proxy/navigateur, connexion coupée = travail perdu).
- **L'embedding par batch** d'un gros PDF sollicite le provider AI pendant longtemps.
- Le statut `Completed` était **menti** : renvoyé avant même la fin réelle du traitement,
  il ne reflétait aucune vérité observable.
- Aucune **reprise** possible après un redémarrage de l'API en plein traitement.

L'objectif est de **découpler la réception du fichier de son traitement** : répondre
immédiatement à l'upload, puis traiter en arrière-plan de façon fiable et reprenable.

---

## Décision

Adopter le **pattern Outbox** couplé à un **`BackgroundService`** hébergé dans le même
processus que l'API.

1. À l'upload, on persiste le document (statut `Pending`) **et** un message dans la table
   `outbox_messages`, dans la **même transaction**. L'API répond aussitôt `202 Accepted`.
2. Un worker (`IngestionWorker : BackgroundService`) **sonde** la table outbox, et pour
   chaque message non traité, envoie la commande MediatR correspondante
   (`IngestDocumentCommand` ou `ProcessVideoTranscriptionJobCommand`).
3. Le worker est **réveillé immédiatement** par un signal en mémoire (`IngestionSignal`)
   quand un job vient d'être mis en file, sans attendre le prochain tour de sonde.

### Pourquoi Outbox plutôt qu'une simple file en mémoire (`Channel<T>`) ?

| Critère | Outbox (table SQL) | `Channel<T>` en mémoire |
|---------|--------------------|-------------------------|
| Survie au redémarrage | ✅ Les messages non traités sont relus | ❌ File perdue au restart |
| Atomicité avec l'écriture métier | ✅ Même transaction que le `Document` | ❌ Deux systèmes désynchronisables |
| Traçabilité / rejeu | ✅ `RetryCount`, `ProcessedAt`, `Error` en base | ❌ Aucune trace |
| Complexité | Modérée (une table, un repo) | Faible |

Le **point décisif** est la fiabilité : un document accepté (`202`) **doit** finir par être
traité, même si l'API redémarre entre-temps. Une file en mémoire ne le garantit pas.

---

## Architecture de la solution

### Écriture transactionnelle à l'upload

`UploadDocumentHandler` crée l'entité `Document` (statut `Pending`) et insère un
`OutboxMessage` (`EventType = DocumentIngestionRequested`, payload = `{ DocumentId }`)
**dans la même unité de travail**. Soit les deux sont commités, soit aucun.

### Le worker — `IngestionWorker`

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
    while (!stoppingToken.IsCancellationRequested)
    {
        try { await ProcessPendingMessagesAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        catch (Exception ex) { logger.LogError(ex, "Erreur inattendue lors du sondage..."); }

        // Réveil : soit signal immédiat (nouveau job), soit intervalle (filet de sécurité).
        await signal.WaitAsync(pollInterval, stoppingToken);
    }
}
```

Points de conception clés :

- **Scope DI par message.** Chaque message est traité dans son propre `AsyncScope`, donc
  sa propre instance de `AppDbContext`. Aucun contexte EF Core n'est partagé entre deux
  messages ni avec la boucle de sondage.
- **Traitement séquentiel.** Un seul job à la fois : Whisper/FFmpeg sont des ressources
  partagées dont la réentrance concurrente n'est pas garantie.
- **Reprise gratuite.** Un message reste `ProcessedAt = null` tant qu'il n'est pas traité.
  Un redémarrage le retrouve au tour de sonde suivant — la sonde **est** le mécanisme de
  reprise, sans code dédié.
- **Retries bornés.** En cas d'échec de *délivrance* du message (payload corrompu, document
  introuvable, base momentanément indisponible), `MarkFailedAttempt` incrémente `RetryCount` ;
  au-delà de `MaxRetryAttempts`, le message est marqué traité pour ne pas boucler indéfiniment.
  Les échecs *métier* (extraction impossible…) sont, eux, gérés par la commande elle-même qui
  marque le document `Failed`.

### Signal de réveil — `IngestionSignal`

Objet singleton exposant `Pulse()` (appelé par le contrôleur après l'upload) et
`WaitAsync(timeout, ct)` (attendu par le worker). Il transforme la sonde en **push** dans le
cas nominal, tout en gardant la sonde périodique comme filet de sécurité.

### Cycle de vie du statut

```
Pending ──(worker démarre)──▶ Processing ──(succès)──▶ Completed
                                   └────────(échec)────▶ Failed (+ ErrorMessage)
```

Le statut est désormais **honnête** : il n'atteint `Completed` qu'après écriture effective
des chunks. Le front est notifié de chaque transition (voir [ADR-003](003-notification-signalr.md)).

---

## Conséquences

### Positives

- L'upload répond en quelques millisecondes (`202`), quel que soit le poids du traitement.
- Un document accepté finit **toujours** par être traité, même après un redémarrage.
- Le statut reflète la réalité, ce qui rend possible une UX d'attente fiable.
- La table outbox offre une traçabilité (tentatives, erreurs) exploitable en debug.
- Le worker vivant dans le même process reste simple à déployer (aucune infra externe).

### Négatives / points d'attention

- **Un seul process = pas de scale-out horizontal** de l'ingestion. Plusieurs instances de
  l'API sonderaient la même table sans verrou et pourraient traiter un message en double.
  Acceptable en mono-instance ; à revoir (verrou `FOR UPDATE SKIP LOCKED` ou broker dédié) le
  jour d'un déploiement multi-instances.
- **Traitement séquentiel** = pas de parallélisme entre jobs. Suffisant pour la charge
  actuelle, mais un gros backlog s'écoule lentement.
- Le fichier uploadé doit **survivre à la requête** puisqu'il est lu plus tard par le worker
  (voir [ADR-008](008-workfilestore-fichiers-de-travail.md)).

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| Traitement synchrone dans la requête (statu quo Step 2) | Timeouts, travail perdu si connexion coupée, statut menti. |
| `Channel<T>` / `BackgroundQueue` en mémoire | Aucune survie au redémarrage, pas d'atomicité avec l'écriture métier. |
| `Task.Run(...)` fire-and-forget dans le contrôleur | Non observable, non reprenable, meurt avec le process sans trace. |
| Broker externe (RabbitMQ, Azure Service Bus) | Infrastructure supplémentaire injustifiée pour une appli mono-instance locale. |
| Hangfire / Quartz.NET | Dépendance et tables propres à un ordonnanceur, surdimensionné pour un unique type de job. |
