# Spec — Lot 2 : extracteurs multi-format + détection de langue (Step 3)

**Date :** 2026-07-03
**Auteur :** Geoffrey (avec Claude)
**Statut :** Approuvé
**Périmètre :** `Step 3/src/Back` + petite extension `Step 3/src/Front`
**Référence plan :** [`PLAN-AMELIORATION-RAG.md`](../../../Step%203/PLAN-AMELIORATION-RAG.md) — Lot 2, points I-2 et I-6 (I-4 OCR explicitement hors périmètre)

---

## Contexte

Le Lot 0-bis (pertinence de la récupération) est livré. Le Lot 2 du plan couvre 3 constats :
- **I-2** : aucun extracteur DOCX/XLSX/CSV/PPTX/TXT/MD/JSON — seuls PDF, HTML et vidéo/audio sont supportés, alors que l'objectif annoncé est « RAG sur tous les types de document ».
- **I-4** : OCR pour les PDF scannés — **explicitement reporté** (dépendance native Tesseract plus lourde à valider, décision utilisateur).
- **I-6** : langue codée en dur (`to_tsvector('french', …)`, défauts `"fr"`) — un document anglais dégrade le full-text (mauvais stemming) et Whisper transcrit l'anglais « en français phonétique » si mal configuré.

---

## Objectif

