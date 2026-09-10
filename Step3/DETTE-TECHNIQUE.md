# Dette technique — Step 3

Liste de référence de **tout ce qui reste à faire hors qualité du RAG**. Sécurité, exploitation,
tests, outillage, dette de code.

## Périmètre — quel document pour quoi

| Sujet | Document |
|-------|----------|
| Qualité du RAG : ingestion, récupération, restitution, citations | [PLAN-AMELIORATION-RAG.md](PLAN-AMELIORATION-RAG.md) (I-xx / R-xx) |
| **Socle : sécurité, exploitation, tests, dette de code** | **Ce fichier** (S / E / T / D) |
| Pièges à connaître avant de coder sur un périmètre | [src/Back/CLAUDE.md](src/Back/CLAUDE.md) · [src/Front/CLAUDE.md](src/Front/CLAUDE.md) |
| Pourquoi une décision a été prise | [docs/adr/](docs/adr/) |

*Dernière revue : 2026-08-09.*

---

## Les cinq à traiter en premier

| # | Sujet | Pourquoi celui-là |
|---|-------|-------------------|
| **S-2** | Secrets hors du dépôt | Une clé d'API est en clair dans un fichier versionné. Coût : une heure. |
| **E-1** | Health checks + `ValidateOnStart` | Une configuration fausse ne se manifeste aujourd'hui qu'au premier appel concerné. |
| **E-4** | CI GitHub Actions | Le dossier existe mais est vide, et `dotnet test` est parfois bloqué en local (E-7). |
| **S-1** | Isolation par utilisateur | Bloquant absolu dès qu'un deuxième compte existe. |
| **T-4** | ✅ Harnais d'évaluation RAG — **livré (08/09)** | Sans lui, aucune amélioration de pertinence n'était mesurable — il conditionnait tout le plan RAG. Voir `src/Back/AIExperience.Eval`. |

---

## S — Sécurité

### S-1. Aucune authentification, et isolation par utilisateur absente de la lecture
L'`UserId` provient de la section `DevAuth` de la configuration. Plus grave que l'absence d'auth :
`PgVectorStoreService` ne filtre **jamais** sur `user_id`. Seules les tables `documents` et
`conversation_sessions` portent la colonne ; la recherche vectorielle et la recherche full-text
l'ignorent. Un deuxième utilisateur verrait donc les documents du premier dans ses citations.
Traiter les deux ensemble : ajouter l'auth sans ajouter le filtre ne résout rien.

