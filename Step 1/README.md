# 🧩 Step 1 — RAG Fondamental

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![React 19](https://img.shields.io/badge/React-19-61DAFB?logo=react)
![pgvector](https://img.shields.io/badge/pgvector-PostgreSQL%2017-336791?logo=postgresql)
![OpenAI](https://img.shields.io/badge/OpenAI%2FOllama-compatible-412991?logo=openai)

> **Étape suivante →** [Step 2 — RAG Avancé](../Step%202/README.md)

---

## 🎯 Vue d'ensemble

Step 1 est le **socle du système RAG** (Retrieval-Augmented Generation). Il implémente l'intégralité du pipeline de base : ingestion de documents PDF, recherche vectorielle dans PostgreSQL, génération de réponses contextualisées via un LLM, et affichage en streaming dans une interface React.

Ce step pose toute l'architecture qui sera enrichie dans les étapes suivantes. Si vous ne connaissez pas encore le RAG, c'est par ici que tout commence.

---

## 📚 Ce que vous allez apprendre

| Concept | Technologie | Ce que vous construisez |
|---------|-------------|------------------------|
| Clean Architecture | .NET 10 | 4 couches Domain → Application → Infrastructure → Web.Api |
| Ingestion documentaire | PdfPig + pgvector | Extraction → Chunking → Embedding → Stockage |
| Recherche sémantique | pgvector (cosinus) | Retrouver les passages les plus pertinents par similarité |
| Génération contextuelle | IChatClient (OpenAI/Ollama) | Faire répondre un LLM uniquement à partir des sources |
| Streaming token par token | Server-Sent Events (SSE) | Affichage ChatGPT-like sans bloquer le navigateur |
| Pattern CQRS léger | MediatR | Séparer les commandes d'écriture des lectures directes |

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     Clean Architecture                   │
│                                                         │
│  ┌──────────┐   ┌───────────────┐   ┌────────────────┐ │
│  │  Domain  │ ← │  Application  │ ← │ Infrastructure │ │
│  │          │   │               │   │                │ │
│  │ Entités  │   │ IngestionSvc  │   │ RagPipeline    │ │
│  │ Interfaces│  │ Chunker       │   │ PgVector       │ │
│  │ Modèles  │   │ PdfExtractor  │   │ EmbeddingSvc   │ │
│  └──────────┘   └───────────────┘   └────────────────┘ │
│                                              ↑           │
│                                     ┌────────────────┐  │
│                                     │    Web.Api     │  │
│                                     │  Controllers   │  │
│                                     │  SSE Streaming │  │
│                                     └────────────────┘  │
└─────────────────────────────────────────────────────────┘
         ↕                                    ↕
  ┌──────────────┐                   ┌──────────────────┐
  │  PostgreSQL  │                   │  React 19 + Vite │
  │  + pgvector  │                   │  DocumentsPage   │
  │  (Docker)    │                   │  ChatPage (SSE)  │
  └──────────────┘                   └──────────────────┘
```

### Structure des dossiers

```
Step 1/
├── docker-compose.yml              # PostgreSQL 17 + pgvector
├── scripts/init.sql                # Schéma complet (tables, index HNSW)
├── ARCHITECTURE.md                 # Documentation technique détaillée
└── src/
    ├── Back/
    │   ├── AIExperience.Rag.Domain/          # 0 dépendance externe
    │   │   ├── Entities/                     # Document, DocumentChunk, etc.
    │   │   ├── Models/                       # RagQuery, RagResponse, RagStreamChunk
    │   │   └── Interfaces/Services/          # IRagPipelineService, IEmbeddingService…
    │   ├── AIExperience.Rag.Application/     # Services métier + CQRS
    │   │   ├── Commands/                     # UploadDocument, DeleteDocument
    │   │   └── Services/                     # IngestionService, RecursiveChunker…
    │   ├── AIExperience.Rag.Infrastructure/  # Implémentations concrètes
    │   │   ├── AI/Rag/                       # RagPipelineService, AdaptiveQueryRouter
    │   │   ├── AI/Embedding/                 # OpenAIEmbeddingService
    │   │   ├── VectorStore/                  # PgVectorStoreService
    │   │   └── Persistence/                  # EF Core 10, repositories
    │   ├── AIExperience.Web.Api/             # ASP.NET Core REST + SSE
    │   │   ├── Controllers/                  # DocumentsController, ChatController
    │   │   └── DTOs/                         # Records C# (request/response)
    │   └── AIExperience.App.Console/         # CLI interactive (tests sans front)
    └── Front/
        └── src/
            ├── api/client.ts                 # Client HTTP + SSE parser
            ├── pages/DocumentsPage.tsx       # Upload / liste / suppression
            └── pages/ChatPage.tsx            # Chat streaming (flushSync)
```

---

## ⚙️ Fonctionnalités

### 📄 Ingestion de documents PDF

Le pipeline d'ingestion transforme un PDF brut en vecteurs recherchables :

```
PDF uploadé
    ↓
PdfTextExtractor (PdfPig)
    → Regroupe les mots par position Y → lignes de texte
    ↓
RecursiveChunker
    → Découpe par \n\n (paragraphes)
    → Si > 800 caractères : subdivise encore
    → Chevauchement de 100 caractères entre chunks
    ↓
OpenAIEmbeddingService (batch)
    → float[] de 768 dimensions (Ollama) ou 3072 (OpenAI)
    ↓
PgVectorStoreService
    → INSERT dans document_chunks avec index HNSW (cosinus)
```

**Exemple concret :** un PDF de 50 pages (~100 000 caractères) produira environ 130 chunks de 800 caractères avec 100 de chevauchement, chacun stocké avec son vecteur 768 dimensions.

---

### 🔍 Pipeline RAG — Stratégie Direct

C'est la stratégie fondamentale, celle qui est **complètement implémentée** dans ce step :

```
❓ Question utilisateur
        ↓
🔢 Embedding de la question (float[768])
        ↓
🗄️  Recherche pgvector — similarité cosinus, top-10
        ↓
🤏 Compression contextuelle (optionnel)
    → Le LLM extrait les phrases pertinentes de chaque chunk
    → "AUCUN" → chunk éliminé
        ↓
📝 Construction du prompt
    → Historique de conversation
    → Contexte documentaire injecté
    → Question de l'utilisateur
        ↓
🤖 LLM génère la réponse (streaming token par token)
        ↓
📎 Construction des citations (document, page, score)
        ↓
✅ RagResponse (réponse + citations + métriques)
```

**Exemple de requête :**

```http
POST /api/chat/ask
Content-Type: application/json

{
  "question": "Quelles sont les principales attractions de Paris ?",
  "documentIds": ["3fa85f64-5717-4562-b3fc-2c963f66afa6"],
  "strategy": "Direct"
}
```

**Exemple de réponse :**

```json
{
  "answer": "Paris est connue pour la Tour Eiffel, le Musée du Louvre et Notre-Dame de Paris. La ville offre également une gastronomie réputée et de nombreux quartiers historiques comme Montmartre.",
  "citations": [
    {
      "documentName": "guide-paris.pdf",
      "pageNumber": 12,
      "excerpt": "La Tour Eiffel, construite en 1889, est le monument le plus visité au monde avec 7 millions de visiteurs par an.",
      "score": 0.92
    },
    {
      "documentName": "guide-paris.pdf",
      "pageNumber": 34,
      "excerpt": "Le quartier de Montmartre conserve son atmosphère de village avec la basilique du Sacré-Cœur et la Place du Tertre.",
      "score": 0.87
    }
  ],
  "strategyUsed": "Direct",
  "totalTokens": 1240,
  "durationMs": 2340
}
```

---

### 🌊 Streaming Server-Sent Events (SSE)

Le streaming permet d'afficher la réponse mot par mot, comme ChatGPT, sans attendre la réponse complète :

**Endpoint streaming :**
```http
POST /api/chat/stream
Content-Type: application/json

{ "question": "...", "documentIds": [...] }
```

**Format des événements SSE reçus :**
```
event: token
data: {"token": "Paris"}

event: token
data: {"token": " est"}

event: token
data: {"token": " connue"}

[... centaines de tokens ...]

event: done
data: {"answer": "Paris est connue...", "citations": [...], "strategyUsed": "Direct", "totalTokens": 1240, "durationMs": 2340}
```

**Pourquoi SSE et pas WebSocket ?**
- 🔁 SSE est unidirectionnel (serveur → client) ce qui suffit pour du streaming
- 🔌 Reconnexion automatique intégrée dans le navigateur
- 🪶 Plus simple à implémenter côté serveur ASP.NET Core
- 📦 `POST` au lieu de `GET` pour envoyer le body JSON de la question

**Côté React (ChatPage.tsx) :**

```typescript
for await (const event of api.chat.askStream({ question, documentIds })) {
  if (event.event === 'token') {
    streamingRef.current += event.data.token;
    // flushSync force React 18 à rendre immédiatement chaque token
    // sans ça, React batcherait tous les tokens et n'afficherait rien
    flushSync(() => setStreamingContent(streamingRef.current));
  }
}
```

---

### 🤖 Routeur adaptatif (AdaptiveQueryRouter)

Même en Step 1, un composant intelligent est présent : le routeur adaptatif envoie la question au LLM pour qu'il **choisisse la meilleure stratégie** selon la complexité :

```
Question : "C'est quoi Paris ?"
  → LLM analyse : question simple, factuelle
  → Retourne : "Direct" ✅

Question : "Compare les systèmes de transport en commun de Paris,
            Londres et Tokyo en termes d'efficacité et de coût"
  → LLM analyse : question complexe, multi-aspects
  → Retourne : "Fusion" (implémenté en Step 2)
```

> ⚠️ **Dans ce step**, seule la stratégie `Direct` est complètement fonctionnelle. Les stratégies `HyDE` et `Fusion` sont marquées en `TODO` dans `RagPipelineService.RetrieveChunksAsync` et seront implémentées en Step 2.

---

### 📊 Interface React

**Page Documents** — Gérez vos PDFs source :

```
┌──────────────────────────────────────────┐
│  📁 Mes Documents                        │
│                                          │
│  [+ Importer un PDF]                     │
│                                          │
│  ☑ guide-paris.pdf        2.3 Mo  ✅    │
│  ☑ histoire-france.pdf    5.1 Mo  ✅    │
│  ☐ monuments.pdf          1.8 Mo  ⏳    │
│                                          │
│  [🗑️ Supprimer la sélection]            │
└──────────────────────────────────────────┘
```

**Page Chat** — Interrogez vos documents :

```
┌──────────────────────────────────────────┐
│  💬 Chat RAG                             │
│  ─────────────────────────────────────  │
│  👤 Quelles sont les attractions de      │
│     Paris près de la Seine ?             │
│                                          │
│  🤖 Le long de la Seine, vous trouverez │
│     le Musée d'Orsay, la Cathédrale     │
│     Notre-Dame▌  (streaming en cours)   │
│                                          │
│  📎 Sources [▼]                          │
│     • guide-paris.pdf p.12 (score: 0.92)│
│     • guide-paris.pdf p.45 (score: 0.87)│
│                                          │
│  ⚡ Direct | 1 240 tokens | 2 340 ms    │
└──────────────────────────────────────────┘
```

---

## 🔧 Configuration

Le fichier `src/Back/AIExperience.Web.Api/appsettings.json` concentre toute la configuration :

```json
{
  "ConnectionStrings": {
    // Connexion à PostgreSQL lancé via docker-compose
    "Postgres": "Host=localhost;Port=5432;Database=ragdocumentchat;Username=postgres;Password=postgres"
  },
  "AI": {
    // Provider : AzureOpenAI | OpenAI | GitHubModels | Ollama
    "Provider": "OpenAI",
    // URL de votre service LLM (ici LM Studio en local)
    "Endpoint": "http://localhost:1234/v1",
    // Modèle de chat (ex: llama-3.2-1b-instruct pour Ollama)
    "ChatModel": "llama-3.2-1b-instruct",
    // Modèle d'embedding (doit correspondre aux dimensions ci-dessous)
    "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
    "ApiKey": "sk-lm-...",
    // 768 pour Ollama/nomic, 3072 pour text-embedding-3-large OpenAI
    "EmbeddingDimensions": 768
  },
  "RagOptions": {
    // Stratégie par défaut si non précisée dans la requête
    "DefaultStrategy": "Adaptive",
    "Retrieval": {
      "TopK": 10,            // Nombre de chunks récupérés
      "ScoreThreshold": 0.3  // Score cosinus minimum (0 = tous, 1 = parfait)
    },
    "ContextCompression": {
      "Enabled": true        // Le LLM filtre les phrases non pertinentes
    }
  },
  "Cors": {
    // Autoriser le front-end React en développement
    "AllowedOrigins": ["http://localhost:5173"]
  }
}
```

---

## 🚀 Démarrage rapide

### Prérequis
- 🐳 [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- 🔷 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 🟢 [Node.js 20+](https://nodejs.org/)
- 🤖 Un LLM accessible : [LM Studio](https://lmstudio.ai/) (local) ou une clé OpenAI/Azure

### 1. Démarrer la base de données

```powershell
cd "Step 1"
docker-compose up -d
# PostgreSQL 17 + pgvector démarre sur localhost:5432
# Le schéma est initialisé automatiquement via scripts/init.sql
```

### 2. Lancer le back-end

```powershell
dotnet run --project "Step 1/src/Back/AIExperience.Web.Api"
# API REST disponible sur http://localhost:50406
# Documentation interactive : http://localhost:50406/scalar/v1
```

### 3. Lancer le front-end

```powershell
cd "Step 1/src/Front"
npm install
npm run dev
# Interface React sur http://localhost:5173
```

### 4. Tester

1. Ouvrez [http://localhost:5173](http://localhost:5173)
2. **Page Documents** → importez un PDF
3. **Page Chat** → posez une question sur votre document
4. Observez le streaming token par token 🎉

> 💡 **Alternative sans front-end :** lancez `dotnet run --project "Step 1/src/Back/AIExperience.App.Console"` pour une interface en ligne de commande interactive.

---

## 📋 Endpoints REST

| Méthode | Route | Description |
|---------|-------|-------------|
| `GET` | `/api/documents` | Liste tous les documents |
| `GET` | `/api/documents/{id}` | Détail d'un document |
| `POST` | `/api/documents` | Upload + ingestion d'un PDF |
| `DELETE` | `/api/documents/{id}` | Supprime un document et ses chunks |
| `POST` | `/api/chat/ask` | Réponse RAG complète (bloquant) |
| `POST` | `/api/chat/stream` | Réponse RAG en streaming SSE |

---

## 🗺️ Ce qui vient ensuite

Step 1 est une fondation solide, mais le RAG peut faire bien mieux. Ces fonctionnalités sont préparées (configuration, interfaces) mais **pas encore implémentées** :

| Fonctionnalité | Statut dans Step 1 | Disponible dans |
|---------------|-------------------|-----------------|
| Stratégie **HyDE** (doc hypothétique) | TODO commenté | → Step 2 |
| Stratégie **Fusion** (multi-requêtes + RRF) | TODO commenté | → Step 2 |
| **Reranker** LLM cross-encoder | Interface prête, code absent | → Step 2 |
| Cache Redis des réponses | Config prête, code absent | Futur |
| Ingestion en arrière-plan (background service) | Statut forcé manuellement | Futur |
| Observabilité (OpenTelemetry, Grafana) | Infrastructure commentée | Futur |
| Authentification utilisateur | UserId hardcodé en dev | Futur |

> **→ [Continuer vers Step 2](../Step%202/README.md)** pour découvrir les stratégies de récupération avancées.

---

## 🧰 Stack technique

### Back-end
| Technologie | Version | Usage |
|------------|---------|-------|
| .NET / ASP.NET Core | 10.0 | Framework + API REST |
| EF Core + Npgsql | 10.0 | ORM PostgreSQL |
| pgvector | 0.3.0 | Recherche vectorielle cosinus |
| Microsoft.Extensions.AI | 10.5.0 | Abstraction `IChatClient` multi-provider |
| Semantic Kernel | 1.74.0 | Templates de prompts |
| MediatR | 14.1.0 | CQRS (commandes uniquement) |
| FluentValidation | 12.1.1 | Validation des commandes |
| PdfPig | 0.1.15 | Extraction texte PDF |
| Scalar.AspNetCore | — | Documentation API interactive |

### Front-end
| Technologie | Version | Usage |
|------------|---------|-------|
| React | 19.2.6 | Framework UI |
| TypeScript | 6.0 | Typage statique |
| Vite | 8.0 | Build tool + dev server |
| React Router | 7.16 | Navigation SPA |
| Fetch API | natif | HTTP + SSE streaming |

### Infrastructure
| Technologie | Usage |
|------------|-------|
| Docker Compose | PostgreSQL 17 + pgvector |
| LM Studio / Ollama | Modèles LLM locaux |
| OpenAI / Azure OpenAI | Modèles cloud |
