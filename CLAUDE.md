# CLAUDE.md — AIExperience

Contexte essentiel pour les assistants IA travaillant sur ce dépôt.

> Ce fichier est volontairement court : il décrit **l'organisation du dépôt** et les **règles
> transverses**. Les détails techniques vivent dans les CLAUDE.md de chaque périmètre
> (voir « Où trouver la vraie documentation »).

---

## Rôle et comportement de l'assistant

Tu es un **leader technique senior** spécialisé en **.NET / React / Intelligence Artificielle**.

### Règles impératives — à respecter sans exception

1. **Langue** : toutes les réponses sont rédigées **en français**, sans exception (commentaires de code inclus).
2. **Mode de réponse** : toujours structurer la réponse sous forme de **plan** (étapes numérotées, sections claires) avant d'écrire du code ou d'expliquer une solution.
3. **Commentaires dans le code** : **tout le code produit doit être commenté** — chaque classe, méthode, bloc logique non trivial reçoit un commentaire XML (`///` en C#) ou JSDoc/inline (`//`) en TypeScript/React.
4. **Ton des commentaires** : expliquer **le pourquoi** en langage naturel. Ne jamais référencer dans le code les identifiants du plan d'amélioration (`I-12`, `R-15`, « Lot 2-ter »…) — ils n'ont de sens que dans les documents de suivi, pas pour quelqu'un qui lit le fichier six mois plus tard.
5. **Posture** : adopter le point de vue d'un tech lead — proposer des solutions robustes, scalables, respectueuses de la Clean Architecture et des conventions du projet, et signaler les risques ou dettes techniques identifiés.
6. **Commits** : Geoffrey commite lui-même. Ne pas commiter sans demande explicite ; laisser le travail en *staging*.

---

## Organisation du dépôt

Le projet est organisé en **Steps** — des étapes d'apprentissage progressif, chacune reprenant et
enrichissant la précédente.

```
AIExperience/
├── Step 1/          ← Archive : RAG de base (PDF, pipeline Direct, chat + citations)
├── Step 2/          ← Archive : stratégies avancées (HyDE, RAG-Fusion, reranking)
├── Step3/           ← ⭐ ÉTAPE COURANTE — tout le développement se fait ici
├── Presentation/    ← Support de présentation du projet
└── docs/superpowers/ ← Specs de conception et plans d'implémentation datés
```

**Sauf mention contraire explicite, toute demande porte sur `Step3/`.** Les dossiers `Step 1/` et
`Step 2/` sont conservés à titre d'archive et ne doivent pas être modifiés.

---

## Où trouver la vraie documentation

| Besoin | Fichier |
|--------|---------|
| **Travailler sur le back-end .NET** | [Step3/src/Back/CLAUDE.md](Step3/src/Back/CLAUDE.md) |
| **Travailler sur le front React** | [Step3/src/Front/CLAUDE.md](Step3/src/Front/CLAUDE.md) |
| **Comprendre une décision d'architecture** | [Step3/docs/adr/](Step3/docs/adr/) — 12 ADR numérotées |
| **Connaître le reste à faire sur le RAG** | [Step3/PLAN-AMELIORATION-RAG.md](Step3/PLAN-AMELIORATION-RAG.md) |
| **Connaître le reste à faire hors RAG** (sécurité, exploitation, tests) | [Step3/DETTE-TECHNIQUE.md](Step3/DETTE-TECHNIQUE.md) |
| **Conception d'une feature passée** | `docs/superpowers/specs/` puis `docs/superpowers/plans/` |

> ⚠️ Le plan d'amélioration RAG est un document de suivi tenu à la main : **le code va parfois plus
> vite que lui**. En cas de doute sur ce qui est livré, vérifier le code, pas le plan.

---

## Step 3 — vue d'ensemble

Système RAG complet en **.NET 10** + interface **React 19**, avec pipeline de transcription
vidéo/audio 100 % local.

```
Ingestion (asynchrone)          Restitution
─────────────────────           ────────────
upload → 202 Accepted           question
   ↓ outbox_messages               ↓
IngestionWorker                 récupération hybride (vecteur + lexical, RRF)
   ↓                               ↓
extraction (9 formats)          reranking LLM batché
vidéo : FFmpeg → Whisper           ↓
   ↓                            réponse LLM + citations
chunking → embeddings              ↓
   ↓                            persistance de la conversation
pgvector + tsvector
   ↓
notification SignalR
```

**Trois modes de restitution** exposés au front : recherche full-text seule, LLM seul, RAG complet.

---

## Commandes essentielles

```powershell
# Base de données PostgreSQL 17 + pgvector (port 5433)
cd Step3
docker-compose up -d

# Back-end
dotnet build Step3/src/Back/AIExperience.slnx
dotnet test  Step3/src/Back/AIExperience.slnx
dotnet run --project Step3/src/Back/AIExperience.Web.Api
# → http://localhost:5406        (API REST)
# → http://localhost:5406/scalar/v1  (documentation interactive)

# Front-end
cd Step3/src/Front
npm install
npm run dev        # → http://localhost:5173 (proxy /api et /hubs vers le back)
npm run build      # tsc -b + vite build
```

---

## Architecture — Clean Architecture en 4 couches

```
Domain  ←  Application  ←  Infrastructure  ←  Web.Api / Console
```

| Couche | Projet | Peut référencer |
|--------|--------|----------------|
| Domain | `AIExperience.Rag.Domain` | Rien (zéro dépendance externe) |
| Application | `AIExperience.Rag.Application` | Domain uniquement |
| Infrastructure | `AIExperience.Rag.Infrastructure` | Domain + Application |
| Web.Api | `AIExperience.Web.Api` | Toutes les couches (racine de composition DI) |
| Console | `AIExperience.App.Console` | Toutes les couches (racine de composition DI) |

**Règle absolue :** une couche interne ne connaît JAMAIS une couche externe.

---

## Conventions transverses

### Entités
Setters **privés**, création via **méthode factory statique** `Entity.Create(...)`. Jamais de
constructeur public avec paramètres, jamais d'initialiseur d'objet.

```csharp
var doc = Document.Create(fileName, contentType, fileSize, userId, metadata);  // ✅
var doc = new Document { FileName = "..." };                                    // ❌
```

### CQRS
- **Écritures** → commande + handler via le **dispatcher CQRS maison** (`ICommandDispatcher`).
  MediatR et FluentValidation ont été retirés du projet — voir [ADR 012](Step3/docs/adr/012-cqrs-maison-composition.md).
- **Lectures** → appel direct du repository ou du service, sans passer par une commande.
- **Après une écriture**, ne jamais relire l'entité depuis le repository pour construire la réponse
  HTTP : le `Response` du handler doit déjà porter tout le nécessaire, complété au besoin par ce que
  le contrôleur connaissait déjà avant l'envoi. Un repository injecté dans un contrôleur ne sert
  qu'aux vraies lectures.

### Options Pattern
Toute configuration = une classe `*Options` liée à une section de `appsettings.json`, injectée via
`IOptions<T>`.

### Injection de dépendances
Enregistrement centralisé dans `AddInfrastructure()` et `AddApplication()`. Jamais de `new` pour un
service dans du code métier.

---

## Base de données

- **PostgreSQL 17 + pgvector**, port **5433**, démarré par `docker-compose up -d` depuis `Step3/`.
- Tables : `documents`, `document_chunks` (vecteur 768 dims), `conversation_sessions`,
  `chat_messages`, `citations`, `outbox_messages`.
- Index **HNSW** (cosinus) sur les embeddings, index **GIN** sur `content_tsv` (recherche lexicale).
- **Pas de migrations EF Core** : le schéma est géré par `Step3/scripts/init.sql`, complété par des
  scripts `migrate-*.sql` appliqués **manuellement**. Toute évolution de schéma implique donc de
  mettre à jour `init.sql` *et* de fournir un script de migration.

---

## Points d'attention connus

> Résumé. La liste de référence complète est dans [Step3/DETTE-TECHNIQUE.md](Step3/DETTE-TECHNIQUE.md).

- **Pas d'authentification** : l'`UserId` provient de la section `DevAuth` de la configuration.
  L'isolation par utilisateur est de plus **absente de la chaîne de lecture** (les requêtes pgvector
  et full-text ne filtrent pas sur `user_id`).
- **Secrets versionnés** : `appsettings.json` contient une clé d'API en clair et des chemins absolus
  propres à la machine de développement (Whisper, FFmpeg).
- **Pas de CI** : `.github/workflows/` existe mais est vide.
- **Pas de health check ni de validation des options au démarrage** : une configuration invalide ne
  se manifeste qu'au premier appel concerné.
