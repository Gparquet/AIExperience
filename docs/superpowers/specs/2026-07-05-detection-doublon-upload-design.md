# Détection de doublon à l'upload de document

**Date** : 2026-07-05
**Statut** : Validé, en attente de plan d'implémentation
**Périmètre** : `Step 3` (confirmé par l'utilisateur — c'est là que vit le projet `AIExperience.Tests`, réutilisé pour les tests de cette fonctionnalité). L'architecture Document/Upload de Step 3 est identique à celle de Step 1 (vérifié par diff), donc ce spec s'applique sans changement de conception.

## Contexte

Le flux d'upload de documents (`POST /api/documents`) ne fait actuellement **aucune vérification de doublon**. Un utilisateur peut réimporter le même fichier plusieurs fois, créant des documents et des chunks redondants en base et dans l'index vectoriel.

Exploration du code existant (voir historique de conversation) :
- Aucun champ hash de contenu sur l'entité `Document`, aucune contrainte SQL d'unicité sur `file_name`.
- `UploadDocumentHandler` crée systématiquement un nouveau `Document` sans vérification.
- `DeleteDocumentCommand`/`DeleteDocumentHandler` existent déjà et gèrent la suppression physique (cascade SQL vers `document_chunks`, suppression du fichier référencé).
- Front-end (`DocumentsPage.tsx`) : upload direct sans pré-vérification ; le dossier `components/` est vide (pas de composant Modal réutilisable — le seul modal existant est inline dans `DocumentsPage.tsx`).

## Objectif

Empêcher la création silencieuse de doublons. Si un utilisateur tente de réimporter un document déjà présent (même nom de fichier **et** même contenu), afficher une popup de confirmation : soit annuler, soit remplacer l'ancien document (suppression + réimport complet).

## Critères de doublon

Un document est considéré comme doublon d'un document existant si, **pour le même utilisateur** :
- `FileName` est strictement identique, **ET**
- `ContentHash` (SHA-256 du contenu du fichier) est strictement identique.

Les deux critères doivent correspondre simultanément (un fichier renommé avec un contenu identique, ou un fichier différent portant le même nom, ne sont **pas** considérés comme doublons).

Le périmètre de comparaison est **par utilisateur** (`UserId`), cohérent avec le reste de l'application (`GetByUserIdAsync`), même si l'authentification reste actuellement hard-codée en développement.

## Architecture

### 1. Backend — Entité et persistance

- **`Document`** (`AIExperience.Rag.Domain/Entities/Document.cs`) : nouvelle propriété `ContentHash` (string, 64 caractères hex — SHA-256), avec setter privé. Le paramètre est ajouté à la méthode factory `Create(fileName, contentType, fileSizeBytes, userId, metadata, chunkingStrategy, contentHash)`.
- **`init.sql`** : ajout de la colonne `content_hash VARCHAR(64) NOT NULL` sur la table `documents`, et d'un index non-unique `(user_id, file_name, content_hash)` pour accélérer la recherche de doublon. Pas de contrainte `UNIQUE` — la garde-fou anti-doublon reste gérée au niveau applicatif, pas transactionnel.
- **`DocumentConfiguration.cs`** (EF Core) : mapping de la nouvelle propriété.

**Note de déploiement** : `init.sql` ne s'exécute qu'au premier démarrage d'un volume Postgres vide. La base de développement existante ne recevra pas automatiquement la colonne. Deux options à appliquer manuellement selon le choix de l'utilisateur au moment de l'implémentation :
1. Recréer le volume (`docker-compose down -v && docker-compose up -d`) — perte des documents existants.
2. Exécuter manuellement : `ALTER TABLE documents ADD COLUMN content_hash VARCHAR(64) NOT NULL DEFAULT '';`

### 2. Backend — Repository

- **`IDocumentRepository`** : nouvelle méthode `GetByFileNameAndHashAsync(string userId, string fileName, string contentHash, CancellationToken ct)` retournant le `Document` correspondant ou `null`.
- Implémentation EF Core standard dans `DocumentRepository.cs` (requête `Where(d => d.UserId == userId && d.FileName == fileName && d.ContentHash == contentHash)`).

### 3. Backend — Endpoint de pré-vérification (UX, non-autoritaire)

