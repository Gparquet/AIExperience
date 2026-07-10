# ADR-009 — Trois modes de restitution configurables (Full-text / LLM direct / RAG complet)

**Date :** 2026-06-07  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Step 3 a une vocation **démonstrative** : montrer la valeur ajoutée du RAG. Or pour la démontrer,
il faut pouvoir **comparer** le RAG à ce qu'il remplace ou complète :

- Une **recherche full-text** classique (PostgreSQL), sans embeddings ni LLM : rapide, factuelle,
  mais littérale.
- Un **LLM seul**, sans documents : fluide, mais qui « hallucine » hors de son savoir figé.
- Le **RAG complet** : LLM ancré dans les documents, avec citations.

Mettre ces trois approches côte à côte, sur la même question, rend tangible l'apport du RAG. Il
faut donc un pipeline capable de basculer entre ces modes **sans dupliquer les endpoints ni la
logique**.

---

## Décision

Piloter le comportement du pipeline par **deux booléens** portés par la requête (`RagQuery`) :
`UseLlm` et `UseRag`. Leur combinaison sélectionne l'un des trois modes, à travers **le même
endpoint** (`/api/chat/ask` et `/api/chat/stream`).

| `UseLlm` | `UseRag` | Mode | Comportement |
|:--------:|:--------:|------|--------------|
| `false` | `true` | **Full-text** | Recherche PostgreSQL `plainto_tsquery('french', …)` + `ts_rank`. Ni embedding ni LLM. Retourne les chunks bruts. |
| `true` | `false` | **LLM direct** | Question envoyée directement au LLM, sans récupération documentaire. |
| `true` | `true` | **RAG complet** | Pipeline complet : embed question → cosinus pgvector → (compression) → LLM + citations. |

---

## Architecture de la solution

### Options portées par le Domain — `RagQuery`

```csharp
public bool UseLlm { get; init; } = true;          // false = mode full-text
public bool UseRag { get; init; } = true;          // false = LLM direct
public string? SystemPrompt { get; init; }         // prompt système personnalisable par mode
public bool IncludeHistory { get; init; } = true;
public int MaxHistoryTurns { get; init; } = 5;
```

Les valeurs par défaut (`true`/`true`) font du **RAG complet le comportement nominal** ; les
deux autres modes sont des dégradations volontaires activées explicitement.

### Aiguillage dans `RagPipelineService`

Le pipeline lit les deux drapeaux **au tout début** et court-circuite les étapes inutiles :

- `UseLlm = false` → **pas d'embedding, pas de LLM**. On délègue à la recherche full-text
  (`plainto_tsquery` + `ts_rank` de PostgreSQL) et on renvoie les chunks tels quels. Latence
  minimale, coût nul.
- `UseRag = false` → **pas de récupération**. On construit le prompt sans contexte documentaire
  et on interroge directement le LLM.
- Les deux à `true` → pipeline RAG complet inchangé (stratégies Direct/HyDE/Fusion/Adaptive).

Un **seul point de décision**, en tête de pipeline, évite de disperser des `if` dans toutes les
étapes.

### Prompt système par mode — `SystemPrompt`

Chaque mode a un prompt système par défaut différent (le RAG cadre « réponds uniquement d'après
le contexte », le LLM direct est plus conversationnel). Ils sont exposés par
`GET /api/chat/system-prompts` (`ragSystem`, `directLlmSystem`) et **surchargeables** par
l'utilisateur via `SystemPrompt` — utile pour la démonstration et l'expérimentation.

### Un endpoint, trois comportements

Le front (ChatPage) présente trois onglets qui ne font que **positionner les deux drapeaux** ;
côté serveur, aucun endpoint supplémentaire, aucune branche de code dupliquée.

---

## Conséquences

### Positives

- **Comparaison directe** des trois approches sur la même question — objectif pédagogique atteint.
- **Un seul endpoint** et un seul pipeline : pas de duplication, maintenance simplifiée.
- Le mode **full-text** offre une réponse quasi instantanée et sans coût pour les requêtes
  factuelles, sans mobiliser le LLM.
- Prompts système **personnalisables** par mode, précieux pour la démo et le réglage.

### Négatives / points d'attention

- **Deux booléens = 4 combinaisons théoriques**, dont une (`UseLlm = false` **et**
  `UseRag = false`) n'a pas de sens ; le pipeline doit la neutraliser proprement (retomber sur
  full-text) plutôt que de produire un comportement indéfini.
- Le mode full-text dépend de la configuration linguistique de PostgreSQL
  (`plainto_tsquery('french', …)`) — les corpus dans d'autres langues seront moins bien servis.
- La signification des drapeaux (`UseRag=false` = LLM direct, contre-intuitif au premier abord)
  doit être documentée pour éviter les confusions d'appel API.

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| Un endpoint distinct par mode (`/search`, `/llm`, `/rag`) | Triple la surface d'API et duplique l'orchestration commune (historique, prompts, citations). |
| Un unique paramètre `mode: "fulltext" \| "llm" \| "rag"` | Moins composable ; les deux booléens expriment des axes orthogonaux (récupérer ? / raisonner ?). |
| Toujours faire du RAG complet | Empêche la comparaison pédagogique et impose le coût LLM même pour une simple recherche factuelle. |
| Choix du mode côté serveur (heuristique) | Retire à l'utilisateur le contrôle explicite nécessaire à une démonstration. |
