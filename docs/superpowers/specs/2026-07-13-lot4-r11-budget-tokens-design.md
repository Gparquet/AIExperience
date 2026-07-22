# Design — Lot 4 (R-11) : Budget de tokens du contexte RAG

Date : 2026-07-13
Périmètre : `Step3/src/Back`
Référence plan : `Step3/PLAN-AMELIORATION-RAG.md`, Lot 3/4, R-11 (lignes ~148, ~984-999)

---

## 1. Contexte et constat

`RagPipelineService.BuildChatHistoryAsync` concatène tous les `contextChunks` retenus (jusqu'à
`TopKAfterRerank` chunks de ~800 à ~1400 caractères chacun, plus l'historique de conversation)
sans jamais compter les tokens envoyés au LLM. `ChatOptions.MaxOutputTokens = 2000` borne
uniquement la **sortie** ; rien ne borne l'**entrée**. Au-delà de la fenêtre de contexte du modèle
local (~4096 tokens), le serveur LLM tronque silencieusement le début du prompt — souvent le
system prompt ou les premiers extraits — produisant des réponses incohérentes ou des citations
perdues.

Le pseudo-code du plan propose de construire le contexte sous budget : compter les tokens, inclure
les chunks par pertinence jusqu'à un plafond, réserver la place pour l'historique et la sortie.

## 2. Décisions de cadrage (validées avec Geoffrey)

1. **Comptage de tokens = heuristique caractères, cachée derrière une interface.** Le provider
   actuel (LM Studio/Ollama, modèles type Llama) n'expose aucun tokenizer BPE standard ; même une
   bibliothèque comme `Microsoft.ML.Tokenizers` donnerait un compte exact pour le **mauvais**
   tokenizer (cl100k/o200k ≠ vocabulaire Llama/SentencePiece). Le budget de tokens est un garde-fou
   de sécurité, pas une contrainte exacte : une estimation légèrement pessimiste est acceptable,
   voire préférable.
   **Note d'évolutivité** : le calcul est isolé derrière `ITokenEstimator` (Domain) pour ne pas
   fermer la porte. Si le provider bascule un jour sur OpenAI/Azure OpenAI (déjà supporté par
   `AiProviderOptions.Provider`), une implémentation `Microsoft.ML.Tokenizers` (cl100k_base pour
   GPT-3.5/4, o200k_base pour GPT-4o) donnerait alors un compte réellement exact — il suffira de
   brancher cette implémentation via DI, sans toucher au pipeline RAG.
2. **Budget actif par défaut** (`TokenBudget.Enabled = true`), contrairement à `Reranker`/
   `ContextCompression`/`Cache` (désactivés par défaut car ils ajoutent des appels LLM coûteux). Le
   budget de tokens ne fait que compter/tronquer localement — aucun coût supplémentaire — et
   corrige un risque de troncature silencieuse déjà présent en production : le laisser inactif par
   défaut recréerait exactement le bug que R-11 corrige.
3. **L'historique n'est pas tronqué par tokens.** Il est déjà borné par `MaxHistoryTurns` (défaut
   5). Le budget de tokens compte l'historique déjà sélectionné et déduit son coût du budget
   restant pour les chunks — fidèle au pseudo-code du plan. Une troncature de l'historique par
   tokens est jugée disproportionnée pour un cas marginal (accumulation de réponses très longues).

## 3. Architecture

```
RagOptions.TokenBudget (config)
        │
        ▼
ITokenEstimator (Domain)  ──implémenté par──▶  CharacterTokenEstimator (Application)
        │
        ▼
RagPipelineService.BuildChatHistoryAsync : récupère l'historique (comme aujourd'hui),
                      formate chaque chunk candidat (source, page, section, horodatage),
                      calcule system prompt + historique + question (overhead fixe),
                      budget_contexte = ModelContextTokens - ReservedOutputTokens - overhead,
                      plafonné à ContextBudgetRatio × ModelContextTokens
        ▼
TokenBudgetSelector.SelectWithinBudget<T>(...)  (Infrastructure, statique, générique, pur —
                                                  même pattern que RagStrategyResolver /
                                                  BatchedRerankResponseParser : testable sans mock)
```

Point important : le budget dépend du coût en tokens de l'historique, qui n'est connu qu'après
l'avoir récupéré (`conversationRepository.GetMessagesAsync`). Cette récupération n'a lieu
aujourd'hui qu'à l'intérieur de `BuildChatHistoryAsync`, appelée *après* que `contextChunks` soit
figé (étape 4 du pipeline). La sélection sous budget est donc appliquée **à l'intérieur de**
`BuildChatHistoryAsync`, pas dans un pré-traitement séparé — voir §4.3.

### 3.1. `RagOptions.TokenBudget` (nouvelle section)

```csharp
public sealed class TokenBudgetOptions
{
    /// <summary>Active ou désactive le budget de tokens. Par défaut : true (garde-fou de correction,
    /// pas une fonctionnalité optionnelle — contrairement à Reranker/ContextCompression/Cache).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Taille de la fenêtre de contexte du modèle local, en tokens. Par défaut : 4096.</summary>
    public int ModelContextTokens { get; set; } = 4096;

    /// <summary>Tokens réservés à la sortie du LLM. Doit rester cohérent avec ChatOptions.MaxOutputTokens
    /// (remplace les littéraux 2000 codés en dur dans RagPipelineService). Par défaut : 2000.</summary>
    public int ReservedOutputTokens { get; set; } = 2000;

    /// <summary>Plafond de sécurité : le contexte documentaire ne dépasse jamais ce ratio de la fenêtre
    /// totale, même si le budget calculé (fenêtre - sortie - historique) serait plus généreux — laisse
    /// une marge pour le gabarit de prompt et l'imprécision de l'estimation. Par défaut : 0.6.</summary>
    public double ContextBudgetRatio { get; set; } = 0.6;

    /// <summary>Ratio caractères/token de l'estimation heuristique. Par défaut : 4.0.</summary>
    public double CharsPerToken { get; set; } = 4.0;
}
```

`RagOptions` gagne `public TokenBudgetOptions TokenBudget { get; set; } = new();`.

### 3.2. `ITokenEstimator` (Domain) + `CharacterTokenEstimator` (Application)

```csharp
// Domain/Interfaces/Services/ITokenEstimator.cs
public interface ITokenEstimator
{
    /// <summary>Estime le nombre de tokens d'un texte. Approximation par nature (voir §2.1 du design) —
    /// utilisée uniquement comme garde-fou de budget, pas pour un comptage facturable exact.</summary>
    int EstimateTokens(string text);
}
```

```csharp
// Application/Services/CharacterTokenEstimator.cs
public sealed class CharacterTokenEstimator(IOptions<RagOptions> options) : ITokenEstimator
{
    public int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / options.Value.TokenBudget.CharsPerToken);
}
```

Enregistré dans `AddApplication()`, aux côtés de `RecursiveChunker`/`TemporalChunker` (même
famille : utilitaire de traitement de texte pur, pas de dépendance externe).

### 3.3. `TokenBudgetSelector` (Infrastructure, statique, générique)

```csharp
// Infrastructure/AI/Rag/TokenBudgetSelector.cs
public static class TokenBudgetSelector
{
    /// <summary>Sélectionne, dans l'ordre de pertinence, les éléments dont la somme des coûts ne
    /// dépasse pas le budget. Garantit toujours au moins 1 élément (même si son coût dépasse seul le
    /// budget) pour éviter un contexte vide alors qu'un chunk pertinent mais volumineux existe —
    /// laisser R-4 (garde-fou contexte vide) se déclencher dans ce cas serait un faux négatif.</summary>
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

Générique et pur (aucune dépendance à `DocumentChunk`) — testable avec de simples entiers, sans
mock, même famille que `RagStrategyResolver.ResolveFallback`.

## 4. Intégration dans `RagPipelineService`

### 4.1. Déduplication préalable (reranking + compression)

`AskAsync` et `AskStreamAsync` dupliquent aujourd'hui la construction de `contextChunks` avant
l'appel à `BuildChatHistoryAsync` (reranking + compression, lignes ~87-97 et ~186-195). Ce bloc est
factorisé dans une méthode privée commune `BuildContextChunksAsync(RagQuery, List<ScoredChunk>
rankedChunks, RagOptions, CancellationToken) : Task<IReadOnlyList<DocumentChunk>>`, qui applique
reranking → compression (sans le budget, qui dépend de l'historique — voir §4.3). Le garde-fou R-4
(contexte vide) reste appliqué juste après, comme aujourd'hui, sur le résultat de cette méthode.

### 4.2. Formatage partagé

La logique de formatage d'un chunk (`[Extrait i] Source: ..., p.X, Section: ..., HH:MM:SS–HH:MM:SS`
+ contenu) est extraite de `BuildChatHistoryAsync` en une méthode privée
`FormatChunkForContext(DocumentChunk chunk, int index) : string`, réutilisée :
- par l'étape de sélection sous budget (pour compter le texte réellement envoyé, pas seulement
  `chunk.Content` — évite un écart entre ce qui est compté et ce qui est transmis) ;
- par `BuildChatHistoryAsync` pour construire le prompt final.

### 4.3. Sélection sous budget, à l'intérieur de `BuildChatHistoryAsync`

Le budget dépend du coût en tokens de l'historique, connu seulement après l'avoir récupéré — ce qui
n'a lieu aujourd'hui qu'à l'intérieur de `BuildChatHistoryAsync` (`conversationRepository.
GetMessagesAsync`). La sélection sous budget y est donc insérée, **après** la récupération de
l'historique et **avant** la construction du texte de contexte final :

```csharp
private async Task<(ChatHistory History, IReadOnlyList<DocumentChunk> UsedChunks)> BuildChatHistoryAsync(
    RagQuery query, IReadOnlyList<DocumentChunk> candidateChunks, RagOptions ragOptions, CancellationToken ct)
{
    var chatHistory = new ChatHistory(query.SystemPrompt ?? RagPrompts.RagSystem);
    var pastMessages = new List<ChatMessage>();

    if (query.IncludeHistory && query.SessionId != Guid.Empty)
    {
        pastMessages = await conversationRepository.GetMessagesAsync(query.SessionId, query.MaxHistoryTurns, ct);
        foreach (var msg in pastMessages) { /* inchangé : AddUserMessage/AddAssistantMessage */ }
    }

    var usedChunks = candidateChunks;
    var tb = ragOptions.TokenBudget;
    if (tb.Enabled)
    {
        var overhead = tokenEstimator.EstimateTokens(query.SystemPrompt ?? RagPrompts.RagSystem)
                      + pastMessages.Sum(m => tokenEstimator.EstimateTokens(m.Content))
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

    // Construction du contexte documentaire à partir de usedChunks (au lieu de candidateChunks),
    // via FormatChunkForContext — inchangé sinon.
    // ...
    return (chatHistory, usedChunks);
}
```

`AskAsync`/`AskStreamAsync` appellent désormais `var (chatHistory, usedChunks) =
await BuildChatHistoryAsync(...)`, et construisent les **citations à l'étape 6 à partir de
`usedChunks`** (pas de `contextChunks`) — sans ce changement, une citation pourrait pointer vers un
extrait écarté par le budget et jamais réellement vu par le LLM.

### 4.4. `ReservedOutputTokens` remplace les littéraux codés en dur

Les 4 occurrences de `MaxOutputTokens = 2000` dans `RagPipelineService` (`AskAsync`,
`AskStreamAsync`, `AskDirectLlmAsync`, `StreamDirectLlmAsync`) sont remplacées par
`ragOptions.TokenBudget.ReservedOutputTokens`, pour que la réservation de budget corresponde
réellement à ce que le LLM peut produire — sans ce changement, modifier `ReservedOutputTokens` en
config n'aurait aucun effet sur la sortie réelle, et le calcul de budget serait basé sur une valeur
déconnectée du comportement effectif.

## 5. Cas limites & robustesse

| Cas | Comportement |
|-----|--------------|
| `TokenBudget.Enabled = false` | Comportement actuel inchangé — tous les chunks retenus par reranking/compression sont envoyés sans filtrage supplémentaire |
| Budget calculé négatif ou nul (historique très long) | Au moins 1 chunk reste inclus (dégradation progressive, jamais un contexte vide alors qu'un chunk pertinent existe) |
| Un seul chunk dépasse déjà seul le budget | Inclus quand même (règle du minimum 1) — mieux qu'un contexte vide déclenchant à tort le garde-fou R-4 |
| Chunks écartés par le budget | Log `Warning` (nombre écarté, budget) — pas d'impact sur la réponse HTTP |
| Mode `AskFullTextAsync` (`UseLlm = false`) | Non concerné — aucun appel LLM, pas de fenêtre de contexte à protéger |
| Mode `AskDirectLlmAsync`/`StreamDirectLlmAsync` (`UseRag = false`) | Pas de chunks à budgétiser, mais `ReservedOutputTokens` remplace quand même le littéral codé en dur (§4.4) |

## 6. Hors périmètre (YAGNI, à reconfirmer plus tard si besoin)

- Troncature de l'historique par tokens (voir décision #3, §2).
- Exposition du détail du budget (tokens utilisés, chunks écartés) dans `RagResponse` — relève de
  R-12 (observabilité), pas de ce lot.
- Implémentation `Microsoft.ML.Tokenizers` pour un comptage exact — pertinente seulement en cas de
  bascule vers OpenAI/Azure OpenAI (voir note d'évolutivité, §2 point 1).

## 7. Tests

- **`TokenBudgetSelectorTests`** (pur, sans mock) : liste vide, tout tient dans le budget,
  dépassement partiel (arrêt avant le premier élément qui déborde), un seul élément qui dépasse déjà
  seul le budget (doit être inclus), ordre de pertinence respecté.
- **`CharacterTokenEstimatorTests`** : texte vide, texte court, cohérence avec `CharsPerToken`
  configuré.
- Pas de nouveau test d'intégration sur `RagPipelineService` (privée, dépendances multiples injectées
  — même arbitrage que R-16/R-9 : vérifié manuellement plutôt que mocké) ; la factorisation
  `BuildContextChunksAsync`/`FormatChunkForContext` reste couverte indirectement par les tests
  existants du pipeline (aucun changement de comportement à budget désactivé).