### S-2. Secrets et chemins machine dans un fichier versionné
`src/Back/AIExperience.Web.Api/appsettings.json` contient la clé d'API LM Studio en clair, ainsi que
des chemins absolus vers `F:\` et `C:\Users\geoff\Downloads` (Whisper, FFmpeg).
→ user-secrets pour la clé, `appsettings.Development.json` non versionné pour les chemins.

### S-3. Aucun rate limiting sur `/api/chat`
Une seule question déclenche jusqu'à 5-8 appels LLM (routage, HyDE ou multi-query, reranking,
compression, complétion). Aucune limite par utilisateur ni globale.

### S-4. Surface d'upload non bornée
`[DisableRequestSizeLimit]` sur l'endpoint vidéo, et la validation des fichiers se fait **par
extension**, pas par signature de contenu. Un fichier renommé passe la validation.

### S-5. Prompt injection via documents ingérés
Aucune mitigation : le contenu d'un document ingéré arrive tel quel dans le prompt. Acceptable tant
que le pipeline ne fait que répondre — **à revoir impérativement avant de lui brancher le moindre
outil ou action**.

### S-6. Vulnérabilité connue dans une dépendance
`Microsoft.OpenApi` 2.0.0 remonte `NU1903` (gravité élevée) à chaque build.
→ [GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)

---

## E — Exploitation et industrialisation

### E-1. Aucun health check, aucune validation des options au démarrage
`AddHealthChecks` n'est appelé nulle part, et aucune classe d'options n'utilise `ValidateOnStart`.
Conséquence concrète : un `Whisper:ModelPath` erroné ne se découvre qu'à la première transcription,
donc potentiellement des heures après le démarrage. Les sondes utiles : PostgreSQL, endpoint LLM,
présence du modèle Whisper, présence du binaire FFmpeg.

### E-2. Migrations SQL appliquées à la main
Le schéma vit dans `scripts/init.sql` + 6 scripts `migrate-*.sql` appliqués manuellement
([ADR-010](docs/adr/010-schema-postgres-scripts-sql.md), statut « Accepté avec réserves »). Rien ne
garantit qu'une base existante et une base neuve convergent.
→ Un runner au démarrage (DbUp ou Grate) supprimerait cette classe entière d'incidents sans revenir
sur la décision de ne pas utiliser les migrations EF Core.

### E-3. Observabilité en trompe-l'œil
`.UseOpenTelemetry()` est bien appliqué aux `IChatClient` et `IEmbeddingGenerator` dans
`DependencyInjection.cs`, **mais aucun SDK ni exporter OpenTelemetry n'est enregistré** dans la
composition. Les traces produites ne vont donc nulle part. Il manque aussi les timings par étape du
pipeline et le suivi de consommation de tokens.

### E-4. Aucune CI
`.github/workflows/` existe et est **vide**. Un workflow minimal — `dotnet build`, `dotnet test`,
`tsc -b` — est d'autant plus utile que l'exécution locale des tests est parfois bloquée (E-7).

### E-5. Dockerfiles annoncés mais absents
`docker-compose.yml` référence des services applicatifs en commentaire ; le dossier `docker/` n'a
jamais été créé. Seul PostgreSQL est conteneurisé aujourd'hui.

### E-6. Volumétrie disque non maîtrisée (à anticiper)
Ne se pose pas encore — les originaux ne sont pas conservés — mais deviendra immédiat dès que le
stockage durable des sources sera implémenté (voir le Lot 5 du plan RAG) : conserver les vidéos
sources coûte cher. Prévoir une politique de rétention ou un quota **dès la mise en place**, pas après.

### E-7. `dotnet test` parfois bloqué par Smart App Control
Problème de poste Windows, **pas de code** — `dotnet build` reste fiable. Noté ici pour éviter de
diagnostiquer le projet quand c'est l'environnement. Renforce l'intérêt de E-4.

---

## T — Tests

### T-1. Zéro test côté front
Aucun outil de test n'est installé (`package.json` n'a ni Vitest ni Testing Library). Le premier
candidat évident est le **parseur SSE** de `src/api/client.ts` : logique pure, sans DOM, bugs
silencieux à la clé.

### T-2. `PgVectorStoreService` non testé
C'est du SQL brut — similarité cosinus, recherche lexicale, `tsvector`, upsert, fusion. C'est
exactement là que dorment les régressions, et c'est le seul composant qu'aucun test ne couvre.
→ Testcontainers avec l'image `pgvector/pgvector`.

### T-3. Chaîne HTTP complète non testée
Aucun test ne traverse controller → dispatcher → handler → persistance.
→ `WebApplicationFactory`.

### T-4. Harnais d'évaluation RAG ✅ Livré (08/09)
**Le manque le plus structurant.** Le projet a accumulé beaucoup de sophistication (fusion hybride,
reranking, quatre stratégies, préfixes d'embedding) sans jamais installer de mesure objective. Les
mesures manuelles en SQL du 03/07 (12 questions couvrant les 4 documents du corpus) étaient le bon
réflexe : il faut les figer en jeu de questions « golden » automatisé, avec recall@k et fidélité.
Sans ça, chaque évolution de pertinence se juge au ressenti.
→ Recoupe R-13 du [plan RAG](PLAN-AMELIORATION-RAG.md), tenu ici parce que c'est de l'outillage.

**Livré.** Les 12 questions manuelles du 03/07 sont désormais figées dans `eval/golden-dataset.json`
et exécutées automatiquement par `src/Back/AIExperience.Eval` (`dotnet run --project
src/Back/AIExperience.Eval -- run`), avec recall@k (citations finales du pipeline) et fidélité
(LLM-as-judge). Mode `compare` pour un avant/après entre deux runs. Reste hors CI (E-4 non livrée) :
exécution manuelle, base et LLM réels requis.

### T-5. Types front non générés depuis l'OpenAPI
`src/Front/src/types/index.ts` est un miroir **manuel** des DTO C#. Toute évolution d'un DTO côté
back doit être répercutée à la main, et l'oubli ne se voit qu'à l'exécution.
→ `openapi-typescript` sur le schéma déjà exposé par l'API.

---

## D — Dette de code et incohérences

### D-1. ~~Configuration mensongère : le cache~~ — corrigé
`"Cache": { "Enabled": true }` a été passé à `false` dans les deux `appsettings.json` (Web.Api et
App.Console), puisqu'aucune implémentation de cache n'existe. Implémenter ou retirer la section
reste à faire.

### D-2. ~~Stratégie par défaut incohérente~~ — corrigé
Le DTO `AskQuestionRequest` avait `RagStrategy.HyDE` comme valeur par défaut, alors que
`RagOptions.DefaultStrategy` vaut `Adaptive`. Le défaut du DTO est désormais `RagStrategy.Adaptive`,
pour qu'un client qui n'envoie pas `strategy` (comme le front) obtienne bien la stratégie
configurée.

### D-3. Avertissements de compilation non traités
Deux `CS8602` (déréférencement d'une éventuelle référence null) dans
`src/Back/AIExperience.Rag.Application/Services/TextExtractor/PowerPointTextExtractor.cs`,
lignes 52 et 58.

### D-4. `DocumentMetadata` mal rangé
C'est un `record`, placé dans le dossier `Domain/Enums/`. Sans conséquence fonctionnelle, mais
trompeur à la lecture.

### D-5. Asymétrie de langue dans la recherche full-text
L'index `content_tsv` est construit dans la langue **détectée du document**, tandis que la requête
est analysée dans une langue fixe. Un corpus multilingue perd donc en rappel lexical. Limitation
connue et assumée : la corriger imposerait un fan-out de requête par langue.

---

## Ce qui n'est **pas** ici

Ces sujets relèvent de la qualité du RAG et vivent dans
[PLAN-AMELIORATION-RAG.md](PLAN-AMELIORATION-RAG.md) :

- **Condensation de question multi-tour** — la récupération ignore l'historique (le plus urgent du plan).
- **Budget de tokens en entrée du LLM** (R-11) — spécifié, non implémenté.
- **Consultation des sources depuis les citations** (Lot 5) — les originaux ne sont pas conservés.
- **Reranker cross-encoder local** (bge-reranker ONNX) en remplacement du reranker LLM.
- **Parent-document retrieval** — petits chunks pour la recherche, fenêtre élargie pour le LLM.
- **Reliquat du Lot 1** — chunking en tokens, validation des dimensions d'embedding, idempotence,
  `COPY` binaire.
- **OCR des PDF scannés** — hors périmètre assumé.
