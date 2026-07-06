# Détection de « nouvelle version » à l'upload de document

**Date** : 2026-07-05
**Statut** : Validé, en attente de plan d'implémentation
**Périmètre** : `Step 3`, extension de la fonctionnalité de détection de doublon déjà livrée sur la branche `feature/detection-doublon-upload` (spec initiale : `docs/superpowers/specs/2026-07-05-detection-doublon-upload-design.md`).

## Contexte

La détection de doublon existante compare **nom de fichier ET hash de contenu** (critère AND strict, `UploadDocumentHandler` + `IDocumentRepository.GetByFileNameAndHashAsync`). Si un utilisateur réimporte un fichier de même nom mais dont le **contenu a changé** (mise à jour d'un document source), aucune vérification ne se déclenche : un second document est créé silencieusement, avec le même `FileName` affiché, un `Id` différent — ambigu pour l'utilisateur et pour les citations générées par le RAG.

Ce point a été identifié en revue de code (`DocumentRepository.GetByFileNameAndHashAsync`) : le critère AND ne couvre que le doublon exact, pas le cas « nouvelle version » qui est pourtant le scénario le plus probable de re-upload volontaire.

## Objectif

Étendre la détection existante pour couvrir le cas « même nom, contenu différent » : proposer une popup de remplacement, avec un message **distinct** de celui du doublon exact, pour ne pas laisser croire à l'utilisateur qu'un fichier strictement identique existe déjà.

## Critères de correspondance

Pour un même utilisateur (`UserId`), si un document existant porte le même `FileName` que le fichier uploadé :

- **`ExactDuplicate`** : `ContentHash` strictement identique → comportement inchangé par rapport à l'existant (message « document identique existe déjà »).
- **`SameNameDifferentContent`** (nouveau) : `ContentHash` différent → nouveau message (« une version différente de ce document existe déjà »).

Dans les deux cas, le flux de confirmation/remplacement (popup, bouton « Confirmer », suppression + recréation atomique) est **identique** ; seul le texte affiché change.

**Homonymes multiples préexistants** (cas hérité, avant l'existence de cette vérification, ou résultant d'une course entre deux uploads concurrents) : la comparaison se fait toujours contre le document le **plus récent** portant ce nom (`ORDER BY CreatedAt DESC`, premier résultat). Les autres homonymes plus anciens ne sont pas pris en compte par cette vérification et restent inchangés.

## Architecture

### 1. Repository — simplification de la requête de recherche

`IDocumentRepository.GetByFileNameAndHashAsync(userId, fileName, contentHash)` est **remplacée** par `GetLatestByFileNameAsync(userId, fileName, ct)` :

```csharp
Task<Document?> GetLatestByFileNameAsync(string userId, string fileName, CancellationToken ct = default);
```

Implémentation EF Core : `Where(d => d.UserId == userId && d.FileName == fileName).OrderByDescending(d => d.CreatedAt).FirstOrDefaultAsync(ct)`.

Une seule requête couvre les deux cas (exact et nouvelle version) : c'est le handler qui compare ensuite les hashs pour déterminer le type de correspondance. Pas de requête supplémentaire par hash exact.

**Pas de migration SQL nécessaire** : l'index composite existant `(user_id, file_name, content_hash)` (`DocumentConfiguration.cs`) sert de préfixe pour filtrer par `(user_id, file_name)` ; le tri par `CreatedAt` porte sur un ensemble de résultats toujours minuscule (un seul utilisateur, un seul nom de fichier).

### 2. Domain — `DocumentMatchType`

Nouvel enum (namespace `AIExperience.Rag.Domain.Enums`) :

```csharp
/// <summary>Type de correspondance détecté entre un fichier uploadé et un document existant de même nom.</summary>
public enum DocumentMatchType
{
    /// <summary>Nom de fichier et hash de contenu strictement identiques.</summary>
    ExactDuplicate,

    /// <summary>Même nom de fichier, contenu différent — probable nouvelle version du document.</summary>
    SameNameDifferentContent
}
```

### 3. Application — `DuplicateDocumentException` étendue

Pas de nouvelle classe d'exception : ajout d'une propriété `MatchType` (`DocumentMatchType`) au constructeur existant de `DuplicateDocumentException`. Évite de dupliquer la gestion déjà en place dans `DocumentsController` (catch unique) et dans les tests existants.

`UploadDocumentHandler.Handle` :

```
latest = await documentRepository.GetLatestByFileNameAsync(request.UserId, request.FileName, ct)

si latest != null et latest.Id != request.ReplaceDocumentId :
    matchType = (latest.ContentHash == contentHash) ? ExactDuplicate : SameNameDifferentContent
    throw new DuplicateDocumentException(latest.Id, latest.FileName, latest.CreatedAt, matchType)

si latest != null :
    // remplacement confirmé, inchangé : suppression + création en une seule transaction
    await documentRepository.DeleteAsync(latest.Id, ct)

// création normale, inchangée
```

### 4. Web.Api — DTOs et endpoint de pré-vérification

- `DuplicateDocumentResponse` (409) : ajout du champ `MatchType` (string, sérialisation par défaut du enum .NET — ex. `"ExactDuplicate"` / `"SameNameDifferentContent"`).
- `CheckDuplicateResponse` (`POST /api/documents/check-duplicate`) : ajout du champ `MatchType` (nullable, absent/`null` si `IsDuplicate` est `false`). Le contrôleur appelle `GetLatestByFileNameAsync` puis compare les hashs de la même façon que le handler (même logique dupliquée une seule fois, pas de service partagé supplémentaire — cohérent avec le fait que `check-duplicate` est un contrôle informatif, non autoritaire).

### 5. Front-end

- `types/index.ts` : `export type DocumentMatchType = 'ExactDuplicate' | 'SameNameDifferentContent';`, ajouté à `ExistingDocumentInfo` et propagé par `CheckDuplicateResponse`.
- `DocumentsPage.tsx` : le state `pendingDuplicate` porte désormais aussi `matchType`. La popup affiche :
  - `ExactDuplicate` → texte actuel inchangé (« Un document nommé « {fileName} » a déjà été importé le {date}. [...] sera supprimé et remplacé »).
  - `SameNameDifferentContent` → nouveau texte (« Une version différente de « {fileName} » a déjà été importée le {date}. Le contenu a changé. Remplacer par cette nouvelle version ? »).
  - Mêmes boutons, même mécanique d'appel (`upload(file, strategy, existing.id)`).

## Gestion des erreurs

- Comportement inchangé pour les cas déjà couverts par la spec initiale (échec `crypto.subtle`, `ReplaceDocumentId` obsolète → création normale).
- Aucun nouveau cas d'erreur introduit : cette extension ne fait que raffiner le message affiché pour un scénario auparavant silencieux.

## Tests prévus

Extension de `UploadDocumentHandlerTests` (fakes en mémoire existants, pas de bibliothèque de mocking) :

- Même nom + même hash, sans confirmation → `DuplicateDocumentException` avec `MatchType.ExactDuplicate` (test existant adapté à la nouvelle méthode repository).
- Même nom + hash différent, sans confirmation → `DuplicateDocumentException` avec `MatchType.SameNameDifferentContent` (nouveau).
- Confirmation (`ReplaceDocumentId` correct) dans les deux cas → remplacement atomique identique au comportement actuel (test existant réutilisé, adapté).
- Homonymes multiples préexistants (deux documents même `FileName`, hashs différents, dates de création différentes) → la comparaison se fait contre le plus récent uniquement (nouveau test).

Pas de test automatisé pour `GetLatestByFileNameAsync` (requête EF Core) ni pour le contrôleur — cohérent avec l'absence de précédent de test d'intégration base de données dans ce projet (vérification manuelle via Scalar/navigateur, comme pour la spec initiale).

## Hors périmètre

- Pas de contrainte SQL `UNIQUE` sur `(user_id, file_name)` — le garde-fou reste applicatif, cohérent avec la spec initiale.
- Pas de gestion d'ambiguïté explicite pour les homonymes multiples préexistants (silencieusement résolu par « le plus récent » ; pas d'UX dédiée pour signaler l'existence d'autres homonymes plus anciens).
- Pas de changement du critère de portée (`UserId`) : la comparaison reste strictement par utilisateur, comme pour le doublon exact.
