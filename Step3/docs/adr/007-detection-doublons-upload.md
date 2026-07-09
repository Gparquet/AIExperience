# ADR-007 — Détection de doublons à l'upload (hash côté client + pré-vérification + garde serveur 409)

**Date :** 2026-06-22  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Rien n'empêchait, jusqu'ici, d'uploader deux fois le **même fichier**. Conséquences : chunks et
embeddings dupliqués en base, réponses RAG polluées par des citations redondantes, coût
d'ingestion (embedding) payé pour rien, et liste de documents encombrée.

Deux situations distinctes doivent être traitées différemment :

- **Doublon exact** : même nom **et** même contenu → l'utilisateur re-téléverse un fichier déjà
  présent, probablement par erreur.
- **Même nom, contenu différent** : nouvelle version d'un document → intention légitime
  (remplacement), à ne pas confondre avec un doublon.

Il faut aussi **éviter de transférer inutilement** un gros fichier (vidéo !) sur le réseau juste
pour se voir répondre « doublon ».

---

## Décision

Détecter les doublons via un **hash de contenu**, avec une **double barrière** :

1. **Pré-vérification légère** avant l'envoi : le navigateur calcule le hash du fichier et
   interroge `POST /api/documents/check-duplicate` (nom + hash), **sans** envoyer le fichier.
2. **Garde d'autorité** à l'upload réel : le serveur reste seul juge et renvoie `409 Conflict`
   (via `DuplicateDocumentException`) si un doublon est confirmé au moment de la création.

La pré-vérification est un **confort** (économiser un gros upload) ; l'autorité finale est
**toujours** la garde serveur, robuste aux conditions de course.

---

## Architecture de la solution

### Empreinte de contenu — `ContentHash`

L'entité `Document` porte un `ContentHash` (calculé sur le contenu du fichier). C'est lui, et
non le nom, qui distingue un contenu identique d'un contenu différent.

### Étape 1 — pré-vérification côté client (sans upload)

```typescript
// Hash calculé dans le navigateur ; on n'envoie que { fileName, contentHash }.
checkDuplicate: (fileName: string, contentHash: string) =>
  request<CheckDuplicateResponse>('/api/documents/check-duplicate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ fileName, contentHash }),
  }),
```

Côté serveur, `CheckDuplicate` cherche le document le plus récent portant le même nom pour
l'utilisateur, puis **compare les hashes** pour qualifier la correspondance :

```csharp
var existing = await documentRepository.GetLatestByFileNameAsync(DefaultUserId, request.FileName);
if (existing is null) return Ok(new CheckDuplicateResponse(false, null, null));

var matchType = existing.ContentHash == request.ContentHash
    ? DocumentMatchType.ExactDuplicate               // même nom + même contenu
    : DocumentMatchType.SameNameDifferentContent;    // même nom, contenu différent
```

Le front peut ainsi, **avant tout transfert**, prévenir l'utilisateur (« ce fichier existe
déjà » vs « un document de même nom existe, voulez-vous le remplacer ? »).

### Étape 2 — garde d'autorité à l'upload

`check-duplicate` est explicitement **« purement informatif — l'autorité finale reste
`Upload` »**. Au véritable upload, si un doublon est confirmé, le handler lève
`DuplicateDocumentException`, que le contrôleur traduit en `409 Conflict` avec le type de
correspondance et les infos du document existant :

```csharp
catch (DuplicateDocumentException ex)
{
    // Document non créé → on supprime le fichier de travail déjà écrit (cf. ADR-008).
    if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);

    return Conflict(new DuplicateDocumentResponse(
        new ExistingDocumentInfo(ex.ExistingDocumentId, ex.ExistingFileName, ex.ExistingCreatedAt),
        ex.MatchType.ToString()));
}
```

### Remplacement volontaire — `replaceDocumentId`

Le cas « même nom, contenu différent » (nouvelle version) est géré par le paramètre
`replaceDocumentId` de l'upload : l'utilisateur choisit sciemment de remplacer le document
existant, ce qui n'est **pas** bloqué comme un doublon.

### Sérialisation des enums

Les types de correspondance (`DocumentMatchType`) sont convertis en `string` avant sérialisation
(pas de `JsonStringEnumConverter` global), pour éviter un entier brut illisible côté front.

---

## Conséquences

### Positives

- **Pas de gros upload inutile** : le doublon exact est détecté avant le transfert du fichier.
- **Intégrité du corpus** : plus de chunks/embeddings dupliqués polluant le RAG.
- **Distinction claire** doublon exact vs nouvelle version, avec un chemin de remplacement dédié.
- **Robustesse** : la garde serveur `409` reste souveraine même si la pré-vérification est
  contournée ou victime d'une condition de course.

### Négatives / points d'attention

- **Fenêtre de course** entre `check-duplicate` et l'upload réel (un autre upload peut passer
  entre les deux) — assumée, car la garde serveur rattrape le cas.
- **Portée de l'unicité** : basée sur `(UserId, FileName)` + hash ; sans authentification réelle
  (`UserId` hardcodé), la détection est globale — à réévaluer avec l'auth.
- Le hash doit être **calculé deux fois** (client pour la pré-vérif, serveur pour l'autorité) —
  léger surcoût, mais nécessaire pour ne pas faire confiance à un hash fourni par le client.

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| Détection sur le seul **nom de fichier** | Faux positifs (versions différentes) et faux négatifs (même contenu renommé). |
| Détection **serveur uniquement** (pas de pré-vérif) | Oblige à uploader tout le fichier — coûteux pour une vidéo — avant de savoir que c'est un doublon. |
| Faire **confiance au hash client** comme autorité | Un client peut mentir ; la garde serveur doit rester souveraine. |
| Contrainte d'unicité SQL seule | Renvoie une erreur base peu exploitable et ne distingue pas doublon exact / nouvelle version. |