- **`POST /api/documents/check-duplicate`**
  - Body : `{ fileName: string, contentHash: string }` (hash calculé côté navigateur via `crypto.subtle.digest('SHA-256', ...)`).
  - Réponse `200` : `{ isDuplicate: bool, existingDocument?: { id: guid, fileName: string, createdAt: datetime } }`.
  - Implémentation : le contrôleur appelle **directement** `documentRepository.GetByFileNameAndHashAsync(...)` (pas de MediatR — c'est une lecture, conforme à la convention CQRS du projet : *« Requêtes (lecture) → appel de service direct »*). C'est la **même** méthode repository que celle utilisée par `UploadDocumentHandler` (section 4) — la logique de recherche du doublon n'existe qu'à un seul endroit.
  - Objectif : éviter d'envoyer le fichier complet uniquement pour découvrir qu'il s'agit d'un doublon. Ce contrôle est **purement informatif côté UX** ; l'autorité finale reste la commande d'upload (section suivante), qui protège contre tout contournement ou situation de course, y compris pour d'éventuels futurs appelants (console, autre client API) qui invoqueraient `UploadDocumentCommand` sans passer par cet endpoint.

### 4. Backend — Commande d'upload (logique métier déplacée dans le Handler)

**Principe** : la décision « bloquer / remplacer / créer normalement » est une règle métier et doit vivre dans la couche Application (`UploadDocumentHandler`), pas dans le contrôleur — conformément à la règle du projet *« Pas de logique métier dans les controllers »*. Ceci garantit aussi que la protection anti-doublon ne peut jamais être contournée, quel que soit l'appelant de la commande.

- **`UploadDocumentCommand`** : ajout de deux propriétés `ContentHash` (string) et `ReplaceDocumentId` (Guid?).
- **`UploadDocumentHandler.Handle`** :
  1. Recherche un doublon via `documentRepository.GetByFileNameAndHashAsync(request.UserId, request.FileName, request.ContentHash, ct)`.
  2. Si un doublon est trouvé et que `request.ReplaceDocumentId` ne correspond pas exactement à son `Id` → lève une exception dédiée `DuplicateDocumentException(existingDocument)`.
  3. Si un doublon est trouvé et que `request.ReplaceDocumentId` correspond → `documentRepository.DeleteAsync(existing.Id, ct)` **puis** `documentRepository.AddAsync(nouveauDocument, ct)`, suivis d'un **seul** `unitOfWork.SaveChangesAsync(ct)` final. Suppression et recréation sont ainsi **atomiques dans une seule transaction** (correction volontaire par rapport à une première approche envisagée : dispatcher un `DeleteDocumentCommand` imbriqué via MediatR aurait créé deux transactions séparées, avec un risque réel de perdre l'ancien document sans garantie que le nouveau soit créé en cas d'échec entre les deux étapes).
  4. Si aucun doublon n'est trouvé → création normale (comportement actuel inchangé), en persistant `ContentHash` sur le nouveau `Document`.
- **`DocumentsController.Upload`** : calcule le SHA-256 du fichier temporaire déjà reçu (opération d'I/O, pas une décision métier — même niveau que l'utilitaire `GetContentType` déjà présent dans ce contrôleur), transmet `ContentHash` et `ReplaceDocumentId` (query param, optionnel) à `UploadDocumentCommand`, et attrape spécifiquement `DuplicateDocumentException` pour renvoyer un **`409 Conflict`** avec les informations du document existant (`id`, `fileName`, `createdAt`). C'est cohérent avec le seul pattern de gestion d'erreur déjà en place dans ce contrôleur (le `catch (Exception ex)` autour de l'appel à `ingestionService.IngestAsync`).
- **`DuplicateDocumentException`** : nouvelle exception dans `AIExperience.Rag.Application` (ou `Domain`), portant les informations du document existant nécessaires à la construction de la réponse 409.

**Cas limite** : si `ReplaceDocumentId` est fourni mais qu'aucun doublon ne correspond (ex. l'original a été supprimé entre-temps par un autre onglet) → traité comme une création normale, sans erreur bloquante.

### 5. Frontend

- **`api/client.ts`** :
  - Nouvelle fonction `checkDuplicate(fileName: string, contentHash: string): Promise<CheckDuplicateResponse>`.
  - `upload()` accepte un paramètre optionnel `replaceDocumentId?: string`, transmis en query string.

- **`DocumentsPage.tsx`** — nouveau flux `handleUpload` :
  1. Utilisateur sélectionne un fichier via l'input existant.
  2. Calcul du hash SHA-256 en JavaScript (`crypto.subtle.digest`).
  3. Appel à `checkDuplicate(file.name, hash)`.
  4. Si `isDuplicate === false` → upload direct (flux actuel inchangé).
  5. Si `isDuplicate === true` → affichage d'une popup de confirmation, réutilisant les classes CSS existantes (`modal-overlay` / `modal` / `modal-actions`, déjà utilisées par le modal de suppression inline) :
     > « Un document nommé **"{fileName}"** a déjà été importé le {date}. Si vous continuez, ce document existant sera **supprimé** et remplacé par le nouveau fichier. »
     - Bouton **Annuler** : ferme la popup, réinitialise l'input file, aucune action réseau.
     - Bouton **Confirmer le remplacement** : appelle `upload(file, strategy, existingDocument.id)`, puis recharge la liste des documents.
  - Pas de nouveau composant `Modal` partagé créé : le pattern inline existant est reproduit à l'identique pour cette seconde popup, conformément au style déjà en place dans le fichier (YAGNI — extraction en composant réutilisable non nécessaire pour ce périmètre).

## Gestion des erreurs

- Échec du calcul de hash côté navigateur (API `crypto.subtle` indisponible, ex. contexte non-HTTPS) → fallback : upload direct sans pré-vérification front (la `DuplicateDocumentException` levée par `UploadDocumentHandler`, traduite en 409 par le contrôleur, reste la garde-fou).
- `DuplicateDocumentException` reçue par le front sur l'endpoint d'upload sans passage préalable par `check-duplicate` (cas rare, contournement ou API appelée directement) → afficher un message d'erreur générique indiquant qu'un document identique existe déjà.

## Tests prévus (détaillés dans le plan d'implémentation)

Step 3 dispose déjà d'un projet `AIExperience.Tests` (xUnit + FluentAssertions, sans bibliothèque de mocking — les tests existants n'en utilisent aucune). On reste cohérent avec cette convention :