1. Ajouter des extracteurs de texte pour DOCX, XLSX, CSV, PPTX, TXT, MD, JSON, branchés dans `CompositeTextExtractor` via le pattern `ITextExtractor` déjà en place (aucune modification de l'interface).
2. Détecter automatiquement la langue d'un document texte à l'ingestion, et l'utiliser pour indexer `content_tsv` avec le bon dictionnaire Postgres (au lieu de `'french'` figé).
3. Débloquer l'upload de ces nouveaux formats côté front (actuellement restreint à `.pdf`).

**Hors scope** : OCR (I-4), ré-ingestion automatique des documents déjà indexés en `'french'`, détection de la langue de la question posée au chat (recherche multilingue reste simplifiée).

---

## Décisions de conception

| Décision | Choix retenu | Raison |
|----------|-------------|--------|
| Propagation de `SectionTitle` pour DOCX/XLSX/PPTX | Chaque extracteur émet des lignes `# Titre` dans le texte produit | `RecursiveChunker` détecte déjà les titres Markdown — zéro changement sur le chunker |
| Pagination | `IPageAwareTextExtractor` pour XLSX (page = feuille) et PPTX (page = slide) ; pas de pagination pour DOCX/TXT/MD/JSON | Excel et PowerPoint ont une notion native de page ; Word ne paginé pas, TXT/MD/JSON non plus |
| CSV | Géré par le même extracteur qu'XLSX (`ExcelTextExtractor`), traité comme une feuille unique | Évite une dépendance CsvHelper supplémentaire : parsing CSV simple via `ClosedXML` (import direct) ou split manuel encadré de guillemets |
| Détection de langue | Heuristique maison par fréquence de mots vides (fr/en/es/de/it), zéro dépendance NuGet | Cohérent avec la philosophie 100% locale du projet (Whisper, pas de cloud) ; évite le risque de compatibilité .NET 10 d'une lib tierce non vérifiée (décision utilisateur, cf. échange) |
| Repli de langue | Langue par défaut configurable (`"fr"`) si texte < seuil de longueur ou score ambigu entre 2 langues | Évite un mauvais classement sur un texte trop court pour être significatif |
| `content_tsv` | Colonne normale (plus `GENERATED`), peuplée explicitement dans `UpsertAsync` via `to_tsvector(@regconfig::regconfig, @content)` | `to_tsvector(regconfig, text)` est `STABLE`, pas `IMMUTABLE` → Postgres interdit les colonnes générées avec langue variable par ligne. Voir §"Détail SQL" |
| Mapping ISO → regconfig | Table statique `fr→french, en→english, es→spanish, de→german, it→italian`, repli `simple` | `simple` ne fait aucun stemming mais reste un `regconfig` valide pour toute langue non mappée — jamais d'erreur SQL |
| Langue de la requête full-text | Configurable via `RagOptions.Retrieval.FullTextLanguage` (défaut `"french"`), pas de détection par question | Détecter la langue d'une question courte est peu fiable ; limite documentée, pas résolue dans ce lot |
| Langue vidéo | Aucun changement — `TranscribeVideoHandler` peuple déjà `metadata.Language = result.Language` (Whisper) | Seule la propagation en aval (upsert SQL) manquait |
| Upload front | Étendre `accept` de `DocumentsPage.tsx` à tous les nouveaux formats | Sans ça, les nouveaux extracteurs sont inaccessibles via l'UI |

---

## Architecture — fichiers impactés

### 1. Domain (`AIExperience.Rag.Domain`)

**`Interfaces/Services/ILanguageDetectionService.cs`** — nouveau :
```csharp
public interface ILanguageDetectionService
{
    /// <summary>Retourne un code ISO 639-1 ("fr", "en", ...) détecté à partir du texte fourni.</summary>
    string Detect(string text);
}
```

**`Entities/Document.cs`** — nouvelle méthode :
```csharp
public void SetDetectedLanguage(string language)
{
    Metadata = Metadata with { Language = language };
    UpdatedAt = DateTimeOffset.UtcNow;
}
```

### 2. Application (`AIExperience.Rag.Application`)

**`Services/TextExtractor/DocxTextExtractor.cs`** — nouveau. `DocumentFormat.OpenXml`, parcourt `Body.Descendants<Paragraph>()`, détecte les styles `Heading1`-`Heading4` → préfixe `#`/`##`/`###`/`####`.

**`Services/TextExtractor/ExcelTextExtractor.cs`** — nouveau. `ClosedXML`. `.xlsx` → une page par feuille (`# NomFeuille` puis lignes `colonne: valeur`) ; `.csv` → une page unique. Implémente `IPageAwareTextExtractor`.

**`Services/TextExtractor/PowerPointTextExtractor.cs`** — nouveau. `DocumentFormat.OpenXml`. Une page par slide (`# Slide N` ou titre de slide si présent), concatène les `TextBody` des shapes. Implémente `IPageAwareTextExtractor`.

**`Services/TextExtractor/PlainTextExtractor.cs`** — nouveau. `.txt`/`.md`, lecture native `File.ReadAllTextAsync`. Le Markdown est laissé tel quel (le chunker gère déjà `#`).

**`Services/TextExtractor/JsonTextExtractor.cs`** — nouveau. `System.Text.Json`, aplatissement récursif en lignes `clé.sous_clé: valeur`.

**`Services/LanguageDetection/StopwordLanguageDetectionService.cs`** — nouveau, implémente `ILanguageDetectionService`. Tokenise le texte (lowercase, mots), compte les correspondances contre 5 listes de stopwords statiques (fr/en/es/de/it, ~30 mots chacune), retourne la langue au score normalisé le plus haut si le score dépasse un seuil de confiance, sinon la langue par défaut.

**`Services/IngestionService.cs`** — modifié :
- `IngestAsync` : après extraction des pages, concatène un échantillon du texte, appelle `languageDetectionService.Detect(...)`, met à jour le document via `documentRepository.GetByIdAsync` + `document.SetDetectedLanguage(...)` + `documentRepository.UpdateAsync`, puis passe la langue détectée à `vectorStoreService.UpsertBatchAsync`.
- `IngestFromSegmentsAsync` : utilise directement `metadata.Language` (déjà peuplé par Whisper côté handler), pas de détection.
- `IngestTextAsync` : idem, utilise `metadata.Language` tel que fourni par l'appelant (méthode actuellement sans appelant, gardée cohérente par principe).

**`DependencyInjection.cs`** — enregistrement des 5 nouveaux `ITextExtractor` + `ILanguageDetectionService`.

### 3. Infrastructure (`AIExperience.Rag.Infrastructure`)

**`VectorStore/PostgresTextSearchConfig.cs`** — nouveau, mapping statique ISO → regconfig (`fr→french`, etc., repli `simple`).

**`VectorStore/PgVectorStoreService.cs`** — modifié :
- `UpsertAsync`/`UpsertBatchAsync` : nouveau paramètre `string language` (code ISO), calcule le regconfig via `PostgresTextSearchConfig`, l'inclut dans l'`INSERT` (`content_tsv = to_tsvector(@regconfig::regconfig, @content)`), y compris dans la clause `ON CONFLICT ... DO UPDATE` (recalcul si le contenu change).
- `SearchFullTextAsync`/`SearchLexicalAsync` : remplacent le littéral `'french'` par `@queryLanguage`, alimenté depuis `RagOptions.Retrieval.FullTextLanguage`.

**`Options/RagOptions.cs`** — ajout `RetrievalOptions.FullTextLanguage` (string, défaut `"french"`).

**`Options/LanguageDetectionOptions.cs`** — nouveau (ou section dans un fichier d'options existant) : `DefaultLanguage` (défaut `"fr"`), `MinTextLength` (seuil sous lequel on ne détecte pas), `MinConfidence`.

### 4. Interfaces (`IVectorStoreService`, `IIngestionService`)

Signatures `UpsertAsync`/`UpsertBatchAsync` enrichies d'un paramètre `language`. `IIngestionService` inchangé côté signature publique (la langue circule via `DocumentMetadata` déjà présent dans les signatures).

### 5. Schéma SQL

**`scripts/init.sql`** — modifié : `content_tsv` n'est plus `GENERATED ALWAYS AS ... STORED`, devient une colonne `tsvector` normale (toujours indexée par le GIN existant). Commentaire mis à jour expliquant la contrainte `STABLE` vs `IMMUTABLE`.

**`scripts/migrate-i6-language-fts.sql`** — nouveau, pour les bases déjà créées (même pattern que `migrate-temporal-chunks.sql`) :
```sql
ALTER TABLE document_chunks DROP COLUMN content_tsv;
ALTER TABLE document_chunks ADD COLUMN content_tsv tsvector;
UPDATE document_chunks SET content_tsv = to_tsvector('french', content); -- backfill, langue historique
CREATE INDEX IF NOT EXISTS ix_document_chunks_content_tsv ON document_chunks USING gin (content_tsv);
```

### 6. Front-end (`Step 3/src/Front`)

**`src/pages/DocumentsPage.tsx`** — `accept=".pdf"` → liste complète des extensions supportées ; libellé bouton `"+ Ajouter un PDF"` → `"+ Ajouter un document"`.

---

## Détail SQL — pourquoi `content_tsv` ne peut plus être `GENERATED`

PostgreSQL exige que l'expression d'une colonne `GENERATED ALWAYS AS (...) STORED` soit **immuable** (`IMMUTABLE`). `to_tsvector(regconfig, text)` est catalogué `STABLE` (le résultat dépend potentiellement de l'état des dictionnaires de recherche), donc toute tentative de généra­tion avec un `regconfig` **variable par ligne** (ex. une colonne `chunk_language`) est rejetée par Postgres à la création de la table. Seul un littéral constant (comme l'actuel `'french'`) fonctionne en `GENERATED`.

La solution retenue : sortir le calcul de `content_tsv` de la déclaration de colonne et le faire explicitement dans le SQL d'`INSERT`/`UPDATE` (`UpsertAsync`), où `to_tsvector` est appelé normalement (aucune contrainte d'immutabilité en dehors d'une colonne générée). L'index GIN reste valide car il porte sur une colonne physique, générée ou non.

---

## Flux de données — ingestion d'un document texte

```
Upload (DocumentsController)
  → UploadDocumentCommand (métadonnées par défaut, Language="fr")
  → IngestionService.IngestAsync
      1. CompositeTextExtractor.ExtractPagesAsync (dispatch par extension → 8 extracteurs possibles)
      2. Concatène un échantillon du texte extrait
      3. ILanguageDetectionService.Detect(échantillon) → code ISO détecté
      4. documentRepository.GetByIdAsync + Document.SetDetectedLanguage + UpdateAsync
      5. ITextChunker.ChunkPages (inchangé — propage PageNumber/SectionTitle déjà émis par les extracteurs)
      6. IEmbeddingService.EmbedBatchAsync (inchangé)
      7. IVectorStoreService.UpsertBatchAsync(items, langueDétectée)
          → INSERT ... content_tsv = to_tsvector(regconfig(langueDétectée), content)
```

---

## Gestion d'erreurs

- Fichier corrompu / illisible par `DocumentFormat.OpenXml` ou `ClosedXML` : l'exception native remonte telle quelle (cohérent avec `PdfTextExtractor`/`HtmlTextExtractor` existants, pas de wrapping supplémentaire) — capturée par `DocumentsController.Upload` qui marque le document `Failed`.
- Langue non reconnue (score sous le seuil de confiance) : repli silencieux sur la langue par défaut, pas d'exception.
- Code langue détecté sans mapping Postgres connu : repli sur `regconfig 'simple'`, jamais d'échec SQL.
- Fichier JSON/XLSX/PPTX vide ou sans texte exploitable : le garde-fou I-3 existant (0 chunk → `InvalidOperationException`) s'applique sans modification.

---

## Tests prévus

| Test | Fichier | Couverture |
|---|---|---|
| `DocxTextExtractorTests` | fixture `.docx` minimale (titres + paragraphes) | extraction texte + propagation `# Titre` |
| `ExcelTextExtractorTests` | fixture `.xlsx` multi-feuilles + `.csv` | pagination par feuille, format `colonne: valeur` |
| `PowerPointTextExtractorTests` | fixture `.pptx` multi-slides | pagination par slide |
| `PlainTextExtractorTests` | `.txt`, `.md` | lecture brute, pas de transformation |
| `JsonTextExtractorTests` | JSON imbriqué | aplatissement clé.sous_clé |
| `StopwordLanguageDetectionServiceTests` | échantillons fr/en/es/de/it, texte court, texte vide | détection + repli |
| `PostgresTextSearchConfigTests` | codes connus + code inconnu | mapping + repli `simple` |
| `CompositeTextExtractorTests` (existant, étendu) | nouvelles extensions | dispatch correct vers le bon extracteur |

Exécution : `dotnet test "Step 3/src/Back/AIExperience.slnx"`.

---

## Risques & dette technique

- **Pas de ré-ingestion automatique** : les documents déjà indexés gardent un `content_tsv` calculé en `'french'` tant qu'ils ne sont pas ré-uploadés — cohérent avec la limitation déjà actée pour R-15 (préfixes nomic).
- **Recherche full-text reste mono-langue par requête** (`FullTextLanguage` configuré, pas détecté) : un corpus vraiment multilingue interrogé dans plusieurs langues nécessiterait une recherche par langue + fusion RRF, explicitement hors scope.
- **Heuristique stopwords** : moins précise qu'une lib entraînée sur de gros corpus, mais suffisante pour discriminer fr/en/es/de/it sur des documents de taille normale (paragraphes, pas des tweets).
- **`ClosedXML`/`DocumentFormat.OpenXml`** : licences MIT, largement utilisées, faible risque de compatibilité .NET 10 (contrairement à une lib de détection de langue non vérifiée).
