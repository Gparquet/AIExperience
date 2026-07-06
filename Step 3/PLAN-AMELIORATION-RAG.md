# Plan d'amélioration — Pipeline d'ingestion & de restitution RAG (Step 3)

> Document d'analyse technique rédigé en posture de **tech lead .NET / IA**.
> Objectif : améliorer la **qualité**, la **robustesse** et la **performance** du pipeline
> RAG (ingestion → embeddings → recherche → génération LLM) **sur tous les types de document**.
> Date d'analyse : 2026-06-26 — Périmètre : `Step 3/src/Back` + `Step 3/src/Front`.
> **Révision : 2026-07-02** — mise à jour d'avancement (Lot 0 ✅, Lot 1 ~80 %), ajout des
> constats R-15 à R-18 (pertinence de la récupération) et insertion du **Lot 0-bis**.
> **Révision : 2026-07-03** — R-15/R-16/R-9 livrés. R-10 mesuré empiriquement **non viable** (le
> seuil cosinus ne discrimine pas signal/bruit sur ce corpus, cf. §4.5) → déprioritisé au profit de
> **R-18** (reranker batché), livré (commit `69b8282`). **R-17 livré** (`RagStrategyResolver` +
> `Cache.Enabled=false` par défaut) → **Lot 0-bis 100 % livré**.
> **Révision : 2026-07-05** — **Lot 2 livré** (I-2 : extracteurs DOCX/XLSX/CSV/PPTX/TXT/MD/JSON ;
> I-6 : détection de langue heuristique fr/en/es/de/it + `content_tsv` par langue détectée au lieu
> de `'french'` figé). I-4 (OCR) explicitement hors périmètre, reporté. Implémenté via 14 tâches
> TDD (`docs/superpowers/plans/2026-07-03-lot2-multi-format-langue.md`), revues une à une puis en
> revue finale de branche (corrections : validateur d'upload `.doc`/`.xls`/`.md`, asymétrie langue
> index/requête FTS documentée comme limitation connue). Commits `5f761f1`..`e5a2142`.
> **Révision : 2026-07-06** — diagnostic du symptôme *« restitution médiocre sur les vidéos »* :
> ajout des constats **I-21** (timestamps inline embeddés — l'équivalent vidéo de R-15) et **I-22**
> (double chemin d'ingestion vidéo incohérent), et insertion du **Lot 2-bis — Qualité de la
> restitution vidéo** (I-21, I-22, I-10 remonté du reliquat Lot 1, montée de modèle Whisper).

---

## 1. Synthèse exécutive

La Step 3 est fonctionnellement riche (vidéo Whisper, 3 modes RAG, stratégies HyDE/Fusion/Adaptive,
reranking, compression, citations enrichies). Mais l'analyse du code révèle **3 catégories de problèmes** :

| Catégorie | Gravité | Exemple emblématique |
|-----------|---------|----------------------|
| **Bugs de bout en bout** (cassent une fonctionnalité annoncée) | 🔴 Critique | Le LLM reçoit un **GUID** au lieu du **nom du document** → les citations `[SOURCE: …]` sont systématiquement fausses |
| **Lacunes multi-format** | 🟠 Élevée | HTML lève `NotImplementedException`, aucun extracteur DOCX/TXT/MD/CSV, OCR PDF absent |
| **Anti-patterns performance / qualité** | 🟡 Moyenne | Une seule question RAG = **jusqu'à ~26 appels LLM séquentiels** (rerank + compression) |

Le fil rouge : **la chaîne « page → section → citation » est rompue** et **la restitution est lente
et peu fiable** parce que les métadonnées de structure ne sont jamais propagées et que les étapes
LLM coûteuses sont activées par défaut sans batching.

### Avancement (révision 2026-07-03)

| Lot | Statut | Preuve |
|-----|--------|--------|
| **Lot 0** — quick wins critiques | ✅ **Livré** (R-1, R-3, R-4, R-5/R-6 défauts, I-1, I-3, I-19 + tests) | commit `4a9dbb7` (28/06) |
| **Lot 1** — robustesse ingestion | 🟨 **~80 % livré** (I-5, I-7, I-8, I-9 partiel, I-11, I-12, I-17, I-18) — reste : taille en tokens (I-9), I-10, I-13, I-14, I-15 | commits `f18c79a`, `61b20bc`, `f12b23a`, `742f59d` (02/07) |
| **Lot 0-bis** — pertinence de la récupération | ✅ **100 % livré** — R-15/R-16/R-9/R-18/R-17 livrés, R-10 mesuré non viable et déprioritisé | commits `96a141a`, `69b8282` (03/07) + §4.5 |
| **Lot 2** — multi-format complet | ✅ **Livré** — I-2 (DOCX/XLSX/CSV/PPTX/TXT/MD/JSON) + I-6 (détection de langue + `content_tsv` par langue). I-4 (OCR) hors périmètre | commits `5f761f1`..`e5a2142` (05/07), plan `docs/superpowers/plans/2026-07-03-lot2-multi-format-langue.md` |
| **Lot 2-bis** — qualité de la restitution vidéo | 🔜 **Prochain lot** — I-21, I-22, I-10, Whisper `medium` (voir révision 06/07) | — |
| **Lots 3, 4** | ⏳ Non commencés | — |

> ⚠️ **Effet de bord du Lot 0, non anticipé** : la désactivation du reranker (R-5) a supprimé la
> coupe à `TopKAfterRerank = 5` et la compression ne filtre plus les chunks « AUCUN ». Le dernier
> filtre de pertinence a disparu : les **10 chunks bruts** du Top-K sont désormais tous injectés
> au LLM **et affichés en citations**, bruit compris. D'où l'insertion du Lot 0-bis qui restaure
> un filtrage de précision *sans* réintroduire la latence (voir R-18).

---

## 2. Diagnostic détaillé — INGESTION

### 2.1. Extraction de texte (multi-format) — la plus grosse lacune

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| I-1 | `HtmlTextExtractor.ExtractTextAsync` lève `NotImplementedException`. Le `CompositeTextExtractor` le sélectionne via `CanHandle` puis l'appelle → **upload d'un .html = crash de l'ingestion** | [HtmlTextExtractor.cs:13](Step%203/src/Back/AIExperience.Rag.Application/Services/TextExtractor/HtmlTextExtractor.cs#L13) | 🔴 Format annoncé mais cassé |
| I-2 | Aucun extracteur **DOCX, XLSX, PPTX, TXT, Markdown, CSV, JSON, EML**. Seuls PDF + vidéo/audio fonctionnent | dossier `TextExtractor/` | 🟠 « tous types de document » non tenu |
| I-3 | `CompositeTextExtractor` : si aucun extracteur ne correspond, retourne `string.Empty` **silencieusement**. Le document est ensuite marqué `Completed` avec **0 chunk** | [CompositeTextExtractor.cs:22-29](Step%203/src/Back/AIExperience.Rag.Application/Services/TextExtractor/CompositeTextExtractor.cs#L22-L29) | 🟠 Échec silencieux, document « fantôme » |
| I-4 | `PdfTextExtractor` : pas d'**OCR** (PDF scannés/images → texte vide), pas de préservation des tableaux, pas d'extraction des métadonnées (titre/auteur/nb pages) | [PdfTextExtractor.cs](Step%203/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PdfTextExtractor.cs) | 🟠 Pans entiers de documents ignorés |
| I-5 | **Numéro de page perdu** : le PDF est aplati en une seule string (`AppendLine` + `\n\n`) avant le chunking → `TextChunk.PageNumber` toujours `null` → citations sans page | [PdfTextExtractor.cs:18-33](Step%203/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PdfTextExtractor.cs#L18-L33) | 🔴 Casse les citations `p.X` |
| I-6 | **Langue codée en dur** : `to_tsvector('french', …)`, Whisper `"fr"` par défaut dans `VideoTextExtractor`. Un document EN dégrade full-text et embeddings | [VideoTextExtractor.cs:59](Step%203/src/Back/AIExperience.Rag.Application/Services/TextExtractor/VideoTextExtractor.cs#L59) | 🟠 Multilingue non géré |

### 2.2. Chunking

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| I-7 | Le `RecursiveChunker` **n'est pas récursif** : il découpe par paragraphe uniquement. Un paragraphe de 5 000 caractères devient **un chunk de 5 000 caractères** (pas de repli phrase), contredisant son propre commentaire | [RecursiveChunker.cs:16-39](Step%203/src/Back/AIExperience.Rag.Application/Services/RecursiveChunker.cs#L16-L39) | 🟠 Chunks géants → embeddings dilués, recall faible |
| I-8 | Le chunker ne renseigne **jamais** `SectionTitle` ni `PageNumber`. Toute la chaîne « citation enrichie avec section » (annoncée en Step 3) est donc **vide en pratique** pour les documents texte | idem | 🔴 Fonctionnalité annoncée inopérante |
| I-9 | Overlap **basé caractères** (`[^OverlapSize..]`) → coupe en plein milieu d'un mot ; taille **en caractères** et non en **tokens** → risque de troncature silencieuse par le modèle d'embedding | [RecursiveChunker.cs:12-13,28](Step%203/src/Back/AIExperience.Rag.Application/Services/RecursiveChunker.cs#L12-L13) | 🟡 Qualité d'embedding |
| I-10 | `TemporalChunker` : un segment Whisper > 800 car. est ajouté **entier** (jamais scindé) → chunk surdimensionné possible ; aucun overlap temporel | [TemporalChunker.cs:27-42](Step%203/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs#L27-L42) | 🟡 Cas limite |

### 2.3. Embeddings

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| I-11 | `EmbedBatchAsync` envoie **TOUS** les chunks en un seul appel. Un PDF de 500 pages → des milliers de chunks dans une requête → rejet/timeout du provider | [OpenAIEmbeddingService.cs:28-32](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Embedding/OpenAIEmbeddingService.cs#L28-L32) | 🔴 Ingestion des gros docs impossible |
| I-12 | **Aucune résilience** (retry/backoff type Polly) sur embeddings et LLM. Une erreur transitoire avorte toute l'ingestion | tout l'Infrastructure/AI | 🟠 Fragilité |
| I-13 | Dimension d'embedding **non validée** vs colonne pgvector `vector(768)`. Un changement de provider (3072 dims) échoue à l'`INSERT` sans message clair | [init.sql:51](Step%203/scripts/init.sql#L51) | 🟡 Couplage caché |
| I-14 | **Aucune idempotence** : ré-uploader le même fichier crée des chunks **dupliqués** | `IngestionService` | 🟡 Pollution de l'index |

### 2.4. Persistance & schéma

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| I-15 | `UpsertBatchAsync` exécute **N `INSERT` unitaires** dans une transaction (mieux que N commits, mais N allers-retours). Pour les gros docs, lent. `COPY` / `NpgsqlBinaryImporter` serait l'ordre de grandeur au-dessus | [PgVectorStoreService.cs:192-206](Step%203/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs#L192-L206) | 🟡 Débit ingestion |
| I-16 | **Ingestion 100 % synchrone dans la requête HTTP** : `DocumentsController.Upload` attend `IngestAsync`. Un gros PDF/vidéo bloque le thread et risque le timeout. L'Outbox/BackgroundService est **documenté mais non câblé** (le handler ne fait que persister) | [DocumentsController.cs:65-78](Step%203/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs#L65-L78), [UploadDocumentHandler.cs](Step%203/src/Back/AIExperience.Rag.Application/Document/Command/UploadDocumentHandler.cs) | 🔴 Scalabilité / UX |
| I-17 | `init.sql` : **aucun index full-text GIN**. `SearchFullTextAsync` recalcule `to_tsvector('french', content)` **à chaque ligne et à chaque requête** → scan séquentiel | [init.sql:43-62](Step%203/scripts/init.sql#L43-L62) vs [PgVectorStoreService.cs:110-122](Step%203/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs#L110-L122) | 🟠 Mode « Classique » lent à l'échelle |
| I-18 | `init.sql` **ne contient pas** `start_time_seconds` / `end_time_seconds` (appliquées à la main via migration). Schéma à la dérive | [init.sql](Step%203/scripts/init.sql), `migrate-temporal-chunks.sql` | 🟠 Reproductibilité |

### 2.5. Vidéo

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| I-19 | **Bug Whisper langue** : `GetOrCreateProcessorAsync(language)` construit le processeur avec la **1ʳᵉ langue** reçue et le met en cache (Singleton). Les appels suivants avec une autre langue **réutilisent le mauvais processeur** | [WhisperTranscriptionService.cs:85-119](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Transcription/WhisperTranscriptionService.cs#L85-L119) | 🔴 Transcription multilingue fausse |
| I-20 | Pas de VAD ni de découpage des vidéos longues : tout le fichier en une passe, sans progression ni gestion mémoire | `WhisperTranscriptionService` | 🟡 Robustesse gros fichiers |
| I-21 | **Timestamps inline embeddés** *(nouveau — révision 06/07)* : `TemporalChunker` incruste `[HH:MM:SS → HH:MM:SS]` dans le `Content` de chaque chunk, et c'est ce texte formaté qui part à l'embedding. Les segments Whisper étant courts, **~25–35 % de chaque chunk vidéo est du bruit numérique** → similarité question/chunk effondrée. Les timestamps sont pourtant déjà persistés proprement (`StartTime`/`EndTime`) | [TemporalChunker.cs:53](Step%203/src/Back/AIExperience.Rag.Application/Services/TemporalChunker.cs#L53), [IngestionService.cs:137](Step%203/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs#L137) | 🔴 Restitution vidéo dégradée |
| I-22 | **Double chemin d'ingestion vidéo incohérent** *(nouveau — révision 06/07)* : la page Documents accepte `.mp4/.mkv/…` mais ce chemin passe par `VideoTextExtractor.ExtractTextAsync` qui fige la langue à `"fr"` et retourne `FullText` (timestamps inline inclus) découpé par le `RecursiveChunker` texte → coupes arbitraires, **aucun `StartTime`/`EndTime`**, citations sans repère temporel. Seul `POST /api/video/transcribe` emprunte le bon chemin (`IngestFromSegmentsAsync`) | [VideoTextExtractor.cs:59](Step%203/src/Back/AIExperience.Rag.Application/Services/TextExtractor/VideoTextExtractor.cs#L59), [DocumentsPage.tsx:8](Step%203/src/Front/src/pages/DocumentsPage.tsx#L8) | 🔴 Qualité dépendante du point d'entrée |

---

## 3. Diagnostic détaillé — RESTITUTION (RAG / LLM)

### 3.1. Bugs critiques de qualité de réponse

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| R-1 | **Le contexte injecté au LLM contient le GUID du document**, pas son nom : `Document: {chunk.DocumentId}, Page: {chunk.PageNumber}`. Or le prompt système exige `[SOURCE: NomDocument, p.X]`. Le modèle ne **peut pas** citer correctement → noms inventés / GUID bruts. `chunk.DocumentName` est pourtant disponible | [RagPipelineService.cs:416-421](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs#L416-L421) | 🔴 Citations textuelles fausses |
| R-2 | **Historique de conversation mort** : `ChatController.Ask`/`Stream` ne transmettent jamais de `SessionId` ni ne persistent les messages. `SessionId == Guid.Empty` → `IncludeHistory` est inopérant. Tables `conversation_sessions` / `chat_messages` inutilisées côté API | [ChatController.cs:28-36](Step%203/src/Back/AIExperience.Web.Api/Controllers/ChatController.cs#L28-L36) | 🔴 Multi-tour non fonctionnel |
| R-3 | **Prompts système = restes de démo contradictoires** : RAG = « expert dans la finance », LLM direct = « expert dans le voyage ». Inadaptés à un RAG générique | [RagPrompts.cs:22-31,90-95](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/PromptTemplates/RagPrompts.cs#L22-L31) | 🟠 Réponses biaisées |
| R-4 | **Aucun garde-fou « contexte vide »** : si la récupération ne renvoie rien, on appelle quand même le LLM avec un contexte vide (hallucination probable) | [RagPipelineService.cs:80-86](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs#L80-L86) | 🟡 Fiabilité |

### 3.2. Performance de la restitution

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| R-5 | **Reranker LLM = N appels séquentiels** (1 par chunk, jusqu'à `TopK=20`). Activé **par défaut**. Avec un modèle local 1B, 20 appels avant même de répondre | [LlmRerankerService.cs:28-64](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/LlmRerankerService.cs#L28-L64), [RagOptions.cs:58-65](Step%203/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs#L58-L65) | 🔴 Latence majeure |
| R-6 | **Compression contextuelle = N appels séquentiels** supplémentaires (1 par chunk survivant), activée **par défaut**. Total d'une question RAG : ~20 (rerank) + ~5 (compression) + 1 (réponse) ≈ **26 appels LLM** | [ContextCompressorService.cs:14-34](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/ContextCompressorService.cs#L14-L34) | 🔴 Latence catastrophique |
| R-7 | **Fusion** : embeddings + recherches **séquentiels** (limitation Npgsql mono-connexion documentée). Pourrait paralléliser via connexions dédiées | [RagPipelineService.cs:354-377](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs#L354-L377) | 🟡 Latence |
| R-8 | Pas de **cache** malgré `CacheOptions` : toute question identique rejoue tout le pipeline | [RagOptions.cs:74-82](Step%203/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs#L74-L82) | 🟡 Coût/latence |

### 3.3. Pertinence de la récupération

| # | Constat | Localisation | Impact |
|---|---------|--------------|--------|
| R-9 | **Pas de recherche hybride** : on fait vectoriel **ou** full-text, jamais les deux fusionnés. Les briques existent (`SearchFullTextAsync`, `ReciprocalRankFusion`) mais ne sont pas combinées → recall sous-optimal sur termes rares/exacts | `RagPipelineService.RetrieveChunksAsync` | 🟠 Qualité du recall |
| R-10 | `hnsw.ef_search` non réglé ; `ScoreThreshold = 0.3` **inopérant avec `nomic-embed-text`** (deux textes sans aucun rapport scorent 0.4–0.6 avec ce modèle → le seuil ne filtre rien, le Top-K se remplit de bruit). **Reclassé 🟡 → 🔴** : c'est la cause directe des « sources hors sujet » visibles dans le chat | [PgVectorStoreService.cs:44-56](Step%203/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs#L44-L56) | 🔴 Sources non pertinentes |
| R-11 | **Aucune gestion de budget tokens** : contexte construit depuis tous les chunks sans contrôle de la fenêtre du modèle local (≈4096). Risque de troncature silencieuse de l'entrée | [RagPipelineService.cs:414-427](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs#L414-L427) | 🟠 Réponses tronquées |
| R-15 | **Préfixes de tâche `nomic` absents** *(nouveau — révision 02/07)* : `nomic-embed-text-v1.5` est un modèle **asymétrique** entraîné avec les préfixes `search_document:` (ingestion) et `search_query:` (question). `OpenAIEmbeddingService` envoie le texte brut des deux côtés → séparation pertinent/non-pertinent fortement dégradée | [OpenAIEmbeddingService.cs:45-51](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Embedding/OpenAIEmbeddingService.cs#L45-L51) | 🔴 Qualité de récupération |
| R-16 | **Fusion : résultat RRF jamais tronqué ni seuillé** *(nouveau — révision 02/07)* : `RetrieveChunksAsync` retourne `ReciprocalRankFusion.Fuse(...)` sans `.Take(TopK)` → jusqu'à 4 requêtes × TopK = **40 chunks** injectés au LLM et affichés en citations. Les scores RRF (~0.016–0.06) remplacent les scores cosinus → aucun seuil applicable, scores incohérents dans l'UI | [RagPipelineService.cs:418](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs#L418) | 🔴 Explosion du contexte |
| R-17 | **Flags de configuration ignorés** *(nouveau — révision 02/07)* : `HyDE.Enabled` et `MultiQuery.Enabled` ne sont jamais consultés par le pipeline (le routeur Adaptive peut router vers HyDE/Fusion même désactivés) ; `Cache.Enabled = true` par défaut alors qu'**aucun cache n'existe** — config trompeuse | [RagOptions.cs](Step%203/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs), [RagPipelineService.cs:385-427](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs#L385-L427) | 🟡 Config non fiable |
| R-18 | **Citations = tous les chunks récupérés** *(nouveau — révision 02/07)* : chaque chunk du Top-K devient une citation affichée, sans distinction « injecté au LLM » / « réellement utilisé ». Depuis la désactivation du reranker (Lot 0), plus aucun filtre ne s'interpose : le bruit du Top-K est visible tel quel dans le panneau Sources | [RagPipelineService.cs:110-119](Step%203/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs#L110-L119) | 🔴 Sources non pertinentes |

### 3.4. Observabilité & qualité transverse

| # | Constat | Impact |
|---|---------|--------|
| R-12 | Aucune **mesure par étape** (timing rerank/compression/LLM) → impossible de diagnostiquer la lenteur | 🟡 |
| R-13 | Aucun **harnais d'évaluation** (recall@k, fidélité des réponses, jeu de questions « golden ») → on améliore à l'aveugle | 🟠 |
| R-14 | `TotalTokens = 0` en mode streaming ; pas de suivi de consommation | 🟡 |

---

## 4. Plan d'action priorisé

Notation : **Impact** (1-5) × **Effort** (S/M/L). On attaque d'abord *fort impact / faible effort*.

### 🥇 Lot 0 — Correctifs critiques « quick wins » (1 à 2 jours) — ✅ LIVRÉ (28/06, commit `4a9dbb7`)

> Rétablissent des fonctionnalités **annoncées mais cassées**, sans refonte.

1. **R-1 Corriger le contexte LLM** — injecter `chunk.DocumentName` (+ `SectionTitle`, page) à la place du GUID dans `BuildChatHistoryAsync`. *Impact 5 / S.*
2. **R-5 + R-6 Désactiver par défaut** Reranker et Compression (`Enabled=false` dans `appsettings.json`), ou les conditionner à un seuil de chunks. Réduit la latence d'un facteur ~20. *Impact 5 / S.*
3. **R-3 Neutraliser les prompts** « finance » / « voyage » → prompts génériques et configurables via `RagOptions`. *Impact 4 / S.*
4. **I-1 HTML** — implémenter l'extraction (HtmlAgilityPack ou `AngleSharp`) ou, a minima, ne plus crasher (fallback texte brut). *Impact 4 / S.*
5. **I-19 Bug langue Whisper** — clé de cache du processeur par langue (dictionnaire `langue → processor`) ou rebuild si la langue change. *Impact 4 / S.*
6. **I-3 Échec d'extraction non silencieux** — lever/loguer une vraie erreur et marquer le document `Failed` si 0 extracteur ou 0 chunk. *Impact 4 / S.*

### 🥈 Lot 1 — Robustesse de l'ingestion (3 à 5 jours) — 🟨 ~80 % LIVRÉ (02/07)

7. ✅ **I-11 Sous-batching des embeddings** (96 textes/appel) + **I-12 retry Polly** (backoff exponentiel). *Livré — commit `61b20bc`.*
8. 🟨 **I-7/I-8/I-9 Refonte du chunker** : vrai découpage récursif, overlap sur frontière de phrase, propagation `SectionTitle` + `PageNumber` — *livré, commit `f18c79a`*. **Reste : taille en tokens** (le chunker compte toujours en caractères). *Impact 3 / M.*
9. ✅ **I-5 Préserver la pagination PDF** : extraction page par page via `IPageAwareTextExtractor`, page propagée dans chaque chunk. *Livré — commit `f18c79a`.*
10. ✅ **I-17 Index full-text GIN** : colonne générée `content_tsv` + index GIN, requête ciblant la colonne indexée. *Livré — commit `f12b23a`.*
11. ✅ **I-18 Réaligner `init.sql`** : colonnes temporelles + `content_tsv` + index GIN intégrés. *Livré — commit `f12b23a`.*

> Reliquat Lot 1 (reporté, non bloquant) : **I-9** taille en tokens, ~~**I-10** segment Whisper
> surdimensionné~~ *(remonté au Lot 2-bis, point 31)*, **I-13** validation de la dimension
> d'embedding, **I-14** idempotence (doublons), **I-15** `COPY` binaire.

### 🎯 Lot 0-bis — Pertinence de la récupération (2 à 4 jours) — 🔜 PROCHAIN LOT

> **Nouveau (révision 02/07).** Répond au symptôme observé en usage réel : *« le chat remonte des
> sources de mon PDF, mais aussi d'autres ressources qui n'ont rien à voir »*. Cause structurelle :
> le Top-K retourne **toujours** K voisins (pertinents ou non), le seuil 0.3 ne filtre rien avec
> `nomic-embed`, et depuis le Lot 0 plus aucun filtre ne s'interpose avant les citations (R-18).

23. **R-15 Préfixes `nomic`** : préfixer `search_document:` à l'ingestion et `search_query:` à la question dans `OpenAIEmbeddingService`, conditionné au modèle via `AiProviderOptions` (ex. `EmbeddingTaskPrefixes`). ⚠️ **Impose de ré-ingérer les documents** (vecteurs stockés incompatibles). *Impact 5 / S.*
24. **R-16 Tronquer après RRF** : `.Take(opts.Retrieval.TopK)` sur le résultat de `Fuse(...)` ; distinguer score cosinus / score RRF dans les citations (ne pas afficher un RRF comme une similarité). *Impact 4 / S.*
25. **R-10 Calibrer le seuil et le Top-K** : mesurer la distribution réelle des scores sur le corpus (question pertinente vs hors sujet), remonter `ScoreThreshold` en conséquence (ordre de grandeur 0.55–0.65 pour `nomic`, **après** R-15), et baisser `TopK` 10 → 5-6 tant qu'aucun reranker ne filtre derrière. Régler `hnsw.ef_search`. *Impact 4 / S.*
26. **R-5' Reranker batché** *(remonté du Lot 3, point 15)* : **un seul** appel LLM qui note tous les chunks (JSON `{index: score}`) au lieu de N appels séquentiels — restaure le filtre de précision perdu au Lot 0 sans réintroduire la latence. Réactiver `Reranker.Enabled = true` une fois batché. *Impact 5 / M.*
27. **R-9 Recherche hybride** *(remontée du Lot 3, point 16)* : vectoriel + full-text fusionnés via le RRF existant. *Impact 4 / M.*
28. ✅ **R-17 Honorer les flags de config** : consulter `HyDE.Enabled` / `MultiQuery.Enabled` dans le pipeline (repli sur Direct si désactivé) ; passer `Cache.Enabled = false` par défaut tant que le cache n'est pas implémenté (R-8). *Impact 2 / S.* **Livré (03/07)** : `RagStrategyResolver.ResolveFallback` (statique, testable sans mock) appliqué dans `RagPipelineService.ResolveStrategyAsync` (log `Warning` si repli), `CacheOptions.Enabled` par défaut `false`. 6 nouveaux tests (87/87).

> **Vérification avant/après (mini R-13)** : constituer 5–10 questions « golden » sur le corpus
> actuel et comparer les citations retournées avant et après chaque correctif du lot — sinon la
> calibration du seuil (point 25) se fait à l'aveugle.

### 🥉 Lot 2 — Multi-format complet (5 à 8 jours) — ✅ LIVRÉ (05/07, sauf I-4 hors périmètre)

12. ✅ **I-2 Nouveaux extracteurs** : DOCX (`DocumentFormat.OpenXml`), XLSX/CSV (`ClosedXML` + parseur RFC 4180 maison, tabulaire → texte structuré), TXT/Markdown (natif), PPTX (`DocumentFormat.OpenXml`, pagination par slide), JSON (aplatissement récursif). Tous branchés dans `CompositeTextExtractor` via `CanHandle`, validateur d'upload et front-end mis à jour en cohérence. *Livré — commits `5f761f1`..`6678b08`.* (EML non traité — non demandé lors de l'implémentation.)
13. ⏳ **I-4 OCR PDF scannés** : détection « PDF image » → OCR (Tesseract) en repli. **Explicitement reporté** à un lot ultérieur (décision utilisateur lors du cadrage du Lot 2 — dépendance native plus lourde à valider). *Impact 3 / L.*
14. ✅ **I-6 Détection de langue** : heuristique maison par fréquence de mots vides (fr/en/es/de/it, zéro dépendance externe — choix délibéré pour rester cohérent avec la philosophie 100 % locale du projet), propagée à `content_tsv` (colonne non générée, calculée par l'application avec le `regconfig` correspondant à la langue détectée). Vidéo/Whisper déjà correct (langue transmise par `TranscribeVideoHandler`, seule la propagation en aval manquait). **Limitation connue documentée** : la requête full-text reste analysée dans une langue unique configurable (`RagOptions.Retrieval.FullTextLanguage`), pas détectée par question — un corpus multi-langues peut donc avoir une correspondance dégradée pour les documents dans une langue différente de celle configurée. *Livré — commits `312b004`..`e5a2142`.*

### 🎬 Lot 2-bis — Qualité de la restitution vidéo (2 à 4 jours) — 🔜 PROCHAIN LOT

> **Nouveau (révision 06/07).** Répond au symptôme observé en usage réel : *« la restitution
> n'est pas top sur les vidéos quand je pose une question »*. Cause n°1 : le texte embeddé des
> chunks vidéo est pollué par les timestamps inline (I-21) — l'équivalent vidéo du R-15 qui avait
> corrigé les sources hors sujet. Cause n°2 : selon la page utilisée pour uploader (Documents vs
> Vidéo), la qualité d'ingestion n'est pas la même (I-22).

29. **I-21 Contenu de chunk épuré** : `TemporalChunker` garde le **texte pur** dans `Content` ;
    le timing reste porté par `StartTime`/`EndTime` (déjà persistés et propagés aux citations).
    Pour que le LLM puisse citer un horodatage, injecter la plage temporelle dans **l'en-tête
    d'extrait** de `BuildChatHistoryAsync` (`[Extrait i] Source: …, 00:12:30–00:13:10`), là où
    page/section sont déjà injectés. ⚠️ **Ré-ingestion des vidéos obligatoire** (vecteurs stockés
    calculés sur le texte pollué). *Impact 5 / S.*
30. **I-22 Unifier les chemins d'ingestion vidéo** : l'upload d'une vidéo via `POST /api/documents`
    doit déboucher sur `IngestFromSegmentsAsync` (langue paramétrable, timestamps préservés), pas
    sur `FullText` + chunker texte. À défaut, court terme : retirer les extensions vidéo/audio de
    la page Documents et du validateur d'upload pour forcer le passage par la page Vidéo. *Impact 4 / M.*
31. **I-10 Chunking temporel** *(remonté du reliquat Lot 1)* : cible exprimée en caractères
    **utiles** (~1 200–1 500, hors balisage), **overlap de 1–2 segments** entre chunks, scission
    d'un segment surdimensionné avec interpolation des timestamps. *Impact 3 / S-M.*
32. **Whisper `medium`** : passer `ModelPath` de `ggml-small.bin` à `ggml-medium.bin` — meilleure
    matière première de transcription (config uniquement, au prix de la latence de transcription).
    *Impact 2-3 / S.*

> **Vérification avant/après (mini R-13, même règle que le Lot 0-bis)** : constituer ~5 questions
> « golden » sur une vidéo de référence du corpus et comparer citations et réponses avant/après
> I-21 — c'est le correctif dont l'effet doit être le plus visible.

### 🏅 Lot 3 — Qualité & performance de la restitution (5 à 8 jours)

15. ~~**R-5/R-6 Rerank & compression batchés**~~ → **remonté au Lot 0-bis (point 26)** pour le rerank ; la compression batchée reste ici, conditionnée au dépassement du budget tokens (R-11). *Impact 5 / M.*
16. ~~**R-9 Recherche hybride**~~ → **remontée au Lot 0-bis (point 27)**.
17. **R-2 Historique de conversation réel** : introduire `SessionId` dans les DTO chat, persister messages User/Assistant, charger l'historique. *Impact 4 / M.*
18. **R-11 Budget tokens** : tronquer/sélectionner le contexte selon la fenêtre du modèle (compter les tokens). *Impact 4 / M.*
19. **R-4 Garde-fou contexte vide** + **R-8 cache** (mémoire ou Redis) des réponses. *Impact 3 / M.*

### 🎖️ Lot 4 — Industrialisation (continu)

20. **I-16 Ingestion asynchrone** : câbler réellement l'Outbox + `BackgroundService` (statut `Processing → Completed/Failed`), libérer la requête HTTP. *Impact 5 / L.*
21. **R-12/R-14 Observabilité** : timings par étape (logs structurés / OpenTelemetry), suivi tokens. *Impact 3 / M.*
22. **R-13 Harnais d'évaluation** : jeu de questions « golden » + métriques recall@k et fidélité, pour mesurer chaque amélioration. *Impact 4 / L.*

---

## 5. Recommandation de séquencement

```
Semaine 1 : Lot 0 (quick wins critiques) ─────────────► ✅ LIVRÉ (28/06)
Semaine 2 : Lot 1 (robustesse ingestion) ─────────────► 🟨 ~80 % LIVRÉ (02/07)
Semaine 3 : Lot 0-bis (pertinence récupération) ──────► ✅ LIVRÉ (03/07) — corrige les sources hors sujet
Semaine 4 : Lot 2 (multi-format) ─────────────────────► ✅ LIVRÉ (05/07)
Semaine 5 : Lot 2-bis (restitution vidéo) ────────────► 🔜 PROCHAIN — corrige la restitution vidéo
Ensuite   : Lot 3 (restitution restante — R-2, R-11, R-4/R-8)
Continu   : Lot 4 (async, observabilité, évaluation) + reliquat Lot 1
```

**Trois actions à plus fort ratio impact/effort à faire en premier (révision 06/07)** :
1. **I-21** (timestamps hors du texte embeddé + ré-ingestion des vidéos) — restaure la similarité question/chunk sur les vidéos, comme R-15 l'avait fait pour les documents.
2. **I-22** (unification des chemins d'ingestion vidéo) — supprime la dépendance de la qualité au point d'entrée d'upload.
3. **I-10/point 31** (chunks temporels plus denses + overlap) — améliore le recall sur la parole, peu dense par nature.

---

## 6. Risques & dette technique signalés

- **Couplage dimension d'embedding ↔ schéma SQL** (`vector(768)`) : tout changement de provider casse l'`INSERT`. À rendre paramétrable/documenté.
- **Aucune authentification** (`UserId` codé en dur) : à traiter avant toute mise en ligne.
- **Couverture de tests en progression** : Lot 0 et Lot 1 sont venus avec des tests (chunker, pagination PDF, batching, extracteurs, prompts, défauts d'options). Le Lot 0-bis doit suivre la même règle (préfixes, troncature RRF, calibration).
- **Modèle LLM local 1B** : qualité limitée ; le harnais d'évaluation (R-13) permettra d'arbitrer un éventuel passage à un modèle plus capable. Le routage **Adaptive** dépend aussi de ce 1B : tant qu'il n'est pas évalué, préférer `Direct` par défaut est une option défendable.
- **Ré-ingestion obligatoire après R-15** (préfixes `nomic`) : les vecteurs stockés sans préfixe sont incompatibles avec des requêtes préfixées. Prévoir un vidage/ré-upload du corpus (ou un script de ré-ingestion) dans le même lot.
- **Ré-ingestion des vidéos obligatoire après I-21** : les vecteurs des chunks vidéo ont été calculés sur le texte pollué par les timestamps inline ; ils doivent être recalculés sur le texte épuré (même logique que R-15, limitée aux documents vidéo/audio).

> Lot 0-bis (points 23 à 28) et Lot 2 (points 12 à 14) livrés. Prochaine étape suggérée : **Lot 2-bis** (restitution vidéo, points 29 à 32) — répond au symptôme observé en usage réel — puis Lot 3 (restitution restante — R-2, R-11, R-4/R-8).

---

# ANNEXE — Détail de chaque point (mécanisme, impact, correctif)

> Pour chaque constat : le **mécanisme exact** en cause, **pourquoi** c'est un problème,
> et le **correctif** recommandé (avec code lorsqu'il clarifie).

## A. INGESTION

### A.1. Extraction de texte

#### I-1 — `HtmlTextExtractor` lève `NotImplementedException`

**Mécanisme.** Le `CompositeTextExtractor` sélectionne l'extracteur via `CanHandle(filePath)`, et `HtmlTextExtractor.CanHandle` répond `true` pour `.html`/`.htm`. Mais sa méthode `ExtractTextAsync` contient `throw new NotImplementedException();`. L'upload d'un `.html` ne tombe donc pas dans un fallback : il **lève une exception** qui remonte jusqu'au `catch` du controller → document marqué `Failed`.

**Pourquoi c'est grave.** Le format est *annoncé comme supporté* (le `CanHandle` le revendique), mais il casse à coup sûr. C'est pire qu'un format non géré, car l'extracteur « ment » sur sa capacité.

**Correctif.** Implémenter une extraction réelle (`AngleSharp` : parseur conforme WHATWG, retire scripts/styles, conserve la structure) :

```csharp
/// <summary>Extrait le texte visible d'un fichier HTML en supprimant balises, scripts et styles.</summary>
public async Task<string> ExtractTextAsync(string filePath, CancellationToken ct)
{
    var html = await File.ReadAllTextAsync(filePath, ct);
    var context = BrowsingContext.New(Configuration.Default);
    var document = await context.OpenAsync(req => req.Content(html), ct);

    // On retire les nœuds non textuels avant extraction
    foreach (var node in document.QuerySelectorAll("script, style, nav, footer"))
        node.Remove();

    // TextContent renvoie le texte concaténé ; on normalise les espaces multiples
    var text = document.Body?.TextContent ?? string.Empty;
    return System.Text.RegularExpressions.Regex.Replace(text, @"\s+\n", "\n").Trim();
}
```

À défaut, **au minimum ne plus crasher** (texte brut dépouillé des balises via regex). Mais la vraie solution est un parseur.

#### I-2 — Aucun extracteur DOCX / XLSX / PPTX / TXT / Markdown / CSV / JSON

**Mécanisme.** Le dossier `TextExtractor/` ne contient que PDF, HTML (cassé) et Vidéo. Tous les autres formats retombent sur le « rien » (cf. I-3).

**Pourquoi c'est grave.** L'objectif « RAG sur tous les types de document » n'est pas tenu. DOCX et TXT/MD sont les formats les plus courants en entreprise.

**Correctif.** Un extracteur par famille, chacun implémentant `ITextExtractor` et auto-enregistré dans le `CompositeTextExtractor` (le pattern est déjà en place — il suffit d'ajouter des classes) :

| Format | Bibliothèque | Note |
|--------|--------------|------|
| `.docx` | `DocumentFormat.OpenXml` | Parcourir `body.Descendants<Paragraph>()` |
| `.xlsx` / `.csv` | `ClosedXML` / `CsvHelper` | Sérialiser en texte **structuré** (`colonne: valeur`) plutôt qu'en CSV brut |
| `.pptx` | `OpenXml` | Concaténer le texte des `TextBody` par slide |
| `.txt` / `.md` | natif `File.ReadAllText` | Pour le `.md`, conserver les titres `#` comme `SectionTitle` |
| `.json` | `System.Text.Json` | Aplatir en `clé: valeur` lisibles |

```csharp
/// <summary>Extracteur pour les fichiers texte bruts et Markdown.</summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".md",  StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractTextAsync(string filePath, CancellationToken ct)
        => await File.ReadAllTextAsync(filePath, ct);
}
```

Enregistrement DI (chaque `ITextExtractor` est résolu en `IEnumerable<ITextExtractor>` par le composite) :

```csharp
services.AddScoped<ITextExtractor, PlainTextExtractor>();
services.AddScoped<ITextExtractor, DocxTextExtractor>();
// … etc.
```

#### I-3 — Échec d'extraction **silencieux**

**Mécanisme.** Dans `CompositeTextExtractor.ExtractTextAsync`, si aucun extracteur ne matche, on logue une erreur **mais on retourne `Task.FromResult(string.Empty)`**. L'ingestion continue : le chunker produit 0 chunk, l'upsert ne fait rien, et le controller appelle `doc.MarkAsCompleted()`.

**Pourquoi c'est grave.** L'utilisateur voit un document « Completed » qui est en réalité **vide et ininterrogeable**. Un échec déguisé en succès — le pire cas pour le debug.

**Correctif.** Lever une exception explicite et faire échouer proprement :

```csharp
var extractor = _textExtractors.FirstOrDefault(e => e.CanHandle(filePath));
if (extractor is null)
{
    _logger.LogError("Aucun extracteur ne gère le fichier : {File}", filePath);
    throw new NotSupportedException($"Format non supporté : {Path.GetExtension(filePath)}");
}
return extractor.ExtractTextAsync(filePath, cancellationToken);
```

Et dans `IngestionService`, après le chunking, garde-fou « 0 chunk » :

```csharp
if (textChunks.Count == 0)
    throw new InvalidOperationException(
        $"Aucun chunk produit pour le document {documentId} — extraction probablement vide.");
```

#### I-4 — PDF : pas d'OCR, pas de tableaux, pas de métadonnées

**Mécanisme.** `PdfTextExtractor` utilise `page.GetWords()`. Un PDF **scanné** (image sans couche texte) renvoie 0 mot → texte vide. Les tableaux sont aplatis en lignes désordonnées, et le titre/auteur/nb pages n'est jamais lu.

**Pourquoi c'est grave.** Beaucoup de documents réels (contrats scannés, factures) sont des images. Ils s'ingèrent « avec succès » mais sans contenu (cf. I-3).

**Correctif.**
1. **Détecter le PDF image** : si `page.GetWords().Count == 0` sur toutes les pages → OCR.
2. **OCR de repli** via `Tesseract` (rendre chaque page en image, puis OCR).
3. **Métadonnées** : `pdf.Information.Title`, `.Author`, `pdf.NumberOfPages` → alimenter `DocumentMetadata`.

```csharp
var words = page.GetWords().ToList();
if (words.Count == 0)
{
    // Page sans couche texte → OCR de repli (Tesseract)
    var pageText = await _ocrService.RecognizeAsync(page, ct);
    sb.AppendLine(pageText);
    continue;
}
```

> L'OCR est un Lot 2 (effort L). À court terme, au moins **logguer un avertissement** « PDF sans texte extractible ».

#### I-5 — Numéro de page perdu (PDF)

**Mécanisme.** `PdfTextExtractor` concatène **toutes les pages** dans un seul `StringBuilder` séparé par `\n\n`, puis renvoie une string unique. Le `RecursiveChunker` reçoit ce bloc et ne sait plus à quelle page appartient chaque passage. Résultat : `TextChunk.PageNumber` est **toujours `null`**.

**Pourquoi c'est grave.** La citation `[SOURCE: NomDoc, p.X]` exigée par le prompt système ne peut jamais contenir de page.

**Correctif.** Préserver la frontière de page, puis chunker **par page** :

```csharp
/// <summary>Extraction page par page, chaque entrée portant son numéro de page.</summary>
public IReadOnlyList<(int PageNumber, string Text)> ExtractPages(string filePath) { … }
```

On chunke ensuite chaque page indépendamment en propageant `PageNumber` dans le `TextChunk`. Règle I-5 **et** alimente I-8.

#### I-6 — Langue codée en dur (`'french'` / `"fr"`)

**Mécanisme.** Trois endroits figent le français : `to_tsvector('french', …)`/`plainto_tsquery('french', …)` dans `PgVectorStoreService` ; Whisper `"fr"` par défaut dans `VideoTextExtractor` ; `language = "fr"` par défaut des métadonnées.

**Pourquoi c'est grave.** Un document anglais indexé avec le dictionnaire `french` a un *stemming* faux → full-text dégradé. Whisper transcrit l'anglais en « français phonétique ».

**Correctif.** Détecter la langue (`LanguageDetection`, ou `result.Language` que Whisper renvoie déjà), la stocker dans `metadata_language`, et la passer à `to_tsvector(<langue>, …)` (mapping ISO → dictionnaire : `fr→french`, `en→english`…).

### A.2. Chunking

#### I-7 — Le `RecursiveChunker` n'est pas récursif

**Mécanisme.** Son commentaire annonce « par paragraphes, **puis par phrases** si dépassement ». En réalité il split par `\n\n` puis accumule jusqu'à `MaxChunkSize`, **sans aucun repli phrase**. Un paragraphe de 5 000 caractères (PDF sans `\n\n`) devient **un seul chunk de 5 000 caractères**.

**Pourquoi c'est grave.** Un chunk énorme produit un embedding « moyenné » ne ressemblant à aucune question précise → score cosinus médiocre, recall en chute, et explosion du budget tokens (R-11).

**Correctif.** Vrai découpage hiérarchique : paragraphe → phrase (`. ! ?`) → mot, en redescendant uniquement quand le niveau courant dépasse la cible.

```csharp
/// <summary>Découpe récursive : tente le séparateur le plus large, redescend si un fragment dépasse la cible.</summary>
private static readonly string[] Separators = ["\n\n", "\n", ". ", " "];

private IEnumerable<string> Split(string text, int sepIndex)
{
    if (text.Length <= MaxChunkSize || sepIndex >= Separators.Length)
    {
        yield return text;
        yield break;
    }
    foreach (var part in text.Split(Separators[sepIndex]))
        foreach (var sub in Split(part, sepIndex + 1)) // redescend d'un niveau
            yield return sub;
}
```

#### I-8 — `SectionTitle` et `PageNumber` jamais renseignés

**Mécanisme.** `RecursiveChunker.Chunk` ne crée que `new TextChunk { Content = … }`. `SectionTitle` et `PageNumber` ne sont **jamais** affectés. Tout en aval (`IngestionService` → `DocumentChunk` → citation) propage `null`/`""`.

**Pourquoi c'est grave.** Cause racine de la « citation enrichie » vide. Step 3 a ajouté `SectionTitle` dans le DTO, le schéma, l'UI… mais la donnée n'existe jamais à la source.

**Correctif.** Lier le chunking à la structure : PDF → propager la page (I-5) ; Markdown/DOCX → détecter les titres (`#`, styles `Heading`) et les attacher comme `SectionTitle` jusqu'au titre suivant.

```csharp
// Pseudo : on mémorise le dernier titre rencontré
string currentSection = string.Empty;
if (IsHeading(paragraph)) currentSection = paragraph.Trim();
else chunks.Add(new TextChunk { Content = …, SectionTitle = currentSection, PageNumber = page });
```

#### I-9 — Overlap en caractères, taille en caractères (pas en tokens)

**Mécanisme.** `OverlapSize = 100` caractères pris via `current.ToString()[^100..]` → **coupe au milieu d'un mot**. `MaxChunkSize = 800` est en **caractères**, alors que les modèles d'embedding raisonnent en **tokens**.

**Pourquoi c'est grave.** Un overlap débutant par « …ation budgétaire » (mot tronqué) pollue le chunk suivant. 800 caractères ≈ 200-400 tokens selon la langue : pour un modèle à fenêtre courte, des chunks denses peuvent être tronqués **silencieusement** par le générateur d'embeddings.

**Correctif.** Compter en **tokens** (`Microsoft.ML.Tokenizers` / `SharpToken`) et overlap **sur frontière de phrase/mot** :

```csharp
// Overlap propre : on repart de la dernière frontière de phrase dans la fenêtre d'overlap
var tail = current.ToString();
var cut = tail.LastIndexOf(". ", Math.Max(0, tail.Length - OverlapSize), StringComparison.Ordinal);
var overlap = cut > 0 ? tail[(cut + 2)..] : tail[^Math.Min(OverlapSize, tail.Length)..];
```

#### I-10 — `TemporalChunker` : segment surdimensionné, pas d'overlap

**Mécanisme.** Si un segment Whisper unique dépasse `maxCharsPerChunk` (800), il est ajouté **entier** (la condition de flush ne se déclenche que `buffer.Count > 0` *avant* l'ajout). Aucun overlap entre chunks temporels.

**Pourquoi c'est grave.** Cas limite (orateur parlant longtemps sans pause). L'absence d'overlap réduit le recall aux frontières (phrase coupée entre deux chunks).

**Correctif.** Si une ligne dépasse la cible, la scinder par phrases en conservant `Start`/`End` interpolés. Overlap temporel optionnel (1 segment suffit).

### A.3. Embeddings

#### I-11 — `EmbedBatchAsync` envoie **tous** les chunks en un appel

**Mécanisme.** `embeddingGenerator.GenerateAsync(texts)` reçoit l'`IEnumerable` complet. Pour un gros document (milliers de chunks), c'est **une seule requête HTTP géante**.

**Pourquoi c'est grave.** Les providers (OpenAI, LM Studio, Ollama) limitent le nombre d'entrées et la taille du payload. Au-delà → 400/413/timeout → **toute l'ingestion échoue**. Blocage n°1 pour les gros docs.

**Correctif.** Sous-batcher (ex. 96 textes/appel) :

```csharp
public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
{
    const int BatchSize = 96;                         // limite raisonnable, à exposer en option
    var all = texts.ToList();
    var result = new List<float[]>(all.Count);

    foreach (var batch in all.Chunk(BatchSize))       // LINQ Chunk découpe en sous-listes
    {
        var embeddings = await embeddingGenerator.GenerateAsync(batch, cancellationToken: ct);
        result.AddRange(embeddings.Select(e => e.Vector.ToArray()));
    }
    return result;
}
```

#### I-12 — Aucune résilience (retry/backoff)

**Mécanisme.** Aucun `Polly`/`ResiliencePipeline` autour des appels embeddings et LLM. Une erreur 429/503 transitoire avorte l'opération entière.

**Pourquoi c'est grave.** Les LLM locaux (LM Studio/Ollama) renvoient régulièrement des erreurs sous charge. Sans retry, l'ingestion d'un gros document a une probabilité d'échec cumulée élevée.

**Correctif.** `Microsoft.Extensions.Http.Resilience` ou `Polly` (retry exponentiel + jitter sur erreurs transitoires), branché sur le `HttpClient` du provider IA, centralisé dans la DI.

#### I-13 — Dimension d'embedding non validée vs colonne SQL

**Mécanisme.** `init.sql` déclare `embedding vector(768)`. Le code stocke `embeddings[i].Length` dans `EmbeddingDimensions` mais ne **vérifie pas** que cette longueur vaut 768. Changer de modèle (ex. 3072 dims) fait échouer l'`INSERT` avec une erreur Postgres cryptique.

**Pourquoi c'est grave.** Couplage caché entre config IA et schéma SQL : un changement de provider compile mais casse au premier upsert.

**Correctif.** Valider à l'ingestion (`if (embedding.Length != _expectedDims) throw …`) et rendre la dimension **configurable** (`AiProviderOptions.EmbeddingDimensions`).

#### I-14 — Aucune idempotence (doublons)

**Mécanisme.** Ré-uploader le même fichier crée un nouveau `Document` (nouveau GUID) et **réinsère tous les chunks**. Aucune détection de contenu identique.

**Pourquoi c'est grave.** L'index se remplit de doublons → la recherche renvoie 3 fois le même passage, gaspille le budget tokens et fausse le RRF.

**Correctif.** Hash de contenu (SHA-256 du fichier) stocké sur `documents` ; avant ingestion, vérifier l'existence et proposer « déjà ingéré » ou un remplacement (delete + reinsert).

### A.4. Persistance & schéma

#### I-15 — `UpsertBatchAsync` = N `INSERT` unitaires

**Mécanisme.** La méthode ouvre **une** transaction (bien) mais boucle `await UpsertAsync(...)` qui exécute un `INSERT` paramétré par chunk → N allers-retours réseau dans la transaction.

**Pourquoi c'est grave.** Pour 2 000 chunks, 2 000 round-trips. Sur une base distante, ingestion très lente.

**Correctif.** Import binaire Npgsql (`NpgsqlBinaryImporter` / `COPY`), 1 à 2 ordres de grandeur plus rapide :

```csharp
// COPY binaire : un seul flux pour tous les chunks
await using var importer = await conn.BeginBinaryImportAsync(
    "COPY document_chunks (id, document_id, content, chunk_index, embedding, …) FROM STDIN (FORMAT BINARY)", ct);
foreach (var (chunk, emb) in items)
{
    await importer.StartRowAsync(ct);
    await importer.WriteAsync(chunk.Id, ct);
    // … colonnes …
    await importer.WriteAsync(new Vector(emb), ct);   // pgvector Npgsql
}
await importer.CompleteAsync(ct);
```

> Pour de l'upsert : alternative `INSERT … ON CONFLICT` multi-lignes (un seul `INSERT` avec N tuples).

#### I-16 — Ingestion 100 % synchrone dans la requête HTTP

**Mécanisme.** `DocumentsController.Upload` fait `await ingestionService.IngestAsync(...)` **dans la requête**. L'Outbox/BackgroundService est *documenté* (`UploadDocumentHandler` parle d'`OutboxWorker`) mais **n'existe pas** : le handler ne fait que persister le document.

**Pourquoi c'est grave.** Un PDF de 300 pages ou une vidéo de 1 h bloque la requête pendant des minutes → timeout proxy/navigateur, mauvaise UX, pas de scalabilité, pas de reprise sur erreur.

**Correctif (architecture cible).**
1. `Upload` persiste le document en `Pending` et **retourne immédiatement** `202 Accepted`.
2. Un `BackgroundService` (`IngestionWorker`) consomme une file (Channel en mémoire, ou table `outbox_messages` déjà au schéma) et exécute l'ingestion hors requête.
3. Statut `Processing → Completed/Failed` ; le front poll `GET /api/documents/{id}`.

Seul vrai changement structurel (Lot 4), mais il débloque la production.

#### I-17 — Pas d'index full-text GIN

**Mécanisme.** `init.sql` ne crée que des index btree. `SearchFullTextAsync` calcule `to_tsvector('french', dc.content)` **à la volée pour chaque ligne, à chaque requête**, et `@@ plainto_tsquery`.

**Pourquoi c'est grave.** Sans index GIN, **scan séquentiel** + recalcul du tsvector sur toute la table à chaque recherche « Classique ». Inutilisable à l'échelle.

**Correctif.** Colonne `tsvector` générée + index GIN, et cibler cette colonne dans la requête :

```sql
ALTER TABLE document_chunks
  ADD COLUMN content_tsv tsvector
  GENERATED ALWAYS AS (to_tsvector('french', content)) STORED;

CREATE INDEX ix_document_chunks_content_tsv ON document_chunks USING gin (content_tsv);
```

```sql
-- Requête : on cible la colonne indexée
WHERE content_tsv @@ plainto_tsquery('french', @query)
ORDER BY ts_rank(content_tsv, plainto_tsquery('french', @query)) DESC
```

#### I-18 — `init.sql` désaligné du modèle EF Core

**Mécanisme.** `init.sql` ne contient **ni** `start_time_seconds`/`end_time_seconds` (appliquées via `migrate-temporal-chunks.sql` à la main), **ni** l'index full-text (I-17). Le commentaire évoque `vector(3072)` alors que la colonne est `vector(768)`.

**Pourquoi c'est grave.** Une base recréée à partir de `init.sql` seul est **incompatible** avec le code (colonnes manquantes) → erreurs SQL au runtime. Reproductibilité cassée.

**Correctif.** Faire de `init.sql` la **source de vérité unique** : y intégrer colonnes temporelles, index GIN, et clarifier/paramétrer la dimension. À terme, envisager les migrations EF Core.

### A.5. Vidéo

#### I-19 — Bug de cache de langue Whisper

**Mécanisme.** `WhisperTranscriptionService` est Singleton. `GetOrCreateProcessorAsync(language)` construit le `WhisperProcessor` avec `.WithLanguage(language)` **au premier appel** et le met en cache dans `_processor`. Les appels suivants font `if (_processor is not null) return _processor;` — la **langue passée est ignorée**.

**Pourquoi c'est grave.** Première vidéo en `"fr"` → toutes les suivantes sont transcrites en français, même si on demande `"en"`. Multilingue cassé silencieusement.

**Correctif.** Cache **par langue** (le `WhisperFactory` restant partagé) :

```csharp
// Le modèle (factory) est chargé une fois ; le processeur est mis en cache par langue
private readonly ConcurrentDictionary<string, WhisperProcessor> _processors = new();

private WhisperProcessor GetProcessor(string language)
    => _processors.GetOrAdd(language, lang =>
        _factory!.CreateBuilder().WithLanguage(lang).WithThreads(_options.Threads).Build());
```

(en gardant l'init thread-safe du `_factory` via le `SemaphoreSlim` existant).

#### I-20 — Pas de VAD ni découpage des vidéos longues

**Mécanisme.** Tout le fichier audio est passé à Whisper en une passe (`processor.ProcessAsync(fileStream)`), sans détection d'activité vocale ni segmentation préalable.

**Pourquoi c'est grave.** Sur une vidéo de plusieurs heures : mémoire élevée, aucune progression, et un échec en fin de traitement perd tout.

**Correctif (Lot 2/4).** Découper l'audio en fenêtres (ex. 10 min) via FFmpeg, transcrire par fenêtre avec décalage de timestamps, remonter la progression. Optionnellement, VAD pour sauter les silences.

#### I-21 — Timestamps inline embeddés *(nouveau — révision 06/07)*

**Mécanisme.** `TemporalChunker.FormatLine` produit `[HH:MM:SS → HH:MM:SS] texte` pour **chaque segment Whisper**, et `BuildChunk` concatène ces lignes dans `TextChunk.Content`. `IngestFromSegmentsAsync` embed ensuite ce `Content` tel quel :

```csharp
// TemporalChunker.cs — le préfixe de ~24 caractères est incrusté dans le contenu
private static string FormatLine(TranscriptionSegment segment)
    => $"[{segment.Start:hh\\:mm\\:ss} → {segment.End:hh\\:mm\\:ss}] {segment.Text.Trim()}\n";

// IngestionService.cs — c'est ce contenu formaté qui part à l'embedding
var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);
```

Un segment Whisper fait typiquement 40–80 caractères de parole : le préfixe représente donc **25 à 35 % du chunk**. De plus, la limite `maxCharsPerChunk = 800` compte ce balisage — le contenu *utile* d'un chunk vidéo est ~500–600 caractères, contre 800 pour un PDF.

**Pourquoi c'est grave.** Pour `nomic-embed-text`, ces préfixes sont des tokens numériques répétitifs sans valeur sémantique : le vecteur du chunk est « moyenné » vers du bruit, la similarité avec une vraie question chute. C'est la cause structurelle n°1 de la restitution vidéo médiocre — l'équivalent vidéo du R-15. Effet secondaire : les extraits de citations (`Content[..350]`) affichent ce bruit dans l'UI.

**L'ironie.** Les timestamps sont **déjà** portés proprement par `TextChunk.StartTime`/`EndTime`, persistés en base (`start_time_seconds`/`end_time_seconds`) et propagés jusqu'aux `Citation` — la version inline est redondante pour la restitution.

**Correctif.**
1. `TemporalChunker` : `Content` = texte pur des segments (concaténation simple), `StartTime`/`EndTime` inchangés.
2. Pour que le LLM puisse citer un horodatage, l'injecter dans l'**en-tête d'extrait** de `BuildChatHistoryAsync`, là où page/section sont déjà injectés :

```csharp
// RagPipelineService.BuildChatHistoryAsync — plage temporelle en en-tête, pas dans le corps
var time = chunk.StartTime is { } st && chunk.EndTime is { } et
    ? $", {st:hh\\:mm\\:ss}–{et:hh\\:mm\\:ss}"
    : string.Empty;
contextBuilder.AppendLine($"[Extrait {i}] Source: {source}{page}{section}{time}");
```

3. ⚠️ **Ré-ingérer les documents vidéo/audio** (les vecteurs stockés ont été calculés sur le texte pollué).

#### I-22 — Double chemin d'ingestion vidéo incohérent *(nouveau — révision 06/07)*

**Mécanisme.** Deux points d'entrée ingèrent une vidéo, avec deux qualités très différentes :

| | Page Vidéo (`POST /api/video/transcribe`) | Page Documents (`POST /api/documents`) |
|---|---|---|
| Chunking | `TemporalChunker` (frontières de segments) | `RecursiveChunker` texte (coupes arbitraires au milieu des lignes horodatées) |
| Timestamps par chunk | ✅ `StartTime`/`EndTime` | ❌ `null` → citations sans repère temporel |
| Langue Whisper | ✅ paramétrable (`language` de la requête) | ❌ figée à `"fr"` (`VideoTextExtractor.ExtractTextAsync`) |
| Texte embeddé | pollué par I-21 | pollué par I-21 **et** découpé n'importe où |

La page Documents accepte pourtant `.mp4/.mkv/.webm/.avi/.mov` + audio (`DocumentsPage.tsx`, `UploadDocumentValidator`) : rien n'oriente l'utilisateur vers le bon chemin.

**Pourquoi c'est grave.** La qualité de restitution dépend silencieusement de la page utilisée pour uploader. Une vidéo passée par la page Documents est ininterrogeable correctement (chunks sans timestamps, transcription potentiellement dans la mauvaise langue) sans qu'aucune erreur ne le signale.

**Correctif.**
- **Cible** : dans le flux d'upload documents, détecter les extensions vidéo/audio et router vers le pipeline segments (`TranscribeVideoCommand`/`IngestFromSegmentsAsync`) avec langue paramétrable — un seul pipeline vidéo, quel que soit le point d'entrée.
- **Court terme** (si le routage est reporté) : retirer les extensions vidéo/audio de `DocumentsPage.tsx` et du `UploadDocumentValidator`, et afficher un message orientant vers la page Vidéo.

## B. RESTITUTION (RAG / LLM)

### B.1. Bugs critiques de qualité

#### R-1 — Le contexte LLM contient le **GUID**, pas le nom du document

**Mécanisme.** Dans `BuildChatHistoryAsync` :
```csharp
contextBuilder.AppendLine($"[Extrait {i}] Document: {chunk.DocumentId}, Page: {chunk.PageNumber}");
```
On injecte `chunk.DocumentId` (un `Guid`). Or le prompt système exige `[SOURCE: NomDocument, p.X]`. Le modèle n'a **jamais** le nom — `chunk.DocumentName` existe pourtant (renseigné par `SearchAsync`).

**Pourquoi c'est grave.** Le LLM doit citer un nom qu'il ne connaît pas → il recopie le GUID ou **invente** un nom. La citation textuelle est systématiquement fausse, alors que le panneau « Citations » (issu de `chunk.DocumentName`) est correct. Incohérence visible.

**Correctif (1 ligne, impact maximal).**
```csharp
var source = chunk.DocumentName ?? "Document inconnu";
var section = string.IsNullOrWhiteSpace(chunk.SectionTitle) ? "" : $", Section: {chunk.SectionTitle}";
var page = chunk.PageNumber is { } p ? $", p.{p}" : "";
contextBuilder.AppendLine($"[Extrait {i}] Source: {source}{page}{section}");
```

#### R-2 — Historique de conversation mort

**Mécanisme.** `RagPipelineService` gère l'historique (`if (query.IncludeHistory && query.SessionId != Guid.Empty)`), mais `ChatController.Ask`/`AskStream` **ne renseignent jamais `SessionId`** dans le `RagQuery`, et ne **persistent jamais** les messages. `SessionId` reste `Guid.Empty` → la branche historique n'est jamais prise. Tables `conversation_sessions`/`chat_messages` inertes côté API.

**Pourquoi c'est grave.** Le chat est de facto **sans mémoire** : pas de question de suivi (« et dans ce cas ? »). Fonctionnalité présente mais inaccessible.

**Correctif.**
1. Ajouter `SessionId` (optionnel) à `AskQuestionRequest` ; le créer si absent et le **renvoyer** au front.
2. Après réponse, persister le message User puis Assistant via `IConversationRepository`.
3. Transmettre `SessionId` au `RagQuery`.

```csharp
var sessionId = request.SessionId ?? await _conversations.CreateSessionAsync(userId, ct);
await _conversations.AddMessageAsync(sessionId, MessageRole.User, request.Question, ct);
var rag = await _pipeline.AskAsync(new RagQuery { …, SessionId = sessionId, IncludeHistory = true }, ct);
await _conversations.AddMessageAsync(sessionId, MessageRole.Assistant, rag.Answer, ct);
```

#### R-3 — Prompts système = restes de démo contradictoires

**Mécanisme.** `RagPrompts.RagSystem` = « assistant expert dans la finance » ; `RagPrompts.DirectLlmSystem` = « assistant expert dans le voyage ». Deux domaines incohérents, codés en dur.

**Pourquoi c'est grave.** Sur un corpus non-financier, le RAG s'auto-biaise « finance » ; le mode LLM direct parle voyage. Réponses orientées sans raison.

**Correctif.** Prompts **neutres** et **configurables** via `RagOptions` (déjà exposés au front par `GET /api/chat/system-prompts`, le mécanisme d'override existe) :

```text
Tu es un assistant IA factuel. Réponds uniquement à partir du contexte fourni.
Si l'information n'y figure pas, dis-le explicitement. Cite tes sources [SOURCE: NomDocument, p.X].
Réponds en français, de façon précise et structurée.
```

#### R-4 — Pas de garde-fou « contexte vide »

**Mécanisme.** Si `RetrieveChunksAsync` (puis rerank/compression) renvoie 0 chunk, le pipeline appelle quand même le LLM avec un `{context}` vide.

**Pourquoi c'est grave.** Avec un contexte vide, le system prompt « réponds à partir des extraits » est contradictoire → le modèle hallucine ou répond hors-sol.

**Correctif.** Court-circuiter proprement :
```csharp
if (rankedChunks.Count == 0)
    return new RagResponse {
        Answer = "Aucun document pertinent n'a été trouvé pour répondre à cette question.",
        Citations = [], StrategyUsed = strategy, DurationMs = sw.ElapsedMilliseconds };
```

### B.2. Performance de la restitution

#### R-5 — Reranker LLM = N appels séquentiels, activé par défaut

**Mécanisme.** `LlmRerankerService.RerankAsync` fait une **boucle `foreach`** sur les chunks, avec un `await chatClient.GetResponseAsync` par chunk (jusqu'à `TopK = 20`). `RerankerOptions.Enabled = true` par défaut.

**Pourquoi c'est grave.** 20 appels LLM **avant de commencer à répondre**. Sur un modèle local, des dizaines de secondes de latence pure, pour un gain faible avec un modèle 1B (qui note mal la pertinence).

**Correctif.**
- **Court terme** : `Enabled = false` par défaut.
- **Moyen terme** : **un seul appel** notant tous les chunks (JSON `{id: score}`), ou un vrai **cross-encoder** dédié (type `bge-reranker`).

```text
Note la pertinence de chaque extrait (0-10) pour la question. Réponds en JSON: {"1":7,"2":3,...}.
Question: {question}
Extraits:
[1] {chunk1}
[2] {chunk2}
…
```

#### R-6 — Compression contextuelle = N appels séquentiels, activée par défaut

**Mécanisme.** `ContextCompressorService.CompressAsync` boucle sur les chunks et fait **un appel LLM par chunk** survivant. `ContextCompressionOptions.Enabled = true` par défaut.

**Pourquoi c'est grave.** Cumulée à R-5, une **seule question RAG** déclenche ~20 (rerank) + ~5 (compression) + 1 (réponse) ≈ **26 appels LLM séquentiels**. Cause n°1 de la lenteur perçue.

**Correctif.**
- **Court terme** : désactiver par défaut.
- **Moyen terme** : compression **en un appel**, ou la réserver aux contextes qui dépassent le budget tokens (déclencheur conditionnel R-11).

#### R-7 — Fusion : embeddings + recherches séquentiels

**Mécanisme.** La stratégie Fusion génère N reformulations puis, pour chacune, embed + `SearchAsync` **en séquence**. Le commentaire explique : Npgsql ne supporte pas plusieurs `DataReader` simultanés sur la **même** connexion (`AppDbContext` Scoped).

**Pourquoi c'est grave.** Latence additive (4 embeddings + 4 recherches en file). Le `Task.WhenAll` est impossible *sur la même connexion*.

**Correctif.** Paralléliser avec des **connexions distinctes** (`NpgsqlDataSource`, une connexion par tâche) — la limitation est par-connexion :

```csharp
var tasks = allQueries.Select(async q =>
{
    await using var conn = await _dataSource.OpenConnectionAsync(ct); // connexion dédiée
    var vector = await embeddingService.EmbedAsync(q, ct);
    return await SearchOnConnectionAsync(conn, vector, …, ct);
});
var allResults = await Task.WhenAll(tasks);
```

#### R-8 — Pas de cache malgré `CacheOptions`

**Mécanisme.** `CacheOptions` (Enabled, TtlMinutes) existe mais aucun code ne l'utilise. Chaque question identique rejoue tout le pipeline (embeddings + recherche + LLM).

**Pourquoi c'est grave.** En démo/usage répété, on paie plusieurs fois le même calcul coûteux.

**Correctif.** Cache clé = `hash(question + documentIds + strategy + mode)`, valeur = `RagResponse`. `IMemoryCache` d'abord, Redis (`IDistributedCache`) ensuite. Invalidation à l'ingestion/suppression.

### B.3. Pertinence de la récupération

#### R-9 — Pas de recherche hybride

**Mécanisme.** Le pipeline fait du **vectoriel** (modes RAG) **ou** du **full-text** (mode Classique), jamais les deux ensemble. Pourtant `SearchFullTextAsync` et `ReciprocalRankFusion.Fuse` existent déjà.

**Pourquoi c'est grave.** Le vectoriel rate les correspondances **exactes/rares** (références légales, codes produits, acronymes) ; le full-text rate les **synonymes/paraphrases**. L'état de l'art combine les deux. Briques présentes, non assemblées.

**Correctif.** Lancer vectoriel **et** full-text, fusionner via le RRF existant :

```csharp
var vectorHits = await vectorStoreService.SearchAsync(vector, topK, docIds, threshold, ct);
var textHits   = await vectorStoreService.SearchFullTextAsync(query.Question, topK, docIds, ct);
var fused      = ReciprocalRankFusion.Fuse([vectorHits, textHits]); // déjà implémenté
```

#### R-10 — `ef_search` non réglé, `ScoreThreshold` arbitraire

**Mécanisme.** L'index HNSW est créé avec `m=16, ef_construction=64`, mais `hnsw.ef_search` (qualité de recherche au runtime) n'est jamais positionné. `ScoreThreshold = 0.3` est appliqué **dans le SQL** (`WHERE 1 - (… <=> …) >= @threshold`).

**Pourquoi c'est grave.** Un `ef_search` trop bas plafonne le recall malgré `TopK=20`. Un seuil 0.3 fixe est mal calibré selon le modèle (`nomic-embed` ≠ OpenAI) : trop haut → on jette du pertinent ; trop bas → bruit.

**Correctif.** `SET hnsw.ef_search = 100;` (≥ `TopK`) ; rendre le seuil **configurable** et, mieux, filtrer **après reranking** (laisser le rerank/RRF trancher) plutôt que dans le SQL.

#### R-11 — Aucune gestion de budget tokens

**Mécanisme.** `BuildChatHistoryAsync` concatène tous les `contextChunks` sans compter les tokens. `MaxOutputTokens = 2000` borne la **sortie**, jamais l'**entrée**. Avec `TopK=20` chunks non compressés (jusqu'à 800 car.) + historique, on peut dépasser la fenêtre du modèle local (~4096).

**Pourquoi c'est grave.** Au-delà de la fenêtre, le serveur LLM **tronque silencieusement** le début du prompt — souvent le system prompt ou les premiers extraits → réponse incohérente, citations perdues.

**Correctif.** Construire le contexte sous **budget** : compter les tokens, inclure les chunks par pertinence jusqu'à un plafond (ex. 60 % de la fenêtre), réserver la place pour l'historique et la sortie.

```csharp
var budget = _modelContextTokens - reservedForOutput - historyTokens;
foreach (var chunk in rankedChunks) {
    var t = _tokenizer.CountTokens(chunk.Content);
    if (used + t > budget) break;   // on s'arrête avant de déborder
    Append(chunk); used += t;
}
```

### B.4. Observabilité & qualité transverse

#### R-12 — Pas de mesure par étape

**Mécanisme.** Seul le `Stopwatch` global (`DurationMs`) est mesuré. On ne sait pas si la latence vient du rerank, de la compression ou du LLM.

**Pourquoi c'est grave.** Impossible de diagnostiquer/optimiser objectivement (ex. confirmer que R-5/R-6 dominent).

**Correctif.** Logs structurés / `Activity` (OpenTelemetry) par étape : `retrieval_ms`, `rerank_ms`, `compress_ms`, `llm_ms`, nb chunks, nb appels LLM. Exposer en mode debug dans `RagResponse`.

#### R-13 — Pas de harnais d'évaluation

**Mécanisme.** Aucun moyen de mesurer la qualité (recall@k de la récupération, fidélité/exactitude des réponses). Améliorations « au ressenti ».

**Pourquoi c'est grave.** On ne peut pas prouver qu'un changement (hybride, nouveau chunker) améliore, ni détecter une régression.

**Correctif.** Constituer un petit **jeu « golden »** (questions → passages/réponses attendus) et un projet de test calculant recall@k et un score de fidélité (LLM-as-judge ou correspondance), branché sur les Lots 1/3.

#### R-14 — `TotalTokens = 0` en streaming

**Mécanisme.** En mode streaming, `RagResponse.TotalTokens` est forcé à 0 (les updates de streaming n'agrègent pas l'usage).

**Pourquoi c'est grave.** Pas de suivi de consommation en mode streaming (le plus utilisé côté UI).

**Correctif.** Agréger l'usage si le provider le fournit dans le dernier update, ou estimer via le tokenizer (entrée + sortie accumulée).

## B.5. Nouveaux constats — révision 2026-07-02 (pertinence de la récupération)

> Issus du diagnostic du symptôme observé en usage réel : *des sources sans rapport avec la
> question remontent dans le panneau Citations, à côté des bonnes sources du PDF.*

#### R-15 — Préfixes de tâche `nomic` absents (ingestion **et** requête)

**Mécanisme.** `nomic-embed-text-v1.5` est un modèle d'embedding **asymétrique** : il a été entraîné
avec des préfixes de tâche obligatoires — `search_document: <texte>` pour les passages indexés,
`search_query: <question>` pour les requêtes. `OpenAIEmbeddingService.EmbedAsync`/`EmbedBatchAsync`
envoient le texte **brut** des deux côtés, et LM Studio n'ajoute aucun préfixe automatiquement.

**Pourquoi c'est grave.** Sans préfixes, les embeddings ne sont pas dans le régime pour lequel le
modèle est calibré : les scores se compressent et la **séparation pertinent/non-pertinent s'effondre**.
C'est le candidat n°1 pour expliquer que des chunks hors sujet obtiennent des scores comparables
aux chunks pertinents.

**Correctif.** Préfixer selon l'usage, piloté par la configuration (aucun préfixe pour OpenAI
`text-embedding-3-*`, qui est symétrique) :

```csharp
/// <summary>Préfixes de tâche exigés par certains modèles asymétriques (nomic, e5…).</summary>
public sealed class EmbeddingPrefixOptions
{
    public string DocumentPrefix { get; set; } = "";   // ex. "search_document: "
    public string QueryPrefix    { get; set; } = "";   // ex. "search_query: "
}
```

```csharp
// Ingestion (EmbedBatchAsync) : texte de passage
var input = _prefixes.DocumentPrefix + text;
// Restitution (EmbedAsync appelé avec la question) : texte de requête
var input = _prefixes.QueryPrefix + text;
```

> ⚠️ Nécessite de distinguer les deux usages dans `IEmbeddingService` (ex. `EmbedQueryAsync` vs
> `EmbedDocumentsAsync`) car aujourd'hui `EmbedAsync` sert aux deux (question directe, doc HyDE).
> ⚠️ **Ré-ingestion du corpus obligatoire** : les vecteurs déjà stockés (sans préfixe) ne sont pas
> comparables à des requêtes préfixées.

#### R-16 — Fusion : résultat RRF non tronqué, scores incomparables

**Mécanisme.** Dans `RetrieveChunksAsync` (cas `RagStrategy.Fusion`), le retour est
`ReciprocalRankFusion.Fuse(allResults)` **sans troncature**. `Fuse` renvoie **l'union** de tous les
chunks uniques des N listes (jusqu'à 4 requêtes × TopK = 40 chunks). De plus, les scores retournés
sont des scores **RRF** (~0.016 à 0.065), pas des similarités cosinus : le `ScoreThreshold` ne
s'applique plus, et l'UI affiche ces valeurs minuscules comme si c'étaient des similarités.

**Pourquoi c'est grave.** En stratégie Fusion (choisie par le routeur Adaptive sur les questions
comparatives), le contexte explose (aggrave R-11), la latence augmente, et le panneau Citations
affiche des dizaines de sources au score illisible — dont beaucoup de bruit.

**Correctif (2 lignes).**

```csharp
// Fusion des listes via Reciprocal Rank Fusion, tronquée au TopK configuré
return ReciprocalRankFusion.Fuse(allResults)
    .Take(opts.Retrieval.TopK)
    .ToList();
```

Et côté restitution, distinguer la nature du score dans la citation (cosinus vs RRF) ou re-normaliser
avant affichage.

#### R-17 — Flags `Enabled` de la configuration jamais honorés ✅ livré (03/07)

**Mécanisme.** `HydeOptions.Enabled` et `MultiQueryOptions.Enabled` existent mais ne sont lus
**nulle part** : le routeur Adaptive peut sélectionner HyDE ou Fusion même si l'opérateur les a
désactivés dans `appsettings.json`. `CacheOptions.Enabled = true` par défaut alors qu'aucun code de
cache n'existe (R-8).

**Pourquoi c'est grave.** La configuration ment : on croit pouvoir couper HyDE/Fusion (utile quand
le modèle 1B génère des documents hypothétiques qui dérivent — pollution de la récupération) mais
le réglage est sans effet. Un opérateur qui active « le cache » n'obtient rien.

**Correctif.** Dans la résolution de stratégie : si la stratégie résolue est désactivée, repli sur
`Direct` (+ log). Passer `Cache.Enabled = false` par défaut tant que R-8 n'est pas implémenté.

```csharp
// Repli si la stratégie résolue est désactivée en configuration
if (strategy == RagStrategy.HyDE   && !ragOptions.HyDE.Enabled)       strategy = RagStrategy.Direct;
if (strategy == RagStrategy.Fusion && !ragOptions.MultiQuery.Enabled) strategy = RagStrategy.Direct;
```

#### R-18 — Citations = tous les chunks récupérés (plus aucun filtre depuis le Lot 0)

**Mécanisme.** Les citations sont construites depuis `contextChunks`, c'est-à-dire **tout** ce que
la récupération a retourné. Avant le Lot 0, le reranker coupait à `TopKAfterRerank = 5` et la
compression éliminait les chunks « AUCUN ». Ces deux étapes étant désormais désactivées par défaut
(à juste titre pour la latence), la chaîne est : Top-K=10 brut → contexte → citations, **sans aucun
filtre de précision**.

**Pourquoi c'est grave.** C'est la matérialisation visible du problème : même quand le LLM ignore
les extraits hors sujet dans sa réponse, ils apparaissent dans le panneau Sources avec un score
plausible. L'utilisateur perd confiance dans les citations.

**Correctif.** Reranker **batché en un appel** (R-5'), et à terme ne citer que les extraits
réellement exploités (corrélation réponse ↔ extraits, ou demande au LLM de lister les numéros
d'extraits utilisés). R-10 (seuil calibré) est **retiré du correctif** — voir §4.5, mesuré non
discriminant sur ce corpus.

### §4.5 — R-10 mesuré non viable (révision 2026-07-03)

**Méthode.** Mesure directe en SQL (cosinus pgvector hors pipeline, puis simulation exacte du
pipeline hybride RRF avec `K=60`) sur 12 questions couvrant les 4 documents du corpus réel
ré-ingéré (après R-15).

**Résultat n°1 — recherche vectorielle seule.** Sur 5 questions test, 3 ont le meilleur chunk
« bruit » **au-dessus** du meilleur chunk pertinent (plages qui se chevauchent totalement :
pertinent 0.68–0.79, bruit 0.68–0.76). Aucun seuil ne sépare proprement signal et bruit en score
brut sur ce corpus avec `nomic-embed-text`.

**Résultat n°2 — pipeline hybride réel (R-9, déjà livré).** En simulant exactement
`FuseWithFullTextAsync` (vectoriel TopK=10 + lexical TopK=10 → RRF → `Take(10)`), **11/12 questions
retrouvent le bon document en rang 1** du résultat fusionné. La fusion par **rang** (pas par score)
contourne le problème de score compressé — R-9 fait l'essentiel du travail, pas un futur seuil.
Seul échec : une question générique (« montant total facturé ») dont le document (1 seul chunk
très court) a un rang lexical réel de 31/100, hors de portée même avec `TopK` lexical élargi à 30.

**Résultat n°3 — décisif.** Des questions **totalement hors-sujet** (capitale de la Mongolie,
recette de tarte tatin, règles des échecs) scorent **0.69–0.71** en cosinus — strictement la même
plage que les chunks pertinents et le bruit du corpus. `nomic-embed-text` compresse toutes les
similarités dans une bande étroite (~0.65–0.80) indépendamment de la pertinence réelle sur ce
corpus. **Aucune valeur de `ScoreThreshold` ne peut donc servir de garde-fou « aucun document
pertinent » non plus.**

**Conclusion actionnable.** R-10 tel qu'écrit initialement (calibrer `ScoreThreshold` 0.55–0.65)
est déprioritisé : la métrique cosinus brute n'a pas le pouvoir discriminant nécessaire avec ce
modèle sur ce corpus, calibrer plus finement ne changerait rien. Le prochain gain mesurable vient
de **R-18** (reranker cross-encoder/LLM) : seul mécanisme capable de produire un score de
pertinence sémantiquement interprétable pour filtrer le bruit résiduel et détecter l'absence de
contenu pertinent.

## C. Synthèse des « 1 ligne, gros impact »

| Point | Correctif minimal | Effet | Statut |
|-------|-------------------|-------|--------|
| **R-1** | Injecter `DocumentName` au lieu du GUID | Citations textuelles correctes | ✅ |
| **R-5/R-6** | `Reranker.Enabled=false`, `ContextCompression.Enabled=false` | Latence ÷ ~20 | ✅ |
| **R-3** | Prompts génériques | Réponses non biaisées | ✅ |
| **I-17** | Colonne `tsvector` + index GIN | Mode Classique rapide | ✅ |
| **I-19** | Cache processeur Whisper par langue | Multilingue correct | ✅ |
| **R-15** | Préfixes `search_query:`/`search_document:` (+ ré-ingestion) | Séparation pertinent/hors-sujet restaurée | ✅ |
| **R-16** | `.Take(TopK)` après `ReciprocalRankFusion.Fuse` | Contexte/citations bornés en mode Fusion | ✅ |
| **R-9** | Fusion hybride vectoriel + lexical (RRF) | 11/12 questions test retrouvent le bon doc en rang 1 (mesuré 03/07) | ✅ |
| **R-18** | Reranker LLM **batché en 1 appel** (au lieu de N séquentiels) + masquage citations 0% | Filtre de bruit exploitable sans latence catastrophique | ✅ |
| **R-10** | ~~Seuil calibré~~ — mesuré non discriminant sur ce corpus (§4.5) | — | ❌ déprioritisé |
| **R-17** | `RagStrategyResolver` : repli Direct si `HyDE.Enabled`/`MultiQuery.Enabled=false` ; `Cache.Enabled=false` par défaut | Config non trompeuse | ✅ |