- **Backend (automatisé, dans `AIExperience.Tests`)** :
  - `Document.Create(..., contentHash: ...)` : persiste correctement `ContentHash` (valeur par défaut `""` si omis, pour ne pas casser les tests existants qui n'en fournissent pas).
  - `UploadDocumentHandler` (cœur de la règle métier) : création normale (pas de doublon), doublon sans `ReplaceDocumentId` correspondant (→ lève `DuplicateDocumentException`), doublon avec `ReplaceDocumentId` correct (→ suppression + recréation en une seule transaction), `ReplaceDocumentId` obsolète/ne correspondant à aucun doublon (→ création normale). Utilise un faux `IDocumentRepository`/`IUnitOfWork` écrit à la main (classe privée en mémoire), conformément à l'absence de bibliothèque de mocking dans ce projet.
- **Backend (non automatisé — pas de précédent de test d'intégration DB dans ce projet)** :
  - `GetByFileNameAndHashAsync` (requête EF Core) et `DocumentsController` (calcul de hash, mapping 409, endpoint `check-duplicate`) : vérifiés manuellement via Scalar/curl, cohérent avec le fait qu'aucun repository ni controller n'est actuellement testé automatiquement dans ce projet.
- **Frontend (non automatisé — aucune infrastructure Vitest/Jest existante dans Step 3)** :
  - `handleUpload` : vérifié manuellement dans le navigateur (cas sans doublon, doublon annulé, doublon confirmé).

## Hors périmètre

- Pas de contrainte SQL `UNIQUE` (garde-fou applicatif uniquement).
- Pas d'extraction d'un composant `Modal`/`Dialog` réutilisable (scope limité à reproduire le pattern existant).
- Pas de gestion multi-utilisateurs authentifiés réels (le `UserId` reste hard-codé, hors périmètre de cette fonctionnalité).
- Pas de migration EF Core automatisée (le schéma reste géré manuellement via `init.sql`, conformément aux conventions du projet).
