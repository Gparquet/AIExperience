# 🚀 Step 2 — RAG Avancé : Stratégies Adaptatives

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![React 19](https://img.shields.io/badge/React-19-61DAFB?logo=react)
![pgvector](https://img.shields.io/badge/pgvector-PostgreSQL%2017-336791?logo=postgresql)
![OpenAI](https://img.shields.io/badge/OpenAI%2FOllama-compatible-412991?logo=openai)

> **← Étape précédente** [Step 1 — RAG Fondamental](../Step%201/README.md)

---

## 🎯 Vue d'ensemble

Step 2 fait passer le RAG du niveau "ça marche" au niveau "ça marche bien". Là où Step 1 cherchait les documents pertinents en faisant simplement une recherche vectorielle sur la question brute, Step 2 introduit **quatre stratégies de récupération avancées** qui améliorent significativement la qualité des réponses.

Le cœur de l'évolution : le LLM n'est plus seulement utilisé pour répondre, il est aussi mobilisé pour **mieux chercher** (HyDE, MultiQuery) et pour **filtrer les résultats** (Reranker, Adaptive routing).

---

## 📚 Ce que vous allez apprendre

| Concept | Technologie | Ce que vous construisez |
|---------|-------------|------------------------|
| HyDE (Hypothetical Document Embeddings) | LLM + pgvector | Générer un doc fictif pour mieux rechercher |
| Multi-Query RAG | LLM + parallélisme | Reformuler la question sous N angles |
| Reciprocal Rank Fusion (RRF) | Algorithme (Cormack 2009) | Fusionner N résultats de recherche intelligemment |
| Reranking LLM | IChatClient | Classer les chunks par pertinence sémantique réelle |
| Routing adaptatif | LLM comme décideur | Choisir automatiquement la meilleure stratégie |
| Interfaces Domain | Clean Architecture | Séparer contrat et implémentation pour chaque service |

---

## 🆚 Ce qui change par rapport à Step 1

### Nouveaux fichiers ajoutés

```
Step 2/src/Back/
├── AIExperience.Rag.Domain/
│   └── Interfaces/Services/AI/
│       ├── IHydeService.cs          🆕 Contrat pour la génération HyDE
│       ├── IMultiQueryService.cs    🆕 Contrat pour les variantes de requête
│       └── IRerankerService.cs      🆕 Contrat pour le reclassement LLM
│
└── AIExperience.Rag.Infrastructure/
    └── AI/Rag/
        ├── HydeService.cs           🆕 Implémentation HyDE
        ├── MultiQueryService.cs     🆕 Génération de variantes
        ├── ReciprocalRankFusion.cs  🆕 Algorithme de fusion RRF
        └── LlmRerankerService.cs    🆕 Reranking par LLM
```

### Fichiers modifiés

```
├── RagPipelineService.cs            ✏️  Branches HyDE et Fusion complétées
├── RagPrompts.cs                    ✏️  3 nouveaux prompts (HyDE, MultiQuery, Reranker)
├── DependencyInjection.cs           ✏️  3 nouveaux services enregistrés
├── ChatDtos.cs                      ✏️  Champ Strategy ajouté dans la requête
└── appsettings.json                 ✏️  Section RagOptions enrichie
```

---

## 🏗️ Architecture du pipeline étendu

```
┌──────────────────────────────────────────────────────────────────┐
│                    Pipeline RAG Step 2                           │
│                                                                  │
│  ❓ Question                                                     │
│       ↓                                                          │
│  🧭 AdaptiveQueryRouter (LLM choisit la stratégie)              │
│       ↓                                                          │
│  ┌────────────┬────────────────────────┬─────────────────┐      │
│  │   DIRECT   │         HYDE           │     FUSION      │      │
│  │            │                        │                 │      │
│  │ Question   │ LLM génère un doc      │ LLM génère 3   │      │
│  │ → embed    │ fictif → embed → search│ variantes →    │      │
│  │ → search   │                        │ 3 recherches   │      │
│  │            │                        │ → RRF fusion   │      │
│  └────────────┴────────────────────────┴─────────────────┘      │
│       ↓                                                          │
│  🏆 LlmRerankerService (score 0-10 par chunk, top-K gardés)     │
│       ↓                                                          │
│  🤏 ContextCompressorService (optionnel)                         │
│       ↓                                                          │
│  🤖 LLM génère la réponse (bloquant ou streaming SSE)           │
│       ↓                                                          │
│  📎 Citations + métriques → RagResponse                         │
└──────────────────────────────────────────────────────────────────┘
```

---

## ⚙️ Les quatre stratégies en détail

### 1. 🎯 Direct (inchangée depuis Step 1)

La plus simple : on encode directement la question et on cherche les chunks similaires.

```
Question → Embedding → pgvector search → Résultats
```

✅ Rapide et efficace pour les questions précises et courtes.  
⚠️ Limitée pour les questions abstraites ou ambiguës.

---

### 2. 💡 HyDE — Hypothetical Document Embeddings

**Problème résolu :** la question "Comment fonctionne un moteur à réaction ?" et les chunks du manuel technique ont des embeddings très différents, même s'ils parlent du même sujet. La question est courte et générique, les chunks sont longs et techniques.

**Solution :** demander au LLM de **générer un document fictif** qui répondrait à la question. Ce document hypothétique ressemble stylistiquement aux vrais chunks → son embedding est géométriquement plus proche.

```
Question : "Comment fonctionne un moteur à réaction ?"
        ↓
LLM génère un doc fictif (~200 mots) :
  "Un moteur à réaction fonctionne selon le principe de propulsion
   par réaction. L'air est aspiré par le compresseur, mélangé avec
   du kérosène dans la chambre de combustion, puis expulsé à grande
   vitesse par la tuyère, générant une poussée..."
        ↓
Embedding du doc fictif (plus proche des vrais chunks)
        ↓
Recherche pgvector → meilleurs résultats
```

**Implémentation dans HydeService.cs :**

```csharp
var prompt = RagPrompts.Hyde
    .Replace("{length}", options.Value.HyDE.HypotheticalDocLength.ToString())
    .Replace("{question}", question);

var response = await chatClient.GetResponseAsync(
    prompt,
    new ChatOptions { MaxOutputTokens = length + 50, Temperature = 0.3f },
    ct);

return response.Text.Trim(); // Document fictif → sera embedé
```

**Prompt utilisé :**
```
Tu es un expert documentaire.
Génère un court passage de texte (environ {length} mots) qui RÉPONDRAIT
directement à la question suivante. Ce passage doit ressembler à un extrait
de document factuel, pas à une réponse conversationnelle.

Question : {question}
```

✅ Excellent pour les questions complexes, ouvertes ou abstraites.  
⚠️ Coûte un appel LLM supplémentaire avant la recherche.

---

### 3. 🔀 Fusion — Multi-Query + Reciprocal Rank Fusion

**Problème résolu :** une seule formulation de la question peut rater des chunks pertinents si le vocabulaire ne correspond pas exactement.

**Solution :** générer **3 reformulations** de la question, faire **3 recherches parallèles**, puis **fusionner les résultats** via l'algorithme RRF.

```
Question : "Quels sont les risques des voyages en haute montagne en hiver ?"
        ↓
MultiQueryService génère 3 variantes :
  1. "Dangers liés à l'alpinisme hivernal"
  2. "Risques météorologiques et physiques en montagne l'hiver"
  3. "Précautions à prendre pour les randonnées hivernales en altitude"
        ↓
3 recherches pgvector en parallèle → 3 top-10 de chunks
        ↓
ReciprocalRankFusion.Fuse() :
  Score RRF = Σ 1 / (60 + rang)
  Un chunk #1 dans 3 listes → score très élevé
  Un chunk #8 dans 1 seule liste → score faible
        ↓
Résultat fusionné et re-classé (sans doublons)
```

**Pourquoi K=60 dans la formule RRF ?**

La constante 60 a été déterminée empiriquement par Cormack et al. (2009) comme le meilleur compromis entre les documents bien classés dans plusieurs listes et ceux qui apparaissent uniquement en tête d'une seule liste.

**Implémentation dans ReciprocalRankFusion.cs :**

```csharp
public static IReadOnlyList<(DocumentChunk Chunk, double Score)> Fuse(
    IEnumerable<IReadOnlyList<(DocumentChunk Chunk, double Score)>> resultLists)
{
    var scores = new Dictionary<Guid, (DocumentChunk Chunk, double Score)>();

    foreach (var list in resultLists)
    {
        for (int rank = 0; rank < list.Count; rank++)
        {
            var (chunk, _) = list[rank];
            var rrfScore = 1.0 / (60 + rank + 1); // Formule RRF standard

            if (scores.TryGetValue(chunk.Id, out var existing))
                scores[chunk.Id] = (chunk, existing.Score + rrfScore);
            else
                scores[chunk.Id] = (chunk, rrfScore);
        }
    }

    return scores.Values
        .OrderByDescending(x => x.Score)
        .ToList();
}
```

✅ Maximise le recall en couvrant plusieurs angles sémantiques.  
⚠️ Coûte N appels d'embedding et N recherches pgvector.

---

### 4. 🤖 Adaptive — Le LLM choisit la stratégie

**Le mieux des trois mondes :** le routeur adaptatif analyse chaque question et choisit automatiquement la stratégie la plus appropriée, en tenant compte de la complexité et du coût.

```
Question simple : "C'est quoi Paris ?"
  → AdaptiveQueryRouter → "Direct"   (rapide, peu cher)

Question abstraite : "Quels sont les effets du réchauffement climatique ?"
  → AdaptiveQueryRouter → "HyDE"    (doc fictif améliore la recherche)

Question complexe multi-aspects : "Compare les systèmes de santé
  européens en termes de coût, qualité et accessibilité"
  → AdaptiveQueryRouter → "Fusion"  (multi-angle nécessaire)
```

**Implémentation dans AdaptiveQueryRouter.cs :**

Le LLM reçoit un prompt de classification et répond uniquement par `"Direct"`, `"HyDE"` ou `"Fusion"`. La réponse est ensuite parsée en `RagStrategy`.

---

## 🏆 Reranker — Filtrer par pertinence sémantique réelle

Le reranker intervient **après** la récupération initiale pour corriger les erreurs de la similarité cosinus.

**Problème résolu :** la similarité cosinus mesure la proximité mathématique des vecteurs, pas la pertinence réelle. Un chunk peut avoir un score cosinus élevé (0.85) mais être hors sujet.

**Exemple concret :**

```
Question : "Comment prévenir le mal des transports ?"

Chunk A — score cosinus 0.85 :
  "Les transports en commun ont connu une augmentation de 15% de la
   fréquentation en 2023, notamment grâce à la hausse du coût du carburant."
  → Score reranker : 1/10  ← hors sujet malgré le bon score cosinus

Chunk B — score cosinus 0.72 :
  "Pour prévenir le mal des transports, il est conseillé de s'asseoir
   à l'avant du véhicule, de regarder l'horizon fixe et d'éviter la lecture."
  → Score reranker : 9/10  ← très pertinent mais score cosinus moyen
```

Sans reranker, Chunk A serait affiché en premier. Avec le reranker, Chunk B remonte en tête.

**Implémentation dans LlmRerankerService.cs :**

```csharp
// Pour chaque chunk candidat, le LLM attribue un score 0-10
var prompt = RagPrompts.Reranker
    .Replace("{question}", question)
    .Replace("{chunk}", chunk.Content);

var response = await chatClient.GetResponseAsync(prompt, ct);

// "7" → 0.7 (normalisé)
if (int.TryParse(response.Text.Trim(), out var score))
    scored.Add((chunk, score / 10.0));
```

**Prompt utilisé :**
```
Évalue la pertinence de l'extrait suivant pour répondre à la question donnée.
Réponds UNIQUEMENT avec un nombre entier entre 0 et 10.
(0 = totalement hors sujet, 10 = répond parfaitement à la question)
Aucune explication, juste le chiffre.

Question : {question}

Extrait :
{chunk}
```

---

## 📊 Comparaison des stratégies

| Stratégie | Nb appels LLM | Nb recherches pgvector | Latence | Qualité |
|-----------|:---:|:---:|:---:|:---:|
| **Direct** | 0 | 1 | ⚡ Très rapide | ⭐⭐⭐ |
| **HyDE** | +1 (doc fictif) | 1 | 🔵 Modérée | ⭐⭐⭐⭐ |
| **Fusion** | +1 (variantes) | 3 | 🟡 Plus lente | ⭐⭐⭐⭐⭐ |
| **Adaptive** | +1 (routing) + stratégie choisie | Selon stratégie | 🟠 Variable | ⭐⭐⭐⭐⭐ |

> Le **Reranker** s'ajoute à toutes les stratégies quand `Reranker.Enabled = true`. Il coûte N appels LLM supplémentaires (un par chunk candidat).

---

## 🔄 Exemple complet — Stratégie Fusion

```
👤 Utilisateur : "Quel est le meilleur itinéraire pour traverser
                  les Alpes en hiver en voiture ?"

1. AdaptiveQueryRouter → "Fusion" (question multi-aspects)

2. MultiQueryService génère 3 variantes :
   • "Cols alpins praticables en hiver en automobile"
   • "Routes de montagne sécurisées pour traverser les Alpes l'hiver"
   • "Conditions routières hivernales dans les Alpes"

3. 3 recherches pgvector en parallèle :
   Liste 1 : [Col du Fréjus, Tunnel du Mont-Blanc, Col de Montgenèvre, ...]
   Liste 2 : [Tunnel du Mont-Blanc, Col du Petit Saint-Bernard, ...]
   Liste 3 : [Chaînes à neige obligatoires, Fréjus (tunnel), ...]

4. RRF Fusion :
   Tunnel du Mont-Blanc → rank 1 + rank 2 + rank 3 → score RRF 0.047
   Col du Fréjus → rank 1 + rank 3 → score RRF 0.032
   ...

5. LlmRerankerService (top-10 → top-5) :
   "Tunnel du Mont-Blanc" — score cosinus 0.91 → LLM : 9/10 ✅
   "Histoire des Alpes" — score cosinus 0.78 → LLM : 1/10 ❌ éliminé

6. ContextCompressorService :
   Extrait uniquement les phrases pertinentes de chaque chunk retenu

7. LLM génère la réponse avec le contexte comprimé

🤖 "Pour traverser les Alpes en hiver en voiture, l'itinéraire le plus
     fiable est le Tunnel du Mont-Blanc (ouvert toute l'année, pas affecté
     par la neige). Alternativement, le Col du Fréjus est généralement
     praticable avec des chaînes à neige..."
```

---

## 🔧 Configuration

```json
{
  "RagOptions": {
    // Stratégie par défaut : Adaptive, Direct, HyDE ou Fusion
    "DefaultStrategy": "Adaptive",

    "HyDE": {
      "Enabled": true,
      // Longueur du document hypothétique généré (en tokens)
      "HypotheticalDocLength": 200
    },

    "MultiQuery": {
      "Enabled": true,
      // Nombre de variantes générées pour la stratégie Fusion
      "VariantCount": 3
    },

    "Retrieval": {
      // Nombre de chunks récupérés par recherche
      "TopK": 10,
      // Score cosinus minimum (filtre les résultats trop éloignés)
      "ScoreThreshold": 0.3
    },

    "Reranker": {
      "Enabled": true,
      // Nombre de chunks conservés APRÈS le reranking (sur les TopK récupérés)
      "TopKAfterRerank": 5
    },

    "ContextCompression": {
      // Le LLM extrait uniquement les phrases pertinentes de chaque chunk
      "Enabled": true
    },

    "Cache": {
      // Cache des réponses (non encore implémenté)
      "Enabled": true,
      "TtlMinutes": 60
    }
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
cd "Step 2"
docker-compose up -d
# PostgreSQL 17 + pgvector démarre sur localhost:5432
```

### 2. Lancer le back-end

```powershell
dotnet run --project "Step 2/src/Back/AIExperience.Web.Api"
# API REST disponible sur http://localhost:50406
# Documentation interactive : http://localhost:50406/scalar/v1
```

### 3. Lancer le front-end

```powershell
cd "Step 2/src/Front"
npm install
npm run dev
# Interface React sur http://localhost:5173
```

### 4. Tester les stratégies avancées

Depuis l'interface React ou via un client HTTP :

```http
# Tester HyDE
POST /api/chat/ask
{ "question": "Comment fonctionne la photosynthèse ?", "strategy": "HyDE" }

# Tester Fusion
POST /api/chat/ask
{ "question": "Compare les avantages et inconvénients des énergies renouvelables", "strategy": "Fusion" }

# Laisser le système choisir
POST /api/chat/ask
{ "question": "...", "strategy": "Adaptive" }
```

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

**Nouveauté Step 2 :** le champ `strategy` dans le body accepte `"Direct"`, `"HyDE"`, `"Fusion"` ou `"Adaptive"`. La valeur par défaut est `"HyDE"`.

---

## 🧰 Stack technique

### Composants ajoutés en Step 2

| Composant | Rôle | Amélioration apportée |
|-----------|------|----------------------|
| `HydeService` | Génère un doc fictif adapté à la question | +20 à +40% de recall sur les questions abstraites |
| `MultiQueryService` | Reformule la question sous N angles | Couvre des vocabulaires différents |
| `ReciprocalRankFusion` | Fusionne N listes de résultats | Élimine les biais d'une seule recherche |
| `LlmRerankerService` | Score sémantique 0-10 par chunk | Élimine les faux positifs cosinus |

### Stack complète (identique à Step 1)

| Technologie | Version | Usage |
|------------|---------|-------|
| .NET / ASP.NET Core | 10.0 | Framework + API REST |
| EF Core + Npgsql | 10.0 | ORM PostgreSQL |
| pgvector | 0.3.0 | Recherche vectorielle cosinus |
| Microsoft.Extensions.AI | 10.5.0 | Abstraction `IChatClient` multi-provider |
| Semantic Kernel | 1.74.0 | Templates de prompts |
| MediatR | 14.1.0 | CQRS (commandes uniquement) |
| React | 19.2.6 | Framework UI |
| TypeScript | 6.0 | Typage statique |
| Vite | 8.0 | Build tool + dev server |

---

## ← [Retour à Step 1 — RAG Fondamental](../Step%201/README.md)
