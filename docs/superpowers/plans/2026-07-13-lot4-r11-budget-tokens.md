# Lot 4 (R-11) — Budget de tokens du contexte RAG — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Empêcher la troncature silencieuse du prompt envoyé au LLM local (fenêtre ~4096 tokens) en comptant les tokens du contexte RAG (system prompt + historique + question + extraits) et en ne conservant, par ordre de pertinence, que les extraits qui tiennent dans le budget restant.

**Architecture:** Un `ITokenEstimator` (Domain, interface) implémenté par `CharacterTokenEstimator` (Infrastructure, heuristique caractères/token — le provider local n'expose aucun tokenizer BPE exploitable) fournit un comptage approximatif mais suffisant comme garde-fou. `RagPipelineService.BuildChatHistoryAsync` calcule le budget restant après system prompt + historique + question, puis sélectionne les extraits via `TokenBudgetSelector.SelectWithinBudget` (Infrastructure, statique, générique, pur — testable sans mock). Le budget est actif par défaut (`TokenBudgetOptions.Enabled = true`) car il ne fait que compter/tronquer localement, sans appel LLM supplémentaire, et corrige un bug de production déjà présent (troncature silencieuse).

**Tech Stack:** .NET 10 / C# 13, xUnit + FluentAssertions (tests), Microsoft.Extensions.Options (binding config).

## Global Constraints

- Toutes les réponses, tous les commentaires de code et toute la documentation produite sont en **français**, sans exception (règle CLAUDE.md).
- Chaque classe/méthode/bloc logique non trivial reçoit un commentaire XML (`///`) — convention du projet.
- Respect strict de la Clean Architecture : Domain ne référence rien, Application référence Domain uniquement, Infrastructure référence Domain + Application. **Écart au design initial (`docs/superpowers/specs/2026-07-13-lot4-r11-budget-tokens-design.md`) corrigé dans ce plan** : le design plaçait `CharacterTokenEstimator` dans `AIExperience.Rag.Application/Services/`, mais cette classe dépend de `IOptions<RagOptions>` — or `RagOptions` est défini dans `AIExperience.Rag.Infrastructure.Options` (vérifié dans le code actuel). Une classe Application ne peut pas référencer un type Infrastructure. `CharacterTokenEstimator` est donc placée dans `AIExperience.Rag.Infrastructure/AI/Rag/`, aux côtés de `RagStrategyResolver`/`TokenBudgetSelector` qui suivent déjà ce pattern (utilitaires purs référençant `RagOptions`).
- Aucun commit automatique. Le projet fonctionne en stage-only sur `main` (pas de branche, pas de worktree) : chaque étape qui modifie des fichiers se termine par `git add` (jamais `git commit`) — Geoffrey commit lui-même, à son rythme.
- Setters privés + factory statique pour les entités Domain — non applicable ici (aucune nouvelle entité, uniquement des options/services).
- Options Pattern : toute configuration nouvelle est une classe `*Options` liée à une section `appsettings.json` via binding existant (`services.AddOptions<RagOptions>().Bind(configuration.GetSection("RagOptions"))` déjà en place — pas de nouvelle registration à ajouter pour une propriété imbriquée).

---

## File Structure

| Fichier | Rôle |
|---|---|
| `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITokenEstimator.cs` | **Créer** — interface Domain, une méthode `EstimateTokens(string) : int` |
| `Step3/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs` | **Modifier** — ajoute `TokenBudgetOptions` + propriété `RagOptions.TokenBudget` |
| `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/CharacterTokenEstimator.cs` | **Créer** — implémentation heuristique caractères/token |
| `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/TokenBudgetSelector.cs` | **Créer** — sélecteur générique statique pur (budget) |
| `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs` | **Modifier** — factorisation `BuildContextChunksAsync`, extraction `FormatChunkForContext`, sélection sous budget dans `BuildChatHistoryAsync`, remplacement des littéraux `2000` |
| `Step3/src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs` | **Modifier** — enregistrement `ITokenEstimator` |
| `Step3/src/Back/AIExperience.Tests/CharacterTokenEstimatorTests.cs` | **Créer** — tests unitaires |
| `Step3/src/Back/AIExperience.Tests/TokenBudgetSelectorTests.cs` | **Créer** — tests unitaires |
| `Step3/src/Back/AIExperience.Web.Api/appsettings.json` | **Modifier** — section `RagOptions.TokenBudget` explicite |
| `Step3/src/Back/AIExperience.App.Console/appsettings.json` | **Modifier** — idem |

---

### Task 1: `ITokenEstimator` (Domain) + `TokenBudgetOptions` (Infrastructure)

**Files:**
- Create: `Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITokenEstimator.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs`

**Interfaces:**
- Produces: `ITokenEstimator.EstimateTokens(string text) : int` (consommé par Task 2 et Task 5) ; `RagOptions.TokenBudget : TokenBudgetOptions` avec propriétés `Enabled`, `ModelContextTokens`, `ReservedOutputTokens`, `ContextBudgetRatio`, `CharsPerToken` (consommées par Task 2 et Task 5).

Ce sont des types de données/contrats purs (pas de logique à tester unitairement) — aucun test dédié, comme les autres classes `*Options` existantes du fichier (`HydeOptions`, `RerankerOptions`...).

- [ ] **Step 1: Créer l'interface `ITokenEstimator`**

```csharp
namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Estime le coût en tokens d'un texte pour budgétiser le contexte envoyé au LLM (R-11).
/// </summary>
public interface ITokenEstimator
{
    /// <summary>
    /// Estime le nombre de tokens d'un texte. Approximation par nature (heuristique caractères,
    /// voir <see cref="AIExperience.Rag.Infrastructure.AI.Rag.CharacterTokenEstimator"/>) — utilisée
    /// uniquement comme garde-fou de budget, pas comme comptage facturable exact.
    /// </summary>
    /// <param name="text">Texte à estimer (peut être vide ou nul).</param>
    /// <returns>Nombre de tokens estimé, toujours positif ou nul.</returns>
    int EstimateTokens(string? text);
}
```

- [ ] **Step 2: Ajouter `TokenBudgetOptions` et la propriété `RagOptions.TokenBudget`**

Dans `Step3/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs`, ajouter la propriété dans `RagOptions` :

```csharp
    /// <summary>Options du budget de tokens du contexte RAG (R-11).</summary>
    public TokenBudgetOptions TokenBudget { get; set; } = new();
```

(à insérer juste après la propriété `Cache`, avant la fermeture de la classe `RagOptions`)

Puis ajouter la classe en bas du fichier, après `CacheOptions` :

```csharp
/// <summary>
/// Options du budget de tokens du contexte RAG (R-11). Empêche la troncature silencieuse du prompt
/// par le serveur LLM local quand system prompt + historique + extraits dépassent la fenêtre de
/// contexte du modèle.
/// </summary>
public sealed class TokenBudgetOptions
{
    /// <summary>
    /// Active ou désactive le budget de tokens. Par défaut : <c>true</c> — contrairement à
    /// Reranker/ContextCompression/Cache (désactivés par défaut car ils ajoutent des appels LLM
    /// coûteux), ce budget ne fait que compter/tronquer localement (aucun coût supplémentaire) et
    /// corrige un risque de troncature silencieuse déjà présent en production.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Taille de la fenêtre de contexte du modèle local, en tokens. Par défaut : 4096.</summary>
    public int ModelContextTokens { get; set; } = 4096;

    /// <summary>
    /// Tokens réservés à la sortie du LLM. Doit rester cohérent avec la valeur de
    /// <c>MaxOutputTokens</c> passée à <see cref="Microsoft.Extensions.AI.ChatOptions"/> dans
    /// <c>RagPipelineService</c> (qui utilise directement cette valeur). Par défaut : 2000.
    /// </summary>
    public int ReservedOutputTokens { get; set; } = 2000;

    /// <summary>
    /// Plafond de sécurité : le contexte documentaire ne dépasse jamais ce ratio de la fenêtre totale,
    /// même si le budget calculé (fenêtre - sortie - historique) serait plus généreux — laisse une
    /// marge pour le gabarit de prompt et l'imprécision de l'estimation. Par défaut : 0.6.
    /// </summary>
    public double ContextBudgetRatio { get; set; } = 0.6;

    /// <summary>Ratio caractères/token de l'estimation heuristique. Par défaut : 4.0.</summary>
    public double CharsPerToken { get; set; } = 4.0;
}
```

- [ ] **Step 3: Compiler pour vérifier qu'il n'y a pas d'erreur**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 Error(s)

- [ ] **Step 4: Stage (pas de commit)**

```bash
git add "Step3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ITokenEstimator.cs" "Step3/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs"
```

---

### Task 2: `CharacterTokenEstimator` (Infrastructure) + tests + DI

**Files:**
- Create: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/CharacterTokenEstimator.cs`
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs`
- Test: `Step3/src/Back/AIExperience.Tests/CharacterTokenEstimatorTests.cs`

**Interfaces:**
- Consumes: `ITokenEstimator` (Task 1), `RagOptions.TokenBudget.CharsPerToken` (Task 1)
- Produces: `CharacterTokenEstimator : ITokenEstimator`, enregistré dans le conteneur DI — consommé par `RagPipelineService` (Task 5) via injection de `ITokenEstimator`

- [ ] **Step 1: Écrire les tests (échouent — la classe n'existe pas encore)**

```csharp
using AIExperience.Rag.Infrastructure.AI.Rag;
using AIExperience.Rag.Infrastructure.Options;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="CharacterTokenEstimator"/> (R-11) : vérifie l'estimation heuristique
/// caractères/token utilisée comme garde-fou de budget (pas un comptage facturable exact).
/// </summary>
public sealed class CharacterTokenEstimatorTests
{
    private static CharacterTokenEstimator CreateSut(double charsPerToken = 4.0) =>
        new(Options.Create(new RagOptions { TokenBudget = new TokenBudgetOptions { CharsPerToken = charsPerToken } }));

    [Fact]
    public void EstimateTokens_EmptyText_ReturnsZero()
    {
        var sut = CreateSut();

        sut.EstimateTokens(string.Empty).Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_NullText_ReturnsZero()
    {
        var sut = CreateSut();

        sut.EstimateTokens(null).Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_ShortText_UsesConfiguredCharsPerToken()
    {
        var sut = CreateSut(charsPerToken: 4.0);

        // 8 caractères / 4.0 = 2 tokens exactement
        sut.EstimateTokens("12345678").Should().Be(2);
    }

    [Fact]
    public void EstimateTokens_PartialToken_RoundsUp()
    {
        var sut = CreateSut(charsPerToken: 4.0);

        // 9 caractères / 4.0 = 2.25 → arrondi à 3 (pessimiste, jamais sous-estimer le coût réel)
        sut.EstimateTokens("123456789").Should().Be(3);
    }

    [Fact]
    public void EstimateTokens_DifferentCharsPerToken_ChangesResult()
    {
        var sut = CreateSut(charsPerToken: 2.0);

        // 8 caractères / 2.0 = 4 tokens
        sut.EstimateTokens("12345678").Should().Be(4);
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter CharacterTokenEstimatorTests`
Expected: FAIL avec une erreur de compilation (`CharacterTokenEstimator` introuvable)

- [ ] **Step 3: Implémenter `CharacterTokenEstimator`**

```csharp
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Estime le nombre de tokens d'un texte par une heuristique caractères/token, configurée via
/// <see cref="TokenBudgetOptions.CharsPerToken"/>. Le provider local (LM Studio/Ollama, modèles
/// type Llama) n'expose aucun tokenizer BPE standard exploitable ; une bibliothèque comme
/// Microsoft.ML.Tokenizers donnerait un compte exact mais pour le mauvais tokenizer (cl100k/o200k
/// ≠ vocabulaire Llama/SentencePiece). Le budget de tokens est un garde-fou de sécurité, pas une
/// contrainte exacte : une estimation légèrement pessimiste (arrondi au plafond) est acceptable.
/// </summary>
public sealed class CharacterTokenEstimator(IOptions<RagOptions> options) : ITokenEstimator
{
    /// <inheritdoc/>
    public int EstimateTokens(string? text) =>
        string.IsNullOrEmpty(text)
            ? 0
            : (int)Math.Ceiling(text.Length / options.Value.TokenBudget.CharsPerToken);
}
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter CharacterTokenEstimatorTests`
Expected: PASS — 5/5 tests réussis

- [ ] **Step 5: Enregistrer `ITokenEstimator` dans le conteneur DI**

Dans `Step3/src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs`, méthode `ConfigureAiService()` (ligne ~246-259), ajouter la registration aux côtés des autres services du pipeline RAG :

```csharp
    private static IServiceCollection ConfigureAiService(this IServiceCollection services)
    {
        services.AddScoped<IAdaptiveQueryRouter, AdaptiveQueryRouter>();
        services.AddScoped<IContextCompressorService, ContextCompressorService>();

        // Stratégies de récupération avancées (Step 2 — Axe 1)
        services.AddScoped<IHydeService, HydeService>();
        services.AddScoped<IMultiQueryService, MultiQueryService>();
        services.AddScoped<IRerankerService, LlmRerankerService>();

        // Budget de tokens du contexte RAG (R-11) — sans état, réutilisable en concurrence.
        services.AddSingleton<ITokenEstimator, CharacterTokenEstimator>();

        services.AddScoped<IRagPipelineService, RagPipelineService>();

        return services;
    }
```

- [ ] **Step 6: Compiler pour vérifier**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 Error(s)

- [ ] **Step 7: Stage (pas de commit)**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/CharacterTokenEstimator.cs" "Step3/src/Back/AIExperience.Rag.Infrastructure/DependencyInjection.cs" "Step3/src/Back/AIExperience.Tests/CharacterTokenEstimatorTests.cs"
```

---

### Task 3: `TokenBudgetSelector` (Infrastructure, statique, générique, pur) + tests

**Files:**
- Create: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/TokenBudgetSelector.cs`
- Test: `Step3/src/Back/AIExperience.Tests/TokenBudgetSelectorTests.cs`

**Interfaces:**
- Produces: `TokenBudgetSelector.SelectWithinBudget<T>(IReadOnlyList<T> rankedItems, Func<T, int> costOf, int budgetTokens) : IReadOnlyList<T>` — consommé par `RagPipelineService.BuildChatHistoryAsync` (Task 5)

- [ ] **Step 1: Écrire les tests (échouent — la classe n'existe pas encore)**

```csharp
using AIExperience.Rag.Infrastructure.AI.Rag;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="TokenBudgetSelector"/> (R-11) : sélecteur générique pur, testé avec de
/// simples entiers (aucune dépendance à <c>DocumentChunk</c>), même famille que
/// <c>RagStrategyResolver.ResolveFallback</c>.
/// </summary>
public sealed class TokenBudgetSelectorTests
{
    [Fact]
    public void SelectWithinBudget_EmptyList_ReturnsEmpty()
    {
        var result = TokenBudgetSelector.SelectWithinBudget(
            Array.Empty<int>(), costOf: x => x, budgetTokens: 100);

        result.Should().BeEmpty();
    }

    [Fact]
    public void SelectWithinBudget_AllItemsFitInBudget_ReturnsAll()
    {
        var items = new[] { 10, 20, 30 };

        var result = TokenBudgetSelector.SelectWithinBudget(items, costOf: x => x, budgetTokens: 100);

        result.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void SelectWithinBudget_PartialOverflow_StopsBeforeFirstItemThatOverflows()
    {
        // 10 + 20 = 30 (tient), + 30 = 60 (tient), + 50 = 110 (dépasse 100) → arrêt avant le 4e
        var items = new[] { 10, 20, 30, 50 };

        var result = TokenBudgetSelector.SelectWithinBudget(items, costOf: x => x, budgetTokens: 100);

        result.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void SelectWithinBudget_FirstItemAloneExceedsBudget_IsStillIncluded()
    {
        // Garantit toujours au moins 1 élément (jamais un contexte vide alors qu'un chunk
        // pertinent mais volumineux existe) — laisser R-4 se déclencher serait un faux négatif.
        var items = new[] { 500 };

        var result = TokenBudgetSelector.SelectWithinBudget(items, costOf: x => x, budgetTokens: 100);

        result.Should().Equal(500);
    }

    [Fact]
    public void SelectWithinBudget_RelevanceOrderIsPreserved()
    {
        // L'ordre d'entrée (pertinence décroissante) doit être respecté dans le résultat.
        var items = new[] { 40, 30, 20, 10 };

        var result = TokenBudgetSelector.SelectWithinBudget(items, costOf: x => x, budgetTokens: 95);

        result.Should().Equal(40, 30, 20);
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter TokenBudgetSelectorTests`
Expected: FAIL avec une erreur de compilation (`TokenBudgetSelector` introuvable)

- [ ] **Step 3: Implémenter `TokenBudgetSelector`**

```csharp
namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Sélectionne, dans l'ordre de pertinence, les éléments dont la somme des coûts ne dépasse pas le
/// budget donné (R-11). Extrait en méthode statique générique pure (aucune dépendance à
/// <c>DocumentChunk</c>) pour rester testable sans mock, comme <see cref="RagStrategyResolver"/> et
/// <c>BatchedRerankResponseParser</c>.
/// </summary>
public static class TokenBudgetSelector
{
    /// <summary>
    /// Parcourt <paramref name="rankedItems"/> dans l'ordre et accumule les éléments tant que le
    /// budget n'est pas dépassé. Garantit toujours au moins 1 élément en résultat (même si son coût
    /// dépasse seul le budget), pour éviter un contexte vide alors qu'un élément pertinent existe.
    /// </summary>
    /// <param name="rankedItems">Éléments déjà triés par pertinence décroissante.</param>
    /// <param name="costOf">Fonction de coût en tokens d'un élément.</param>
    /// <param name="budgetTokens">Budget total disponible, en tokens.</param>
    /// <returns>Sous-ensemble ordonné de <paramref name="rankedItems"/> tenant dans le budget.</returns>
    public static IReadOnlyList<T> SelectWithinBudget<T>(
        IReadOnlyList<T> rankedItems, Func<T, int> costOf, int budgetTokens)
    {
        var selected = new List<T>();
        var used = 0;
        foreach (var item in rankedItems)
        {
            var cost = costOf(item);
            if (selected.Count > 0 && used + cost > budgetTokens) break;
            selected.Add(item);
            used += cost;
        }
        return selected;
    }
}
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test "Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj" --filter TokenBudgetSelectorTests`
Expected: PASS — 5/5 tests réussis

- [ ] **Step 5: Compiler pour vérifier**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 Error(s)

- [ ] **Step 6: Stage (pas de commit)**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/TokenBudgetSelector.cs" "Step3/src/Back/AIExperience.Tests/TokenBudgetSelectorTests.cs"
```

---

### Task 4: Factoriser `BuildContextChunksAsync` dans `RagPipelineService` (dédup reranking + compression)

Refactor pur, sans changement de comportement : `AskAsync` (lignes 81-97) et `AskStreamAsync`
(lignes 181-196) dupliquent aujourd'hui le reranking + la compression avant `BuildChatHistoryAsync`.
Ce bloc est factorisé en une méthode privée commune, prérequis pour insérer proprement la sélection
sous budget (Task 5) sans tripler la logique. Aucun nouveau test : comportement identique, vérifié
par la suite de tests existante qui doit continuer à passer sans régression.

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs`

**Interfaces:**
- Consumes: `IRerankerService.RerankAsync`, `IContextCompressorService.CompressAsync` (déjà injectés dans le constructeur, inchangés)
- Produces: `BuildContextChunksAsync(RagQuery query, IReadOnlyList<(DocumentChunk Chunk, double Score)> rankedChunks, RagOptions ragOptions, CancellationToken ct) : Task<(IReadOnlyList<(DocumentChunk Chunk, double Score)> RankedChunks, IReadOnlyList<DocumentChunk> ContextChunks)>` — consommé par `AskAsync`/`AskStreamAsync` dans cette même tâche, et par Task 5 indirectement (aucun changement de signature attendu depuis Task 5)

- [ ] **Step 1: Ajouter la méthode privée `BuildContextChunksAsync`**

Dans `RagPipelineService.cs`, juste avant `BuildChatHistoryAsync` (avant la ligne `private async Task<ChatHistory> BuildChatHistoryAsync(`), ajouter :

```csharp
        /// <summary>
        /// Applique le reclassement (reranking) puis la compression du contexte sur les chunks
        /// récupérés, selon les flags de configuration. Factorisé depuis <see cref="AskAsync"/> et
        /// <see cref="AskStreamAsync"/> qui dupliquaient cette logique — prérequis pour insérer
        /// proprement la sélection sous budget de tokens (R-11) sans la tripler.
        /// </summary>
        private async Task<(IReadOnlyList<(DocumentChunk Chunk, double Score)> RankedChunks, IReadOnlyList<DocumentChunk> ContextChunks)> BuildContextChunksAsync(
            RagQuery query,
            IReadOnlyList<(DocumentChunk Chunk, double Score)> rankedChunks,
            RagOptions ragOptions,
            CancellationToken ct)
        {
            // Reclassement par pertinence réelle — corrige les faux positifs du cosinus
            if (ragOptions.Reranker.Enabled && rankedChunks.Count > 0)
                rankedChunks = await rerankerService.RerankAsync(
                    query.Question, rankedChunks, ragOptions.Reranker.TopKAfterRerank, ct);

            // Compression du contexte — réduit les tokens envoyés au LLM en conservant les phrases
            // pertinentes. Fallback sur les chunks bruts si désactivée ou si le résultat est vide.
            IReadOnlyList<DocumentChunk> contextChunks;
            if (ragOptions.ContextCompression.Enabled && rankedChunks.Count > 0)
            {
                var compressed = await contextCompressorService.CompressAsync(
                    query.Question, rankedChunks.Select(r => r.Chunk), ct);
                contextChunks = compressed.Count > 0 ? compressed : rankedChunks.Select(r => r.Chunk).ToList();
            }
            else
            {
                contextChunks = rankedChunks.Select(r => r.Chunk).ToList();
            }

            return (rankedChunks, contextChunks);
        }

```

- [ ] **Step 2: Remplacer le bloc dupliqué dans `AskAsync`**

Remplacer (lignes ~80-97) :

```csharp
            // 3. Reclassement par pertinence réelle — corrige les faux positifs du cosinus
            if (ragOptions.Reranker.Enabled && rankedChunks.Count > 0)
                rankedChunks = await rerankerService.RerankAsync(
                    query.Question, rankedChunks, ragOptions.Reranker.TopKAfterRerank, ct);

            // 4. Compression du contexte — réduit les tokens envoyés au LLM en conservant les phrases pertinentes.
            //    Fallback sur les chunks bruts si la compression est désactivée ou retourne vide.
            IReadOnlyList<DocumentChunk> contextChunks;
            if (ragOptions.ContextCompression.Enabled && rankedChunks.Count > 0)
            {
                var compressed = await contextCompressorService.CompressAsync(
                    query.Question, rankedChunks.Select(r => r.Chunk), ct);
                contextChunks = compressed.Count > 0 ? compressed : rankedChunks.Select(r => r.Chunk).ToList();
            }
            else
            {
                contextChunks = rankedChunks.Select(r => r.Chunk).ToList();
            }
```

par :

```csharp
            // 3-4. Reclassement (reranking) puis compression du contexte — factorisés dans
            // BuildContextChunksAsync (partagé avec AskStreamAsync).
            IReadOnlyList<DocumentChunk> contextChunks;
            (rankedChunks, contextChunks) = await BuildContextChunksAsync(query, rankedChunks, ragOptions, ct);
```

- [ ] **Step 3: Remplacer le bloc dupliqué (identique) dans `AskStreamAsync`**

Remplacer (lignes ~180-196, même contenu que Step 2 avec les commentaires "3. Reclassement..." / "4. Compression du contexte") par la même construction :

```csharp
            // 3-4. Reclassement (reranking) puis compression du contexte — factorisés dans
            // BuildContextChunksAsync (partagé avec AskAsync).
            IReadOnlyList<DocumentChunk> contextChunks;
            (rankedChunks, contextChunks) = await BuildContextChunksAsync(query, rankedChunks, ragOptions, ct);
```

- [ ] **Step 4: Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 Error(s)

- [ ] **Step 5: Lancer la suite complète de tests pour vérifier l'absence de régression**

Run: `dotnet test "Step3/src/Back/AIExperience.slnx"`
Expected: PASS — tous les tests existants passent toujours (aucun changement de comportement)

- [ ] **Step 6: Stage (pas de commit)**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs"
```

---

### Task 5: Sélection sous budget dans `BuildChatHistoryAsync` + remplacement des littéraux `2000`

Cœur de la fonctionnalité R-11. `BuildChatHistoryAsync` reçoit désormais `ragOptions`, calcule le
budget de tokens restant après system prompt + historique + question, sélectionne les extraits via
`TokenBudgetSelector` (Task 3) en utilisant `ITokenEstimator` (Task 1/2), et renvoie les chunks
réellement utilisés pour que les citations ne pointent jamais vers un extrait écarté par le budget
et jamais vu par le LLM. Les 4 occurrences de `MaxOutputTokens = 2000` sont remplacées par
`ragOptions.TokenBudget.ReservedOutputTokens`.

**Files:**
- Modify: `Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs`

**Interfaces:**
- Consumes: `ITokenEstimator.EstimateTokens` (Task 1/2), `TokenBudgetSelector.SelectWithinBudget<T>` (Task 3), `RagOptions.TokenBudget.*` (Task 1)
- Produces: `BuildChatHistoryAsync(RagQuery query, IReadOnlyList<DocumentChunk> candidateChunks, RagOptions ragOptions, CancellationToken ct) : Task<(ChatHistory History, IReadOnlyList<DocumentChunk> UsedChunks)>` (signature changée — consommée par `AskAsync`/`AskStreamAsync` dans cette même tâche) ; `FormatChunkForContext(DocumentChunk chunk, int index) : string` (privée, utilisée en interne)

Pas de nouveau test unitaire sur `RagPipelineService` : la méthode est privée avec 10 dépendances
injectées (même arbitrage déjà pris pour R-16/R-9/R-17 dans ce fichier — vérifié manuellement plutôt
que mocké). La couverture vient des tests déjà écrits sur `CharacterTokenEstimator` (Task 2) et
`TokenBudgetSelector` (Task 3), plus la suite complète existante qui doit continuer à passer
(comportement inchangé quand `TokenBudget.Enabled = false`).

- [ ] **Step 1: Ajouter `ITokenEstimator tokenEstimator` au constructeur primaire**

Modifier la déclaration de classe (lignes 28-39) :

```csharp
    public sealed class RagPipelineService(
        IVectorStoreService vectorStoreService,
        IEmbeddingService embeddingService,
        IAdaptiveQueryRouter adaptiveQueryRouter,
        IHydeService hydeService,
        IMultiQueryService multiQueryService,
        IRerankerService rerankerService,
        IContextCompressorService contextCompressorService,
        IConversationRepository conversationRepository,
        IOptions<RagOptions> options,
        IChatClient chatClient,
        ITokenEstimator tokenEstimator,
        ILogger<RagPipelineService> logger) : IRagPipelineService
```

- [ ] **Step 2: Remplacer `BuildChatHistoryAsync` par la version avec sélection sous budget**

Remplacer intégralement la méthode (lignes ~473-528, de `/// <summary>` juste avant
`BuildChatHistoryAsync` jusqu'à l'accolade fermante de la méthode) par :

```csharp
        /// <summary>
        /// Construit le ChatHistory Semantic Kernel avec le prompt système, l'historique de
        /// conversation et le contexte documentaire sélectionné sous budget de tokens (R-11).
        /// </summary>
        /// <returns>
        /// Le <see cref="ChatHistory"/> prêt pour l'appel LLM, et les chunks réellement retenus
        /// (<c>UsedChunks</c>) — un sous-ensemble de <paramref name="candidateChunks"/> si le budget
        /// de tokens en a écarté. Les citations doivent être construites depuis <c>UsedChunks</c>,
        /// jamais depuis <paramref name="candidateChunks"/>, pour ne jamais citer un extrait non vu
        /// par le LLM.
        /// </returns>
        private async Task<(ChatHistory History, IReadOnlyList<DocumentChunk> UsedChunks)> BuildChatHistoryAsync(
            RagQuery query,
            IReadOnlyList<DocumentChunk> candidateChunks,
            RagOptions ragOptions,
            CancellationToken ct)
        {
            var chatHistory = new ChatHistory(query.SystemPrompt ?? RagPrompts.RagSystem);

            // Injection de l'historique de conversation multi-tour. Le contenu de chaque message est
            // aussi conservé à part (historyContents) pour le calcul du budget de tokens ci-dessous,
            // sans dépendre du type exact retourné par GetMessagesAsync.
            var historyContents = new List<string>();
            if (query.IncludeHistory && query.SessionId != Guid.Empty)
            {
                var pastMessages = await conversationRepository.GetMessagesAsync(
                    query.SessionId, query.MaxHistoryTurns, ct);

                foreach (var msg in pastMessages)
                {
                    if (msg.Role == MessageRole.User)
                        chatHistory.AddUserMessage(msg.Content);
                    else if (msg.Role == MessageRole.Assistant)
                        chatHistory.AddAssistantMessage(msg.Content);
                    historyContents.Add(msg.Content);
                }
            }

            // Sélection sous budget de tokens (R-11). Le budget dépend du coût de l'historique,
            // connu seulement ici (juste après sa récupération ci-dessus) — c'est pourquoi la
            // sélection est appliquée à l'intérieur de BuildChatHistoryAsync et non en amont.
            var usedChunks = candidateChunks;
            var tb = ragOptions.TokenBudget;
            if (tb.Enabled)
            {
                var overhead = tokenEstimator.EstimateTokens(query.SystemPrompt ?? RagPrompts.RagSystem)
                              + historyContents.Sum(tokenEstimator.EstimateTokens)
                              + tokenEstimator.EstimateTokens(query.Question);
                var available = tb.ModelContextTokens - tb.ReservedOutputTokens - overhead;
                var contextBudget = Math.Max(0, Math.Min(available, (int)(tb.ModelContextTokens * tb.ContextBudgetRatio)));

                var withCosts = candidateChunks
                    .Select((chunk, i) => (chunk, cost: tokenEstimator.EstimateTokens(FormatChunkForContext(chunk, i + 1))))
                    .ToList();
                var kept = TokenBudgetSelector.SelectWithinBudget(withCosts, x => x.cost, contextBudget);
                if (kept.Count < withCosts.Count)
                    logger.LogWarning(
                        "Budget de tokens dépassé : {Dropped} extrait(s) écarté(s) sur {Total} (budget {Budget} tokens)",
                        withCosts.Count - kept.Count, withCosts.Count, contextBudget);

                usedChunks = kept.Select(x => x.chunk).ToList();
            }

            // Construction du contexte documentaire depuis les chunks retenus (usedChunks — un
            // sous-ensemble de candidateChunks si le budget en a écarté).
            var contextBuilder = new StringBuilder();
            foreach (var (chunk, i) in usedChunks.Select((c, i) => (c, i + 1)))
                contextBuilder.Append(FormatChunkForContext(chunk, i));

            var userPrompt = RagPrompts.RagUser
                .Replace("{context}", contextBuilder.ToString())
                .Replace("{question}", query.Question);

            chatHistory.AddUserMessage(userPrompt);
            return (chatHistory, usedChunks);
        }

        /// <summary>
        /// Formate un chunk pour l'injection dans le prompt (source lisible, page/section/horodatage
        /// optionnels, puis contenu). Réutilisé pour l'estimation du coût en tokens d'un extrait
        /// (budget R-11) et pour la construction finale du prompt, afin de compter exactement ce qui
        /// est envoyé au LLM — sans cette réutilisation, le budget compterait un texte différent de
        /// celui réellement transmis.
        /// </summary>
        private static string FormatChunkForContext(DocumentChunk chunk, int index)
        {
            var source = chunk.DocumentName ?? "Document inconnu";
            var page   = chunk.PageNumber is { } p ? $", p.{p}" : string.Empty;
            var section = string.IsNullOrWhiteSpace(chunk.SectionTitle)
                ? string.Empty
                : $", Section: {chunk.SectionTitle}";
            // Plage temporelle vidéo injectée dans l'en-tête (le corps du chunk reste du texte pur)
            // — permet au LLM de citer un horodatage.
            var time = chunk.StartTime is { } start && chunk.EndTime is { } end
                ? $", {start:hh\\:mm\\:ss}–{end:hh\\:mm\\:ss}"
                : string.Empty;

            return $"[Extrait {index}] Source: {source}{page}{section}{time}{Environment.NewLine}{chunk.Content}{Environment.NewLine}{Environment.NewLine}";
        }
```

- [ ] **Step 3: Mettre à jour l'appel dans `AskAsync`**

Remplacer (ligne ~115) :

```csharp
            // 5. Construction du prompt et appel au LLM
            var chatHistory = await BuildChatHistoryAsync(query, contextChunks, ct);
            var completionResult = await chatClient.GetResponseAsync(
                chatHistory.Select(m => new Microsoft.Extensions.AI.ChatMessage(
                    m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)).ToList(),
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.1f },
                ct);
```

par :

```csharp
            // 5. Construction du prompt (sous budget de tokens, R-11) et appel au LLM
            var (chatHistory, usedChunks) = await BuildChatHistoryAsync(query, contextChunks, ragOptions, ct);
            var completionResult = await chatClient.GetResponseAsync(
                chatHistory.Select(m => new Microsoft.Extensions.AI.ChatMessage(
                    m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)).ToList(),
                new ChatOptions { MaxOutputTokens = ragOptions.TokenBudget.ReservedOutputTokens, Temperature = 0.1f },
                ct);
```

Puis dans la construction des citations juste après (ligne ~124-138), remplacer `contextChunks.Select(chunk => Citation.Create(` par `usedChunks.Select(chunk => Citation.Create(` (les citations doivent porter sur les chunks réellement vus par le LLM, pas sur `contextChunks` qui peut en contenir davantage si le budget en a écarté).

- [ ] **Step 4: Mettre à jour l'appel dans `AskStreamAsync`**

Remplacer (ligne ~218) :

```csharp
            // 5. Construction du prompt
            var chatHistory = await BuildChatHistoryAsync(query, contextChunks, ct);
            var messages = chatHistory
                .Select(m => new Microsoft.Extensions.AI.ChatMessage(
                    m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content))
                .ToList();

            // 6. Streaming LLM — chaque token est yield immédiatement
            var totalText = new StringBuilder();
            await foreach (var update in chatClient.GetStreamingResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.1f },
                ct))
```

par :

```csharp
            // 5. Construction du prompt (sous budget de tokens, R-11)
            var (chatHistory, usedChunks) = await BuildChatHistoryAsync(query, contextChunks, ragOptions, ct);
            var messages = chatHistory
                .Select(m => new Microsoft.Extensions.AI.ChatMessage(
                    m.Role == AuthorRole.User ? ChatRole.User : ChatRole.Assistant, m.Content))
                .ToList();

            // 6. Streaming LLM — chaque token est yield immédiatement
            var totalText = new StringBuilder();
            await foreach (var update in chatClient.GetStreamingResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = ragOptions.TokenBudget.ReservedOutputTokens, Temperature = 0.1f },
                ct))
```

Puis dans la construction des citations juste après (ligne ~243-253), remplacer
`contextChunks.Select(chunk => Citation.Create(` par `usedChunks.Select(chunk => Citation.Create(`.

- [ ] **Step 5: Remplacer les 2 littéraux restants dans `AskDirectLlmAsync`/`StreamDirectLlmAsync`**

Ces deux méthodes ne reçoivent pas encore `ragOptions` — l'ajouter à leur signature.

Modifier la signature (ligne ~273) :
```csharp
        private async Task<RagResponse> AskDirectLlmAsync(RagQuery query, Stopwatch sw, CancellationToken ct)
```
en :
```csharp
        private async Task<RagResponse> AskDirectLlmAsync(RagQuery query, RagOptions ragOptions, Stopwatch sw, CancellationToken ct)
```

Dans le corps (ligne ~293), remplacer :
```csharp
            var completionResult = await chatClient.GetResponseAsync(messages,
                new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.7f }, ct);
```
par :
```csharp
            var completionResult = await chatClient.GetResponseAsync(messages,
                new ChatOptions { MaxOutputTokens = ragOptions.TokenBudget.ReservedOutputTokens, Temperature = 0.7f }, ct);
```

Modifier la signature (ligne ~309-310) :
```csharp
        private async IAsyncEnumerable<RagStreamChunk> StreamDirectLlmAsync(
            RagQuery query, Stopwatch sw, [EnumeratorCancellation] CancellationToken ct)
```
en :
```csharp
        private async IAsyncEnumerable<RagStreamChunk> StreamDirectLlmAsync(
            RagQuery query, RagOptions ragOptions, Stopwatch sw, [EnumeratorCancellation] CancellationToken ct)
```

Dans le corps (ligne ~331), remplacer :
```csharp
            await foreach (var update in chatClient.GetStreamingResponseAsync(
                messages, new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.7f }, ct))
```
par :
```csharp
            await foreach (var update in chatClient.GetStreamingResponseAsync(
                messages, new ChatOptions { MaxOutputTokens = ragOptions.TokenBudget.ReservedOutputTokens, Temperature = 0.7f }, ct))
```

Puis mettre à jour les deux points d'appel :
- Dans `AskAsync` (ligne ~72) : `return await AskDirectLlmAsync(query, sw, ct);` → `return await AskDirectLlmAsync(query, ragOptions, sw, ct);`
- Dans `AskStreamAsync` (ligne ~169) :
  ```csharp
  await foreach (var chunk in StreamDirectLlmAsync(query, sw, ct))
  ```
  →
  ```csharp
  await foreach (var chunk in StreamDirectLlmAsync(query, ragOptions, sw, ct))
  ```

- [ ] **Step 6: Compiler**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 Error(s)

- [ ] **Step 7: Lancer la suite complète de tests**

Run: `dotnet test "Step3/src/Back/AIExperience.slnx"`
Expected: PASS — tous les tests passent (existants + `CharacterTokenEstimatorTests` + `TokenBudgetSelectorTests` des tâches précédentes)

- [ ] **Step 8: Stage (pas de commit)**

```bash
git add "Step3/src/Back/AIExperience.Rag.Infrastructure/AI/Rag/RagPipelineService.cs"
```

---

### Task 6: Configuration `appsettings.json`

Ajoute une section `TokenBudget` explicite dans `RagOptions`, cohérente avec le fait que chaque
sous-section (`HyDE`, `MultiQuery`, `Retrieval`, `Reranker`, `ContextCompression`, `Cache`) est déjà
rendue explicite dans les deux `appsettings.json` du back-end, même quand la valeur reprend le
défaut C#.

**Files:**
- Modify: `Step3/src/Back/AIExperience.Web.Api/appsettings.json`
- Modify: `Step3/src/Back/AIExperience.App.Console/appsettings.json`

**Interfaces:**
- Consumes: `RagOptions.TokenBudget` (Task 1) via le binding déjà en place (`services.AddOptions<RagOptions>().Bind(configuration.GetSection("RagOptions"))`)

- [ ] **Step 1: Ajouter la section dans `AIExperience.Web.Api/appsettings.json`**

Dans la section `"RagOptions"`, juste après le bloc `"Cache"` (ligne ~34-37) :

```json
    "Cache": {
      "Enabled": true,
      "TtlMinutes": 60
    },
    "TokenBudget": {
      "Enabled": true,
      "ModelContextTokens": 4096,
      "ReservedOutputTokens": 2000,
      "ContextBudgetRatio": 0.6,
      "CharsPerToken": 4.0
    }
```

- [ ] **Step 2: Ajouter la même section dans `AIExperience.App.Console/appsettings.json`**

Même bloc, au même endroit (après `"Cache"`, ligne ~34-37 de ce fichier).

- [ ] **Step 3: Vérifier que le JSON reste valide**

Run: `Get-Content "Step3/src/Back/AIExperience.Web.Api/appsettings.json" -Raw | ConvertFrom-Json | Out-Null; Get-Content "Step3/src/Back/AIExperience.App.Console/appsettings.json" -Raw | ConvertFrom-Json | Out-Null`
Expected: Aucune erreur (le pipeline PowerShell échoue bruyamment si le JSON est invalide)

- [ ] **Step 4: Stage (pas de commit)**

```bash
git add "Step3/src/Back/AIExperience.Web.Api/appsettings.json" "Step3/src/Back/AIExperience.App.Console/appsettings.json"
```

---

### Task 7: Vérification finale de la solution complète

**Files:** Aucun (vérification uniquement)

- [ ] **Step 1: Build complet**

Run: `dotnet build "Step3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 Error(s), 0 Warning(s) nouveau

- [ ] **Step 2: Suite de tests complète**

Run: `dotnet test "Step3/src/Back/AIExperience.slnx"`
Expected: PASS — 100 % des tests passent (269 tests existants au 13/07 + 10 nouveaux tests de cette tâche = 279 attendus ; le nombre exact de tests existants peut avoir bougé, vérifier qu'il n'y a **aucun échec**, pas un total figé)

- [ ] **Step 3: `git status` pour lister l'ensemble des fichiers stagés, sans committer**

Run: `git status`
Expected: Tous les fichiers créés/modifiés de ce plan apparaissent en `Changes to be committed` — aucun commit n'est créé, Geoffrey commit lui-même quand il le décide.

---

## Hors périmètre (rappel du design)

- Troncature de l'historique par tokens (l'historique reste borné par `MaxHistoryTurns`, son coût est simplement déduit du budget restant pour les extraits).
- Exposition du détail du budget (tokens utilisés, extraits écartés) dans `RagResponse` — relève de R-12 (observabilité), un autre point du Lot 4.
- Implémentation `Microsoft.ML.Tokenizers` pour un comptage exact — pertinente seulement en cas de bascule vers OpenAI/Azure OpenAI.
