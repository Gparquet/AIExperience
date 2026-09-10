# Design — R-13 / T-4 : Harnais d'évaluation RAG

Date : 2026-09-08
Périmètre : `Step3/src/Back` (nouveau projet) + `Step3/eval/` (données)
Référence plan : `Step3/PLAN-AMELIORATION-RAG.md` §B.4 (R-13, lignes ~1190-1196) et §« Trois actions »
(ligne ~433) ; `Step3/DETTE-TECHNIQUE.md` T-4 (lignes ~120-126)

---

## 1. Contexte et constat

Le pipeline RAG a accumulé de la sophistication (fusion hybride, reranking LLM, quatre stratégies,
préfixes d'embedding asymétriques) sans jamais installer de mesure objective. Chaque amélioration de
pertinence se juge « au ressenti », par relecture manuelle de citations. Une session de vérification
manuelle du 03/07 (12 questions couvrant les 4 documents alors en base) avait déjà posé la bonne
méthode sans l'outiller : c'est exactement ce que ce lot formalise.

Le plan identifie R-13/T-4 comme le prérequis de fait à tout le reste du plan RAG (Lot 5, reranker
cross-encoder, parent-document…) : sans lui, aucun de ces points ne peut être arbitré autrement qu'au
ressenti.

Le corpus de développement actuel contient exactement 4 documents, ce qui recoupe la session
manuelle du 03/07 :

| Fichier | Type | Nb chunks |
|---|---|---|
| `4013000311051323737919283 (Commande 13816300).pdf` | Billet Disneyland Paris | 1 |
| `Garde - Dr WEISSE Sophie.PDF` | Compte-rendu vétérinaire d'urgence | 6 |
| `CV-Geoffrey - TECHNICAL LEADER - EKEEP IT.docx` | CV | 9 |
| `La compta n'aura plus de secret... TVA partie 1.mkv` | Transcription vidéo (sectorisation TVA) | 20 |

## 2. Décisions de cadrage (validées avec Geoffrey)

1. **Nouveau projet console dédié `AIExperience.Eval`**, même pattern de composition DI que
   `AIExperience.App.Console` (`AddInfrastructure()` + `AddApplication()`). Reste **hors** de
   `AIExperience.Tests`/CI : il appelle la vraie base Postgres et le vrai LLM, donc exécution
   manuelle uniquement (`dotnet run --project src/Back/AIExperience.Eval -- run`), cohérent avec
   l'absence de CI actuelle (E-4).
2. **Jeu de données golden versionné en git**, `Step3/eval/golden-dataset.json` — rédigé à partir du
   contenu réel des 4 documents (lu en base, voir §5). Les points de réponse attendus se limitent aux
   faits utiles au test ; les données personnelles non nécessaires à la vérification (adresse postale
   du compte-rendu vétérinaire, coordonnées du CV) ne sont **pas** reproduites dans le fichier.
3. **Référencement des documents par nom de fichier, jamais par `DocumentId`** : le GUID change à
   chaque ré-ingestion. Le runner résout `fileName → DocumentId` via `IDocumentRepository.GetAllAsync()`
   au lancement et **échoue explicitement** (exception, message listant le(s) fichier(s) manquant(s))
   si un document attendu est absent — détecte une dérive du corpus plutôt que de produire un rapport
   silencieusement faussé.
4. **Recall mesuré au niveau des citations finales du pipeline complet** (`RagResponse.Citations`,
   après fusion hybride + reranking), pas isolément sur la récupération vectorielle brute. Plus
   représentatif de ce que voit l'utilisateur, et n'impose aucune modification du code de production
   ni aucun point d'instrumentation supplémentaire.
5. **Sessions d'évaluation isolées de l'historique réel** : `RagQuery.UserId` est fixé à la constante
   `"eval-harness"` pour tous les appels du runner (au lieu de `DevAuthOptions.DefaultUserId`).
   `ConversationRepository` filtre déjà les sessions par `UserId` (`GetSummariesAsync`, etc.) — ce
   choix suffit à garder les 12 sessions générées par run hors de la barre latérale de conversation de
   Geoffrey dans le front, sans toucher au code de production. Sans lien avec S-1 (isolation absente
   de la *recherche*) : `DocumentIds` est résolu explicitement par le runner, la recherche elle-même
   n'est pas filtrée par utilisateur donc ce choix d'UserId n'affecte pas la récupération.
6. **Fidélité via LLM-as-judge, avec le même `IChatClient` que le pipeline** (même modèle local
   faible que celui qu'on évalue par ailleurs). Score à 3 paliers **0 / 0,5 / 1** plutôt qu'une échelle
   continue — plus stable pour un petit modèle juge. Un échec de parsing produit un score **`null`
   explicite** dans le rapport (jamais un repli silencieux sur 0 ou 1) : le juge étant lui-même faible,
   la justification textuelle qu'il produit sert de matière à une relecture manuelle, pas de vérité
   absolue.
7. **Pas de métrique de précision en v1** (proportion de citations non pertinentes) — hors périmètre
   explicite de R-13 tel que décrit dans le plan (recall@k + fidélité). YAGNI, à reconsidérer si le
   recall seul s'avère insuffisant pour arbitrer une régression.

## 3. Architecture

```
Step3/eval/
├── golden-dataset.json          ← versionné en git (référence de vérité)
└── runs/                        ← gitignoré, artefacts d'exécution horodatés
    └── 20260908-143000.json

src/Back/AIExperience.Eval/
├── Program.cs                          CLI : dispatch "run" | "compare"
├── GoldenDataset/
│   ├── GoldenQuestionDto.cs             DTO de désérialisation JSON (fileName, pas Guid)
│   └── GoldenDatasetLoader.cs           charge + valide le JSON
├── Metrics/
│   ├── ExpectedSource.cs                (DocumentId résolu, PageNumber?, StartTimeSeconds?, EndTimeSeconds?)
│   ├── RecallResult.cs                  (Hit: bool, Rank: int?)
│   └── RecallCalculator.cs              PUR — testable sans mock
├── Judge/
│   ├── EvalPrompts.cs                   prompts système/utilisateur du juge (français)
│   ├── JudgeResult.cs                   (Score: double?, Rationale: string?)
│   ├── JudgeResponseParser.cs           PUR — testable sans mock (même famille que BatchedRerankResponseParser)
│   └── FidelityJudge.cs                 appelle IChatClient + JudgeResponseParser
├── Reporting/
│   ├── EvalQuestionResult.cs
│   ├── EvalRunReport.cs                 agrégats + snapshot RagOptions
│   ├── EvalReportWriter.cs              écrit/charge le JSON horodaté
│   └── RunComparer.cs                   PUR — diff de deux EvalRunReport
├── EvalRunner.cs                        orchestration (résolution corpus → boucle questions → rapport)
└── appsettings.json                     copié du pattern App.Console (connexion DB + config IA)
```

`AIExperience.Eval.csproj` : `OutputType=Exe`, référence `AIExperience.Rag.Infrastructure` (comme
`App.Console`, transitive vers Application/Domain). `AIExperience.Tests.csproj` gagne une
`ProjectReference` vers `AIExperience.Eval` pour tester ses classes pures (`RecallCalculator`,
`JudgeResponseParser`, `RunComparer`) — même raison que sa référence actuelle vers Infrastructure.

### 3.1. Commande `run`

```
dotnet run --project src/Back/AIExperience.Eval -- run [--dataset <chemin>] [--out <dossier>]
```

Par défaut : `--dataset eval/golden-dataset.json`, `--out eval/runs/` — chemins relatifs au
répertoire courant (documenté : lancer depuis `Step3/`, comme les autres commandes du projet).

Séquence dans `EvalRunner` :
1. `GoldenDatasetLoader.Load(datasetPath)` → liste de `GoldenQuestionDto`.
2. `documentRepository.GetAllAsync()` → dictionnaire `fileName → DocumentId` (dernier `CreatedAt` en
   cas de doublon, comme `GetLatestByFileNameAsync`). Résolution de chaque `expectedSources[].fileName`
   → `ExpectedSource.DocumentId`. Fichier introuvable ⇒ exception immédiate, liste des fichiers
   manquants dans le message.
3. Pour chaque question, dans l'ordre du fichier :
   - `RagQuery { Question, Strategy = Adaptive, UseLlm = true, UseRag = true, IncludeHistory = false, SessionId = Guid.Empty, UserId = "eval-harness" }` → `ragPipelineService.AskAsync(...)`.
   - `RecallCalculator.Evaluate(expectedSources, response.Citations)` → `RecallResult`.
   - `FidelityJudge.ScoreAsync(question, response.Answer, expectedAnswerKeyPoints, ct)` → `JudgeResult`.
   - Accumulation dans `EvalQuestionResult`.
4. Agrégats : recall@K moyen (fraction de `Hit = true`), MRR moyen (`1/Rank` si `Hit`, sinon 0),
   fidélité moyenne (moyenne des `Score` non-null, en ignorant les `null`), nombre d'échecs de parsing.
5. `EvalReportWriter.Write(report, outDir)` → `eval/runs/{yyyyMMdd-HHmmss}.json`. Affichage console
   d'un tableau récapitulatif (par question : recall hit/miss, rang, score fidélité ; puis agrégats).

### 3.2. Commande `compare`

```
dotnet run --project src/Back/AIExperience.Eval -- compare <run-a.json> <run-b.json>
```

`EvalReportWriter.Read` charge les deux rapports, `RunComparer.Compare(a, b)` (pur) produit :
question par question, le changement de statut recall (hit→miss, miss→hit, inchangé) et le delta de
score de fidélité ; en agrégat, les deltas de recall@K moyen / MRR moyen / fidélité moyenne. Le
snapshot `RagOptions` des deux runs est affiché côte à côte (pas de diff structurel automatique —
suffisant pour repérer visuellement ce qui a changé entre deux runs, YAGNI d'un différentiateur
générique).

## 4. Composants clés

### 4.1. `RecallCalculator` (pur)

```csharp
public static class RecallCalculator
{
    // Une citation "matche" une source attendue si DocumentId est identique ET
    // (PageNumber attendu absent OU égal) ET (plage horaire attendue absente OU chevauchante).
    // Hit = au moins une correspondance ; Rank = position 1-based de la première citation
    // correspondante dans la liste (ordre déjà trié par pertinence par le pipeline).
    public static RecallResult Evaluate(
        IReadOnlyList<ExpectedSource> expectedSources,
        IReadOnlyList<Citation> citations);
}
```

### 4.2. `JudgeResponseParser` (pur)

Même philosophie que `BatchedRerankResponseParser` : regex tolérante au bruit d'un petit modèle,
attend un format `SCORE: 0|0,5|0.5|1` + `JUSTIFICATION: ...` mais tolère variations d'espacement/casse.
Score absent ou hors des 3 valeurs autorisées ⇒ `JudgeResult(null, texte brut complet)` — jamais une
valeur par défaut.

### 4.3. `FidelityJudge`

```csharp
public sealed class FidelityJudge(IChatClient chatClient)
{
    public async Task<JudgeResult> ScoreAsync(
        string question, string generatedAnswer, IReadOnlyList<string> expectedKeyPoints, CancellationToken ct);
}
```

Construit le prompt (`EvalPrompts.JudgeSystem`/`JudgeUser`, français), appelle
`chatClient.GetResponseAsync` (température basse, ~0.0-0.1, pour un jugement reproductible), délègue
le parsing à `JudgeResponseParser`.

### 4.4. `GoldenDatasetLoader`

Désérialisation `System.Text.Json` stricte (`PropertyNameCaseInsensitive`), validation minimale :
`Question` non vide, au moins une `ExpectedSource` par entrée, `Id` unique dans le fichier — échoue
vite avec un message clair plutôt que de laisser une entrée malformée produire un résultat silencieux.

## 5. Jeu de données golden (annexe — contenu de `eval/golden-dataset.json`)

Rédigé à partir du contenu réel des chunks en base (`document_chunks.content`), 3 questions par
document, cohérent avec la méthode manuelle du 03/07 (12 questions / 4 documents).

```json
{
  "questions": [
    {
      "id": "commande-01",
      "question": "Quelle est la date de visite indiquée sur le billet Disneyland Paris ?",
      "expectedAnswerKeyPoints": ["mardi 7 avril 2026"],
      "expectedSources": [{ "fileName": "4013000311051323737919283 (Commande 13816300).pdf" }]
    },
    {
      "id": "commande-02",
      "question": "Quel est le prix TTC du billet Disneyland Paris commandé ?",
      "expectedAnswerKeyPoints": ["120,60 €"],
      "expectedSources": [{ "fileName": "4013000311051323737919283 (Commande 13816300).pdf" }]
    },
    {
      "id": "commande-03",
      "question": "Quel est le numéro de commande client associé à ce billet Disneyland ?",
      "expectedAnswerKeyPoints": ["13816300"],
      "expectedSources": [{ "fileName": "4013000311051323737919283 (Commande 13816300).pdf" }]
    },
    {
      "id": "garde-01",
      "question": "Quel traitement a été administré à Urielle lors de la consultation d'urgence ?",
      "expectedAnswerKeyPoints": ["Maropitant 1 mg/kg", "injection sous-cutanée (SC)"],
      "expectedSources": [{ "fileName": "Garde - Dr WEISSE Sophie.PDF", "pageNumber": 1 }]
    },
    {
      "id": "garde-02",
      "question": "Quel est le motif de la consultation d'urgence pour la chienne Urielle ?",
      "expectedAnswerKeyPoints": ["vomissements aigus", "anorexie", "abattement discret"],
      "expectedSources": [{ "fileName": "Garde - Dr WEISSE Sophie.PDF", "pageNumber": 1 }]
    },
    {
      "id": "garde-03",
      "question": "Quelle est la conclusion du vétérinaire sur l'origine probable des symptômes d'Urielle ?",
      "expectedAnswerKeyPoints": ["intolérance alimentaire probable", "sans exclure gastro-entérite ou corps étranger digestif"],
      "expectedSources": [{ "fileName": "Garde - Dr WEISSE Sophie.PDF", "pageNumber": 1 }]
    },
    {
      "id": "cv-01",
      "question": "Depuis quand Geoffrey occupe-t-il son poste de Technique Leader chez Eurotunnel ?",
      "expectedAnswerKeyPoints": ["janvier 2021"],
      "expectedSources": [{ "fileName": "CV-Geoffrey - TECHNICAL LEADER  - EKEEP IT.docx", "pageNumber": 1 }]
    },
    {
      "id": "cv-02",
      "question": "Quelles bonnes pratiques de développement Geoffrey a-t-il mises en place chez Eurotunnel ?",
      "expectedAnswerKeyPoints": ["Pull Request", "Code Review", "TDD"],
      "expectedSources": [{ "fileName": "CV-Geoffrey - TECHNICAL LEADER  - EKEEP IT.docx", "pageNumber": 1 }]
    },
    {
      "id": "cv-03",
      "question": "Chez quel client Geoffrey a-t-il travaillé en tant que développeur .NET pour Sopra Steria ?",
      "expectedAnswerKeyPoints": ["Banque de France"],
      "expectedSources": [{ "fileName": "CV-Geoffrey - TECHNICAL LEADER  - EKEEP IT.docx", "pageNumber": 1 }]
    },
    {
      "id": "tva-01",
      "question": "Qu'est-ce que la sectorisation de la TVA, telle que décrite dans la vidéo ?",
      "expectedAnswerKeyPoints": [
        "mécanisme permettant à un établissement public ayant une activité commerciale de régulariser une partie de son traitement de TVA en fonction de sa part client",
        "évite les distorsions de concurrence"
      ],
      "expectedSources": [{ "fileName": "La compta n'aura plus de secret pour vous - Saison 1 Episode 18 - La TVA partie 1.mkv", "startTimeSeconds": 402, "endTimeSeconds": 734 }]
    },
    {
      "id": "tva-02",
      "question": "Quelle est la différence entre le taux de sectorisation provisoire et le taux définitif ?",
      "expectedAnswerKeyPoints": [
        "le taux provisoire est appliqué en cours d'exercice",
        "le taux définitif est calculé en fin d'exercice sur l'écart réel constaté"
      ],
      "expectedSources": [{ "fileName": "La compta n'aura plus de secret pour vous - Saison 1 Episode 18 - La TVA partie 1.mkv", "startTimeSeconds": 632, "endTimeSeconds": 824 }]
    },
    {
      "id": "tva-03",
      "question": "Pourquoi la plupart des établissements publics ne sont-ils pas soumis à la TVA ?",
      "expectedAnswerKeyPoints": [
        "cela n'aurait pas de sens qu'un établissement public reverse à l'État, sous forme de TVA, des fonds qu'il a reçus de l'État pour son fonctionnement"
      ],
      "expectedSources": [{ "fileName": "La compta n'aura plus de secret pour vous - Saison 1 Episode 18 - La TVA partie 1.mkv", "startTimeSeconds": 309, "endTimeSeconds": 411 }]
    }
  ]
}
```

> Ce brouillon reste à valider par Geoffrey avant le premier run — les points clés attendus
> engagent le score de fidélité, une formulation trop stricte pénaliserait injustement une réponse
> correcte mais reformulée différemment par le LLM.

## 6. Cas limites & robustesse

| Cas | Comportement |
|---|---|
| Fichier attendu absent du corpus (dérive) | Exception au démarrage du run, liste des fichiers manquants — aucun rapport partiel produit |
| Réponse du juge non parsable | `FidelityScore = null` dans le rapport, exclu du calcul de moyenne, compté dans `FidelityParseFailures` |
| Aucune citation retournée pour une question | `RecallResult.Hit = false`, `Rank = null` |
| `expectedSources` sans `pageNumber` ni plage horaire | Correspondance sur `DocumentId` seul |
| Deux documents portent le même nom (ré-upload) | Résolution sur le plus récent (`CreatedAt`), comme `GetLatestByFileNameAsync` |
| `dataset` ou `runs` introuvable/malformé | Message d'erreur explicite, arrêt — pas de génération d'un rapport vide |
| `ragPipelineService.AskAsync` lève une exception sur une question | Capturée par question (pas par le run) : `EvalQuestionResult.Error` renseigné, `RecallHit = false`, `FidelityScore = null` — les 11 autres questions du run restent exploitables |
| `FidelityJudge` : l'appel `IChatClient` lève une exception | Capturée dans `FidelityJudge.ScoreAsync`, retourne `JudgeResult(null, "Erreur lors de l'appel au juge : ...")` — même dégradation gracieuse que `QueryCondensationService` |

## 7. Hors périmètre (YAGNI, à reconsidérer plus tard si besoin)

- Métrique de précision (citations non pertinentes) — non demandée par R-13/T-4 en l'état.
- Diff structurel automatique des deux `RagOptions` en mode `compare` — affichage côte à côte suffit
  pour un jeu de 12 questions.
- Intégration CI (`dotnet test`) — nécessite DB + LLM réels, hors de portée tant qu'E-4 (CI) n'existe pas.
- Variation de stratégie par question (Direct/HyDE/Fusion forcés) — le run compare l'état réel de
  production (`Adaptive`), un futur besoin de comparaison inter-stratégies pourra ajouter un champ
  optionnel `strategyOverride` sans remise en cause du schéma.
- Implémentation `Microsoft.ML.Tokenizers` ou autre juge externe plus fiable — le juge local faible
  est un choix assumé (même modèle que la production), la justification textuelle compense sa
  faiblesse pour l'instant.

## 8. Tests

- **`RecallCalculatorTests`** (pur) : hit sur `DocumentId` seul, hit avec `pageNumber` exact, miss sur
  page différente, hit avec chevauchement de plage horaire partiel, miss sur plage disjointe, rang
  correct quand la première citation ne correspond pas, liste de citations vide.
- **`JudgeResponseParserTests`** (pur) : `SCORE: 1`/`0,5`/`0` nominal, tolérance espacement/casse,
  virgule vs point décimal, score hors échelle (ex. `SCORE: 2`) ⇒ `null`, réponse sans balise
  reconnaissable ⇒ `null`, réponse vide/nulle ⇒ `null`.
- **`RunComparerTests`** (pur) : delta recall hit→miss et miss→hit détectés, delta de fidélité calculé,
  question présente dans un seul des deux rapports (id désynchronisé) gérée sans exception.
- **`GoldenDatasetLoaderTests`** : chargement nominal, question sans `expectedSources` rejetée, id
  dupliqué rejeté.
- Pas de test d'intégration sur `EvalRunner` (dépend de la DB + LLM réels) — vérifié manuellement par
  un premier run sur le corpus de dev, comme les autres composants du pipeline non mockés (R-16/R-9).
