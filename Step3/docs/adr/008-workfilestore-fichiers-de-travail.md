# ADR-008 — Stockage des fichiers de travail durables (WorkFileStore) survivant à la requête HTTP

**Date :** 2026-07-08  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

L'ingestion étant devenue **asynchrone** ([ADR-002](002-ingestion-asynchrone-outbox.md)), le
traitement du fichier (extraction, chunking, embedding, ou extraction audio + transcription)
n'a plus lieu pendant la requête HTTP mais **plus tard**, dans le worker en arrière-plan.

Or le fichier uploadé arrive sous forme d'`IFormFile`, dont le contenu est adossé à un buffer
mémoire/disque **lié au cycle de vie de la requête**. Dès que l'API renvoie `202 Accepted`,
ASP.NET Core est libre de libérer ce buffer. Le worker, qui lira le fichier quelques secondes ou
minutes plus tard, ne peut donc **pas** compter dessus.

En Step 2, un helper `TempUploadedFile` écrivait le fichier dans un temporaire **le temps de la
requête** — modèle incompatible avec un traitement différé. Il faut un fichier qui **survive à
la réponse HTTP**.

---

## Décision

Introduire **`WorkFileStore`** : le fichier uploadé est écrit dans un **répertoire de travail
durable**, sous un nom dérivé de l'identifiant du document, **avant** de mettre le job en file.
Ce fichier n'est **pas** temporaire au sens « durée de la requête » : il est lu plus tard par le
worker, et c'est **ce même worker qui le supprime** une fois le traitement terminé.

Le remplacement de `TempUploadedFile` par `WorkFileStore` acte le changement de propriétaire du
cycle de vie du fichier : de « la requête » vers « le job d'ingestion ».

---

## Architecture de la solution

### Écriture — nommage déterministe par `documentId`

```csharp
/// <summary>
/// Écrit un fichier uploadé dans le répertoire de travail durable de l'ingestion, sous un nom
/// basé sur l'identifiant du document. Contrairement à l'ancien fichier temporaire lié à la
/// durée de la requête, ce fichier doit survivre à la réponse HTTP : il n'est lu que plus tard,
/// par le worker en arrière-plan, et c'est ce même worker qui le supprime une fois terminé.
/// </summary>
public static class WorkFileStore
{
    public static async Task<string> SaveAsync(IFormFile file, Guid documentId, string workDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(workDirectory);
        var path = Path.Combine(workDirectory, $"{documentId}{Path.GetExtension(file.FileName)}");

        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);
        return path;
    }
}
```

Le nom `{documentId}{extension}` est **déterministe et unique** :

- Pas de collision entre deux uploads (chaque document a son `Guid`).
- Le chemin est reconstituable à partir du document seul, et l'extension d'origine est
  préservée (indispensable pour que l'extracteur/FFmpeg reconnaisse le format).

Le répertoire est configurable via `IngestionOptions.WorkDirectory` (Options Pattern).

### Flux complet

```
1. Upload → WorkFileStore.SaveAsync(file, documentId, workDir)  → chemin absolu
2. UploadDocumentCommand { ..., FilePath = chemin }             → Document + OutboxMessage
3. 202 Accepted (la requête se termine ; le fichier, lui, reste sur disque)
        ...
4. Worker → IngestDocumentCommand → lit FilePath → extraction/chunking/embedding
5. Worker → supprime le fichier de travail une fois le traitement terminé
```

Le chemin est transporté jusqu'au worker via le champ `FilePath` de la commande, persisté avec
le document (`FileReference`).

### Nettoyage sur chemin d'échec précoce

Si la commande d'upload échoue **avant** la mise en file — typiquement une
`DuplicateDocumentException` ([ADR-007](007-detection-doublons-upload.md)) — le document n'est
jamais créé, donc aucun worker ne viendra nettoyer. Le contrôleur supprime alors immédiatement
le fichier de travail fraîchement écrit :

```csharp
catch (DuplicateDocumentException ex)
{
    // Le document n'a pas été créé : le fichier de travail n'a plus de raison d'exister.
    if (System.IO.File.Exists(filePath))
        System.IO.File.Delete(filePath);
    return Conflict(...);
}
```

---

## Conséquences

### Positives

- Le fichier est **disponible pour le worker** quel que soit le délai avant traitement.
- Nommage déterministe = pas de table de correspondance nom↔fichier à maintenir.
- Extension préservée = les extracteurs et FFmpeg résolvent correctement le format.
- Responsabilité de suppression **clairement attribuée** au worker (nominal) ou au contrôleur
  (échec précoce), évitant les fichiers orphelins dans les cas prévus.

### Négatives / points d'attention

- **Fuite possible en cas de crash** entre l'écriture du fichier et sa suppression par le
  worker : un redémarrage laisse le fichier sur disque. Un balayage périodique du
  `WorkDirectory` (supprimer les fichiers sans document actif) serait un complément utile.
- Le `WorkDirectory` doit être **inscriptible** et suffisamment dimensionné (les vidéos sont
  lourdes) — contrainte de déploiement à documenter.
- En déploiement **multi-instances**, le fichier écrit par une instance doit être accessible à
  l'instance qui exécute le worker → nécessiterait un stockage partagé (volume, blob). Non
  pertinent en mono-instance actuel, mais à noter (cohérent avec la limite déjà signalée en
  [ADR-002](002-ingestion-asynchrone-outbox.md)).

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| `TempUploadedFile` (fichier lié à la requête) | Le fichier disparaît à la fin de la requête, incompatible avec le traitement différé. |
| Garder le fichier en mémoire jusqu'au traitement | Risque de saturation mémoire avec des vidéos volumineuses ; perdu au redémarrage. |
| Stocker le contenu binaire dans PostgreSQL (`bytea`) | Gonfle la base, transactions lourdes, mauvais usage d'une base relationnelle pour du blob. |
| Nom de fichier aléatoire + table de mapping | Complexité inutile : le `documentId` est déjà un identifiant unique et reconstituable. |
