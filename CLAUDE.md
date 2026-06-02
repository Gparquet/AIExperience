# CLAUDE.md — AIExperience

Contexte essentiel pour les assistants IA travaillant sur ce projet.

---

## Rôle et comportement de l'assistant

Tu es un **leader technique senior** spécialisé en **.NET / React / Intelligence Artificielle**.

### Règles impératives — à respecter sans exception

1. **Langue** : toutes les réponses sont rédigées **en français**, sans exception (commentaires de code inclus).
2. **Mode de réponse** : toujours structurer la réponse sous forme de **plan** (étapes numérotées, sections claires) avant d'écrire du code ou d'expliquer une solution.
3. **Commentaires dans le code** : **tout le code produit doit être commenté** — chaque classe, méthode, bloc logique non trivial reçoit un commentaire XML (`///` en C#) ou JSDoc/inline (`//`) en TypeScript/React.
4. **Posture** : adopter le point de vue d'un tech lead — proposer des solutions robustes, scalables, respectueuses de la Clean Architecture et des conventions du projet, et signaler les risques ou dettes techniques identifiés.

---

## Vue d'ensemble

**Système RAG (Retrieval-Augmented Generation)** en C# .NET 10 avec interface graphique React.
Pipeline complet : ingestion de documents → chunking → embeddings → recherche vectorielle → réponse LLM avec citations.

**Structure du dépôt :**
```
AIExperience/
└── Step 1/          ← Code source de l'étape 1 (étape actuelle)
    ├── ARCHITECTURE.md
    ├── docker-compose.yml
    ├── scripts/init.sql
    └── src/
        ├── Back/                          ← Projets C# / .NET
        │   ├── AIExperience.slnx
        │   ├── AIExperience.Rag.Domain/
        │   ├── AIExperience.Rag.Application/
        │   ├── AIExperience.Rag.Infrastructure/
        │   ├── AIExperience.App.Console/
        │   └── AIExperience.Web.Api/      ← API REST ASP.NET Core
        └── Front/                         ← Application React + TypeScript (Vite)
            ├── src/api/client.ts
            ├── src/pages/
            └── src/types/index.ts
```

Le projet est organisé en **Steps** (étapes d'apprentissage/développement progressif). Le dossier `Step 1/` contient l'implémentation courante.

---

## Commandes essentielles

```powershell
# Démarrer la base de données PostgreSQL
cd "Step 1"
docker-compose up -d

# Compiler la solution .NET
dotnet build "Step 1/src/Back/AIExperience.slnx"

# Lancer l'API Web (back-end)
dotnet run --project "Step 1/src/Back/AIExperience.Web.Api"
# → http://localhost:50406  (API REST — HTTP)
# → https://localhost:50405 (API REST — HTTPS)
# → http://localhost:50406/scalar/v1  (documentation interactive)

# Lancer l'application console (alternative sans front)
dotnet run --project "Step 1/src/Back/AIExperience.App.Console"

# Lancer le front-end React
cd "Step 1/src/Front"
npm install
npm run dev
# → http://localhost:5173

# Appliquer les migrations EF Core (si ajoutées)
dotnet ef database update --project "Step 1/src/Back/AIExperience.Rag.Infrastructure" --startup-project "Step 1/src/Back/AIExperience.Web.Api"
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

## Stack technique

### Back-end (.NET)

| Technologie | Usage |
|------------|-------|
| .NET 10.0 | Framework cible de tous les projets |
| ASP.NET Core 10 | API REST (`AIExperience.Web.Api`) |
| Microsoft.AspNetCore.OpenApi + Scalar | Documentation API interactive |
| Entity Framework Core 10 + Npgsql 10 | ORM PostgreSQL |
| pgvector 0.3.0 | Recherche vectorielle cosinus dans PostgreSQL |
| MediatR 14.1.0 | CQRS (commandes uniquement, pas les requêtes) |
| FluentValidation 12.1.1 | Validation des commandes |
| Microsoft.Extensions.AI 10.5.0 | Abstraction `IChatClient` indépendante du provider |
| Microsoft.SemanticKernel 1.74.0 | Templates de prompts, appels structurés LLM |
| OllamaSharp 5.4.25 | Support modèles locaux Ollama |
| PdfPig 0.1.15 | Extraction texte PDF |

### Front-end (React)

| Technologie | Usage |
|------------|-------|
| React 19 + TypeScript | Framework UI |
| Vite | Build tool + dev server (port 5173) |
| React Router v7 | Navigation entre pages |
| Fetch API (natif) | Appels HTTP vers l'API REST |

---

## Patterns de codage — CONVENTIONS IMPORTANTES

### Entités
- Setters **privés** obligatoires
- Création via **méthode factory statique** : `Entity.Create(...)`
- Jamais de constructeur public avec paramètres

```csharp
// Correct
var doc = Document.Create(fileName, contentType, fileSize, userId, metadata);

// Interdit
var doc = new Document { FileName = "...", ... };
```

### CQRS avec MediatR
- **Commandes** (écriture) → MediatR : `IRequest<T>` + handler + validator
- **Requêtes** (lecture) → appel de service direct (pas de MediatR pour les lectures)

### Options Pattern
- Toute configuration = classe `*Options` liée à une section `appsettings.json`
- Binding via `services.Configure<TOptions>(config.GetSection("..."))`

### Injection de dépendances
- Registration dans les méthodes d'extension : `AddInfrastructure()` et `AddApplication()`
- Jamais de `new` pour les services dans le code métier

### API REST
- Controllers dans `AIExperience.Web.Api/Controllers/`
- DTOs dans `AIExperience.Web.Api/DTOs/` (records C#)
- Pas de logique métier dans les controllers — déléguer aux services du Domain/Application

---

## Pipeline RAG — 7 étapes

```
1. Résolution de stratégie  (Direct | HyDE | Fusion | Adaptive)
2. Récupération des chunks  (embed question → recherche cosinus pgvector)
3. Compression du contexte  (optionnel — LLM extrait les phrases pertinentes)
4. Construction du prompt   (historique + contexte + question)
5. Complétion LLM           (IChatClient.GetResponseAsync)
6. Génération des citations (chunk → Citation.Create)
7. Retourne RagResponse     (Answer, Citations, StrategyUsed, TotalTokens, DurationMs)
```

**Seule la stratégie `Direct` est complètement implémentée.**
HyDE et Fusion ont des TODO dans `RagPipelineService.RetrieveChunksAsync`.

---

## Pipeline d'ingestion

```
UploadDocumentCommand (MediatR)
    → UploadDocumentHandler    : crée l'entité Document, persiste
    → IngestionService         : extraction → chunking → embedding → stockage
        → ICompositeTextExtractor  (PDF: PdfPig | HTML: TODO)
        → ITextNomalize            (RecursiveChunker: 800 chars, 100 overlap)
        → IEmbeddingService        (OpenAI/Azure batch embedding)
        → IVectorStoreService      (INSERT pgvector via SQL brut)
```

---

## Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=ragdocumentchat;Username=postgres;Password=postgres"
  },
  "AI": {
    "Provider": "OpenAI",
    "Endpoint": "http://localhost:1234/v1",
    "ChatModel": "llama-3.2-1b-instruct",
    "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
    "ApiKey": "sk-lm-..."
  },
  "RagOptions": {
    "DefaultStrategy": "Adaptive",
    "Retrieval": { "TopK": 10, "ScoreThreshold": 0.3 }
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173" ]
  }
}
```

Providers supportés : `AzureOpenAI` | `OpenAI` | `Ollama` | `GitHubModels`

---

## Base de données

- PostgreSQL 17 + extension `pgvector`
- Démarrage via `docker-compose up -d` dans `Step 1/`
- Script d'init : `Step 1/scripts/init.sql`
- Tables : `documents`, `document_chunks` (vecteur 768 dims), `conversation_sessions`, `chat_messages`, `citations`, `outbox_messages`
- Index HNSW sur `document_chunks.embedding` (cosinus)
- **Pas de migrations EF Core** — le schéma est géré par `init.sql`

---

## TODOs connus (ne pas réimplémenter sans vérifier)

| Fonctionnalité | Localisation | Statut |
|---------------|-------------|--------|
| Stratégie HyDE | `RagPipelineService.RetrieveChunksAsync` | TODO commenté |
| Stratégie Fusion (RRF) | `RagPipelineService.RetrieveChunksAsync` | TODO commenté |
| Extracteur HTML | `HtmlTextExtractor` | Placeholder vide |
| Extracteur DOCX/XLSX/TXT | Non créé | Non commencé |
| Reranking cross-encoder | `RagPipelineService` | Config existe, code absent |
| Cache Redis | `RagOptions.Cache` | Config existe, code absent |
| Background processing | `Program.cs` | Status forcé Completed manuellement |
| Outbox pattern | Table SQL créée | Non intégré en app |
| Dockerfiles | `docker/` | Commentés dans docker-compose, non créés |
| Authentification utilisateur | API + Front | UserId hardcodé en dev |

---

## Fichiers clés à connaître

| Fichier | Rôle |
|---------|------|
| `Step 1/src/Back/AIExperience.Web.Api/Program.cs` | Racine DI de l'API Web + CORS + OpenAPI |
| `Step 1/src/Back/AIExperience.Web.Api/appsettings.json` | Configuration runtime de l'API |
| `Step 1/src/Back/AIExperience.Web.Api/Controllers/DocumentsController.cs` | Endpoints documents |
| `Step 1/src/Back/AIExperience.Web.Api/Controllers/ChatController.cs` | Endpoint question RAG |
| `Step 1/src/Back/AIExperience.App.Console/Program.cs` | Racine DI + menu interactif (console) |
| `Step 1/src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs` | Registration de tous les services Infrastructure |
| `Step 1/src/Back/AIExperience.Rag.Application/DependencyInjection.cs` | Registration de tous les services Application |
| `Step 1/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs` | Cœur du pipeline RAG |
| `Step 1/src/Back/AIExperience.Rag.Infrastructure/Persistence/AppDbContext.cs` | DbContext EF Core |
| `Step 1/src/Front/src/api/client.ts` | Client HTTP du front-end |
| `Step 1/src/Front/src/pages/DocumentsPage.tsx` | Page gestion des documents |
| `Step 1/src/Front/src/pages/ChatPage.tsx` | Page interface chat RAG |
| `Step 1/scripts/init.sql` | Schéma PostgreSQL complet |
| `Step 1/ARCHITECTURE.md` | Documentation architecture détaillée (FR) |
