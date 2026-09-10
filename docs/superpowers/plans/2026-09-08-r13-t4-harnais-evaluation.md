# Harnais d'évaluation RAG (R-13 / T-4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner au projet un harnais d'évaluation objectif du pipeline RAG (recall@k au niveau
citations + fidélité par LLM-as-judge) sur un jeu de 12 questions « golden » couvrant les 4 documents
du corpus de dev, exécutable manuellement et comparable d'un run à l'autre.

**Architecture:** Nouveau projet console `AIExperience.Eval`, composé exactement comme
`AIExperience.App.Console` (même DI `AddInfrastructure()`+`AddApplication()`). Deux commandes CLI :
`run` (exécute le jeu golden via `IRagPipelineService.AskAsync`, calcule les métriques, écrit un
rapport JSON horodaté) et `compare` (diff deux rapports). Les classes de calcul (recall, parsing du
juge, comparaison de runs) sont pures et testées sans mock, dans `AIExperience.Tests`.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Hosting`/`DependencyInjection`, `System.Text.Json`,
xUnit + FluentAssertions (déjà en place).

**Spec:** [docs/superpowers/specs/2026-09-08-r13-t4-harnais-evaluation-design.md](../specs/2026-09-08-r13-t4-harnais-evaluation-design.md)

## Global Constraints

- Tout commentaire de code est en français, explique le *pourquoi* (jamais de référence à « R-13 »,
  « T-4 » ou « Lot X » dans le code — seulement dans ce plan et la spec).
- Aucun framework de mock : les tests utilisent des faux écrits à la main (`FakeChatClient`), comme
  le reste de `AIExperience.Tests`.
- `AIExperience.Eval` référence uniquement `AIExperience.Rag.Infrastructure` (transitive vers
  Application/Domain) — même pattern que `AIExperience.App.Console`. Pas de nouvelle dépendance NuGet.
- Le jeu de données golden référence les documents par **nom de fichier**, jamais par `DocumentId`.
- Ne pas commiter automatiquement — Geoffrey commite lui-même (laisser le travail en staging).

---

### Task 1: Scaffolding du projet `AIExperience.Eval`

**Files:**
- Create: `Step3/src/Back/AIExperience.Eval/AIExperience.Eval.csproj`
- Create: `Step3/src/Back/AIExperience.Eval/appsettings.json`
- Create: `Step3/src/Back/AIExperience.Eval/Program.cs` (placeholder minimal, complété en Task 8)
- Modify: `Step3/src/Back/AIExperience.slnx`
- Modify: `Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj`

**Interfaces:**
- Produces: le projet `AIExperience.Eval` compilable, référencé par `AIExperience.Tests` pour les
  tâches suivantes (Tasks 2-6 y ajoutent des classes pures testées depuis `AIExperience.Tests`).

- [ ] **Step 1: Créer le fichier projet**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>AIExperience.Eval</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.10" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AIExperience.Rag.Infrastructure\AIExperience.Rag.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Créer `appsettings.json`** (repris de `AIExperience.Web.Api/appsettings.json`, seule
  source à jour — celui de `App.Console` a un port Postgres périmé)

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5433;Database=ragdocumentchat;Username=postgres;Password=postgres"
  },
  "AI": {
    "Provider": "OpenAI",
    "Endpoint": "http://localhost:1234/v1",
    "ChatModel": "llama-3.2-3b-instruct",
    "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
    "ApiKey": "sk-lm-mfLYE9Uo:xWxuxs04N8nSRXEjmqYP",
    "EmbeddingTaskPrefixes": true
  },
  "RagOptions": {
    "DefaultStrategy": "Adaptive",
    "HyDE": { "Enabled": true, "HypotheticalDocLength": 200 },
    "MultiQuery": { "Enabled": true, "VariantCount": 3 },
    "Retrieval": { "TopK": 10, "ScoreThreshold": 0.5 },
    "Reranker": { "Enabled": true, "TopKAfterRerank": 5 },
    "ContextCompression": { "Enabled": false },
    "Cache": { "Enabled": false, "TtlMinutes": 60 },
    "Condensation": { "Enabled": true }
  },
  "Whisper": {
    "ModelPath": "F:\\Prog\\IA\\AIExperience\\Step3\\models\\ggml-medium.bin",
    "Threads": 8
  },
  "FFmpeg": {
    "BinaryPath": "C:\\Users\\geoff\\Downloads\\ffmpeg-N-124867-g3137d337fe-win64-lgpl\\ffmpeg-N-124867-g3137d337fe-win64-lgpl\\bin"
  },
  "Ingestion": {
    "WorkDirectory": "App_Data/ingestion-work",
    "PollIntervalSeconds": 3,
    "BatchSize": 5,
    "MaxRetryAttempts": 5
  },
  "DevAuth": {
    "DefaultUserId": "1ea95468-3f27-4a6d-8fb3-25fdd1530023"
  }
}
```

- [ ] **Step 3: Créer un `Program.cs` placeholder**

```csharp
Console.WriteLine("AIExperience.Eval — scaffolding OK. Commandes 'run'/'compare' ajoutées en Task 8.");
```

- [ ] **Step 4: Ajouter le projet à la solution**

Éditer `Step3/src/Back/AIExperience.slnx`, ajouter une ligne (ordre alphabétique) :

```xml
  <Project Path="AIExperience.Eval/AIExperience.Eval.csproj" />
```

(juste après `<Project Path="AIExperience.App.Console/AIExperience.App.Console.csproj" />`)

- [ ] **Step 5: Référencer le projet depuis `AIExperience.Tests`**

Dans `Step3/src/Back/AIExperience.Tests/AIExperience.Tests.csproj`, ajouter dans le `ItemGroup` des
`ProjectReference` existant :

```xml
    <ProjectReference Include="..\AIExperience.Eval\AIExperience.Eval.csproj" />
```

- [ ] **Step 6: Vérifier que tout compile**

Run: `dotnet build src/Back/AIExperience.slnx` (depuis `Step3/`)
Expected: Build réussi, `AIExperience.Eval.dll` et `AIExperience.Tests.dll` produits.

- [ ] **Step 7: Commit**

Ne PAS commiter (consigne explicite : Geoffrey commite lui-même). Laisser en staging.

---

### Task 2: `RecallCalculator` (pur, testé)

**Files:**
- Create: `Step3/src/Back/AIExperience.Eval/Metrics/ExpectedSource.cs`
- Create: `Step3/src/Back/AIExperience.Eval/Metrics/RecallResult.cs`
- Create: `Step3/src/Back/AIExperience.Eval/Metrics/RecallCalculator.cs`
- Test: `Step3/src/Back/AIExperience.Tests/RecallCalculatorTests.cs`

**Interfaces:**
- Consumes: `AIExperience.Rag.Domain.Entities.Citation` (déjà existant — `DocumentId`, `PageNumber`
  (`int?`), `StartTime`/`EndTime` (`TimeSpan?`)).
- Produces: `ExpectedSource(Guid DocumentId, int? PageNumber = null, double? StartTimeSeconds = null, double? EndTimeSeconds = null)`,
  `RecallResult(bool Hit, int? Rank)`, `RecallCalculator.Evaluate(IReadOnlyList<ExpectedSource>, IReadOnlyList<Citation>) : RecallResult`
  — utilisés par `EvalRunner` (Task 7).

- [ ] **Step 1: Écrire les tests (échouent — les classes n'existent pas encore)**

```csharp
// Step3/src/Back/AIExperience.Tests/RecallCalculatorTests.cs
using AIExperience.Eval.Metrics;
using AIExperience.Rag.Domain.Entities;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="RecallCalculator"/> : correspondance document seul, page exacte,
/// chevauchement de plage horaire (vidéo), et rang de la première citation correspondante.
/// </summary>
public sealed class RecallCalculatorTests
{
    private static Citation MakeCitation(Guid documentId, int? page = null, TimeSpan? start = null, TimeSpan? end = null) =>
        Citation.Create(Guid.Empty, documentId, "Document", "extrait", 0.8, page, startTime: start, endTime: end);

    [Fact]
    public void Evaluate_DocumentIdSeulSansPageNiPlage_HitSurCorrespondanceDocumentId()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId) };
        var citations = new[] { MakeCitation(Guid.NewGuid()), MakeCitation(docId) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeTrue();
        result.Rank.Should().Be(2);
    }

    [Fact]
    public void Evaluate_PageAttendueExacte_Hit()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, PageNumber: 1) };
        var citations = new[] { MakeCitation(docId, page: 1) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeTrue();
        result.Rank.Should().Be(1);
    }

    [Fact]
    public void Evaluate_PageDifferente_Miss()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, PageNumber: 1) };
        var citations = new[] { MakeCitation(docId, page: 2) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeFalse();
        result.Rank.Should().BeNull();
    }

    [Fact]
    public void Evaluate_PlageHoraireChevauchante_Hit()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, StartTimeSeconds: 100, EndTimeSeconds: 200) };
        var citations = new[] { MakeCitation(docId, start: TimeSpan.FromSeconds(150), end: TimeSpan.FromSeconds(250)) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PlageHoraireDisjointe_Miss()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId, StartTimeSeconds: 100, EndTimeSeconds: 200) };
        var citations = new[] { MakeCitation(docId, start: TimeSpan.FromSeconds(300), end: TimeSpan.FromSeconds(400)) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Hit.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RangDeLaPremiereCitationCorrespondante()
    {
        var docId = Guid.NewGuid();
        var expected = new[] { new ExpectedSource(docId) };
        var citations = new[] { MakeCitation(Guid.NewGuid()), MakeCitation(Guid.NewGuid()), MakeCitation(docId) };

        var result = RecallCalculator.Evaluate(expected, citations);

        result.Rank.Should().Be(3);
    }

    [Fact]
    public void Evaluate_ListeCitationsVide_Miss()
    {
        var result = RecallCalculator.Evaluate([new ExpectedSource(Guid.NewGuid())], []);

        result.Hit.Should().BeFalse();
        result.Rank.Should().BeNull();
    }
}
```

- [ ] **Step 2: Vérifier que les tests échouent (les types n'existent pas)**

Run: `dotnet test src/Back/AIExperience.slnx --filter RecallCalculatorTests` (depuis `Step3/`)
Expected: FAIL — erreur de compilation, `AIExperience.Eval.Metrics` introuvable.

- [ ] **Step 3: Implémenter `ExpectedSource` et `RecallResult`**

```csharp
// Step3/src/Back/AIExperience.Eval/Metrics/ExpectedSource.cs
namespace AIExperience.Eval.Metrics;

/// <summary>
/// Source attendue pour une question golden, avec le DocumentId déjà résolu (le jeu de données
/// référence les documents par nom de fichier — voir GoldenDatasetLoader/EvalRunner).
/// </summary>
public sealed record ExpectedSource(
    Guid DocumentId,
    int? PageNumber = null,
    double? StartTimeSeconds = null,
    double? EndTimeSeconds = null);
```

```csharp
// Step3/src/Back/AIExperience.Eval/Metrics/RecallResult.cs
namespace AIExperience.Eval.Metrics;

/// <summary>Résultat du calcul de recall pour une question : correspondance trouvée et son rang.</summary>
public sealed record RecallResult(bool Hit, int? Rank);
```

- [ ] **Step 4: Implémenter `RecallCalculator`**

```csharp
// Step3/src/Back/AIExperience.Eval/Metrics/RecallCalculator.cs
using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Eval.Metrics;

/// <summary>
/// Calcule le recall au niveau des citations finales du pipeline RAG (après fusion hybride et
/// reranking) plutôt qu'au niveau de la récupération vectorielle brute — c'est ce que voit
/// réellement l'utilisateur, et cela ne nécessite aucune instrumentation du pipeline de production.
/// </summary>
public static class RecallCalculator
{
    public static RecallResult Evaluate(IReadOnlyList<ExpectedSource> expectedSources, IReadOnlyList<Citation> citations)
    {
        for (var i = 0; i < citations.Count; i++)
        {
            if (expectedSources.Any(expected => Matches(expected, citations[i])))
                return new RecallResult(true, i + 1);
        }
        return new RecallResult(false, null);
    }

    private static bool Matches(ExpectedSource expected, Citation citation)
    {
        if (citation.DocumentId != expected.DocumentId)
            return false;

        if (expected.PageNumber is { } page && citation.PageNumber != page)
            return false;

        if (expected.StartTimeSeconds is { } start && expected.EndTimeSeconds is { } end)
        {
            // Chevauchement d'intervalles [start, end] : la citation doit couvrir au moins une
            // partie de la plage attendue, pas nécessairement l'englober exactement (le chunking
            // temporel peut légèrement décaler les bornes d'un chunk à la ré-ingestion).
            if (citation.StartTime is not { } citStart || citation.EndTime is not { } citEnd)
                return false;
            if (!(citStart.TotalSeconds < end && citEnd.TotalSeconds > start))
                return false;
        }

        return true;
    }
}
```

- [ ] **Step 5: Lancer les tests, vérifier qu'ils passent**

Run: `dotnet test src/Back/AIExperience.slnx --filter RecallCalculatorTests` (depuis `Step3/`)
Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 3: `JudgeResponseParser` (pur, testé)

**Files:**
- Create: `Step3/src/Back/AIExperience.Eval/Judge/JudgeResult.cs`
- Create: `Step3/src/Back/AIExperience.Eval/Judge/JudgeResponseParser.cs`
- Test: `Step3/src/Back/AIExperience.Tests/JudgeResponseParserTests.cs`

**Interfaces:**
- Produces: `JudgeResult(double? Score, string? Rationale)`, `JudgeResponseParser.Parse(string?) : JudgeResult`
  — utilisés par `FidelityJudge` (Task 4).

- [ ] **Step 1: Écrire les tests (échouent)**

```csharp
// Step3/src/Back/AIExperience.Tests/JudgeResponseParserTests.cs
using AIExperience.Eval.Judge;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="JudgeResponseParser"/> : même tolérance au bruit d'un petit modèle local
/// que BatchedRerankResponseParser (espacement, casse, virgule décimale française).
/// </summary>
public sealed class JudgeResponseParserTests
{
    [Fact]
    public void Parse_ScoreEntierNominal_RetourneScoreEtJustification()
    {
        var response = "SCORE: 1\nJUSTIFICATION: La réponse couvre tous les points attendus.";

        var result = JudgeResponseParser.Parse(response);

        result.Score.Should().Be(1.0);
        result.Rationale.Should().Be("La réponse couvre tous les points attendus.");
    }

    [Fact]
    public void Parse_ScoreDemiAvecVirguleDecimale_EstAccepte()
    {
        var result = JudgeResponseParser.Parse("SCORE: 0,5\nJUSTIFICATION: réponse partielle");

        result.Score.Should().Be(0.5);
    }

    [Fact]
    public void Parse_ScoreDemiAvecPointDecimal_EstAccepte()
    {
        var result = JudgeResponseParser.Parse("Score : 0.5");

        result.Score.Should().Be(0.5);
    }

    [Fact]
    public void Parse_ToleratesEspacementEtCasse()
    {
        var result = JudgeResponseParser.Parse("score:0\njustification :incorrect");

        result.Score.Should().Be(0.0);
        result.Rationale.Should().Be("incorrect");
    }

    [Fact]
    public void Parse_ScoreHorsDes3Paliers_RetourneScoreNull()
    {
        // 0.7 respecte le format numérique mais pas le barème à 3 paliers (0 / 0.5 / 1) —
        // signe que le modèle n'a pas respecté la consigne, pas un score à arrondir silencieusement.
        var result = JudgeResponseParser.Parse("SCORE: 0.7\nJUSTIFICATION: trop généreux");

        result.Score.Should().BeNull();
    }

    [Fact]
    public void Parse_ScoreSuiviDAutresChiffres_NEstPasTronqueAUnFauxPositif()
    {
        // "10" ne doit pas être lu comme "1" tronqué à cause d'une regex trop permissive.
        var result = JudgeResponseParser.Parse("SCORE: 10\nJUSTIFICATION: hors barème");

        result.Score.Should().BeNull();
    }

    [Fact]
    public void Parse_AucuneBaliseScoreReconnaissable_RetourneScoreNull()
    {
        var result = JudgeResponseParser.Parse("Je pense que c'est plutôt correct.");

        result.Score.Should().BeNull();
        result.Rationale.Should().Be("Je pense que c'est plutôt correct.");
    }

    [Fact]
    public void Parse_ReponseVide_RetourneScoreNull()
    {
        JudgeResponseParser.Parse(string.Empty).Score.Should().BeNull();
    }

    [Fact]
    public void Parse_ReponseNulle_RetourneScoreNull()
    {
        JudgeResponseParser.Parse(null).Score.Should().BeNull();
    }
}
```

- [ ] **Step 2: Vérifier que les tests échouent**

Run: `dotnet test src/Back/AIExperience.slnx --filter JudgeResponseParserTests` (depuis `Step3/`)
Expected: FAIL — `AIExperience.Eval.Judge` introuvable.

- [ ] **Step 3: Implémenter `JudgeResult`**

```csharp
// Step3/src/Back/AIExperience.Eval/Judge/JudgeResult.cs
namespace AIExperience.Eval.Judge;

/// <summary>
/// Résultat du jugement de fidélité. Score null = échec de parsing explicite (jamais un repli
/// silencieux sur 0 ou 1) — le juge étant un modèle local faible, Rationale porte alors le texte
/// brut de la réponse pour permettre une relecture manuelle.
/// </summary>
public sealed record JudgeResult(double? Score, string? Rationale);
```

- [ ] **Step 4: Implémenter `JudgeResponseParser`**

```csharp
// Step3/src/Back/AIExperience.Eval/Judge/JudgeResponseParser.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace AIExperience.Eval.Judge;

/// <summary>
/// Parseur pur de la réponse du juge LLM (fidélité) — même philosophie que
/// BatchedRerankResponseParser : tolérant au bruit d'un petit modèle local, jamais de valeur
/// par défaut silencieuse en cas d'échec.
/// </summary>
public static class JudgeResponseParser
{
    // (?!\d) évite qu'un score "10" soit lu comme "1" tronqué.
    private static readonly Regex ScoreRegex = new(
        @"SCORE\s*[:\-]\s*([01](?:[.,]\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RationaleRegex = new(
        @"JUSTIFICATION\s*[:\-]\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static JudgeResult Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new JudgeResult(null, response);

        var scoreMatch = ScoreRegex.Match(response);
        if (!scoreMatch.Success)
            return new JudgeResult(null, response.Trim());

        var rawScore = scoreMatch.Groups[1].Value.Replace(',', '.');
        if (!double.TryParse(rawScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
            return new JudgeResult(null, response.Trim());

        // Seules les 3 valeurs du barème sont acceptées (voir EvalPrompts.JudgeSystem) : une
        // valeur hors barème (ex. 0.7) signale un non-respect de la consigne par le modèle,
        // traité comme un échec de parsing plutôt qu'arrondi silencieusement.
        if (score != 0.0 && score != 0.5 && score != 1.0)
            return new JudgeResult(null, response.Trim());

        var rationaleMatch = RationaleRegex.Match(response);
        var rationale = rationaleMatch.Success ? rationaleMatch.Groups[1].Value.Trim() : response.Trim();

        return new JudgeResult(score, rationale);
    }
}
```

- [ ] **Step 5: Lancer les tests, vérifier qu'ils passent**

Run: `dotnet test src/Back/AIExperience.slnx --filter JudgeResponseParserTests` (depuis `Step3/`)
Expected: PASS (9 tests).

- [ ] **Step 6: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 4: `FidelityJudge` (appelle `IChatClient`, testé avec un faux)

**Files:**
- Create: `Step3/src/Back/AIExperience.Eval/Judge/EvalPrompts.cs`
- Create: `Step3/src/Back/AIExperience.Eval/Judge/FidelityJudge.cs`
- Test: `Step3/src/Back/AIExperience.Tests/FidelityJudgeTests.cs`

**Interfaces:**
- Consumes: `Microsoft.Extensions.AI.IChatClient` (déjà enregistré en DI via `AddInfrastructure`),
  `JudgeResponseParser.Parse` (Task 3).
- Produces: `FidelityJudge(IChatClient).ScoreAsync(string question, string generatedAnswer, IReadOnlyList<string> expectedKeyPoints, CancellationToken ct = default) : Task<JudgeResult>`
  — utilisé par `EvalRunner` (Task 7).

- [ ] **Step 1: Écrire les tests (échouent)**

```csharp
// Step3/src/Back/AIExperience.Tests/FidelityJudgeTests.cs
using AIExperience.Eval.Judge;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="FidelityJudge"/> avec un faux IChatClient (même pattern que
/// QueryCondensationServiceTests) — pas de mock, juste une implémentation minimale.
/// </summary>
public sealed class FidelityJudgeTests
{
    private sealed class FakeChatClient(Func<string>? respond = null, bool throwOnCall = false) : IChatClient
    {
        public string? LastUserPrompt { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (throwOnCall)
                throw new InvalidOperationException("Échec LLM simulé.");
            LastUserPrompt = messages.Last().Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond!())));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Non utilisé par FidelityJudge.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task ScoreAsync_ReponseNominale_RetourneScoreEtJustificationParses()
    {
        var judge = new FidelityJudge(new FakeChatClient(() => "SCORE: 1\nJUSTIFICATION: complet et correct."));

        var result = await judge.ScoreAsync("Question ?", "Réponse générée.", ["point clé 1"]);

        result.Score.Should().Be(1.0);
        result.Rationale.Should().Be("complet et correct.");
    }

    [Fact]
    public async Task ScoreAsync_PromptInclutQuestionReponseEtPointsCles()
    {
        var fake = new FakeChatClient(() => "SCORE: 1\nJUSTIFICATION: ok");
        var judge = new FidelityJudge(fake);

        await judge.ScoreAsync("Ma question ?", "Ma réponse.", ["point A", "point B"]);

        fake.LastUserPrompt.Should().Contain("Ma question ?");
        fake.LastUserPrompt.Should().Contain("Ma réponse.");
        fake.LastUserPrompt.Should().Contain("point A");
        fake.LastUserPrompt.Should().Contain("point B");
    }

    [Fact]
    public async Task ScoreAsync_QuandLAppelLlmEchoue_ReplieSurScoreNullAvecMessageDErreur()
    {
        var judge = new FidelityJudge(new FakeChatClient(throwOnCall: true));

        var result = await judge.ScoreAsync("Question ?", "Réponse.", ["point"]);

        result.Score.Should().BeNull();
        result.Rationale.Should().Contain("Erreur lors de l'appel au juge");
    }
}
```

- [ ] **Step 2: Vérifier que les tests échouent**

Run: `dotnet test src/Back/AIExperience.slnx --filter FidelityJudgeTests` (depuis `Step3/`)
Expected: FAIL — `FidelityJudge` introuvable.

- [ ] **Step 3: Implémenter `EvalPrompts`**

```csharp
// Step3/src/Back/AIExperience.Eval/Judge/EvalPrompts.cs
namespace AIExperience.Eval.Judge;

/// <summary>Gabarits de prompt du juge de fidélité (LLM-as-judge), en français comme le reste du corpus.</summary>
public static class EvalPrompts
{
    public const string JudgeSystem =
        "Tu es un évaluateur strict de réponses générées par un système de questions-réponses. " +
        "Tu compares une réponse générée à une liste de points clés attendus et tu attribues un score " +
        "selon exactement 3 paliers : 0 (réponse incorrecte ou aucun point clé couvert), " +
        "0.5 (réponse partiellement correcte, certains points clés couverts), " +
        "1 (réponse correcte, tous les points clés essentiels couverts). " +
        "Réponds STRICTEMENT sous ce format, sans rien ajouter avant :\n" +
        "SCORE: <0, 0.5 ou 1>\n" +
        "JUSTIFICATION: <une phrase expliquant le score>";

    public const string JudgeUser =
        "Question : {question}\n\n" +
        "Réponse générée par le système :\n{answer}\n\n" +
        "Points clés attendus dans une bonne réponse :\n{keyPoints}";
}
```

- [ ] **Step 4: Implémenter `FidelityJudge`**

```csharp
// Step3/src/Back/AIExperience.Eval/Judge/FidelityJudge.cs
using Microsoft.Extensions.AI;

namespace AIExperience.Eval.Judge;

/// <summary>
/// Juge de fidélité (LLM-as-judge) : compare une réponse générée aux points clés attendus du jeu
/// golden, avec le même IChatClient (donc potentiellement le même modèle faible) que le pipeline
/// RAG évalué. Un échec de l'appel est dégradé gracieusement (même logique que
/// QueryCondensationService) plutôt que de faire échouer tout le run pour une seule question.
/// </summary>
public sealed class FidelityJudge(IChatClient chatClient)
{
    public async Task<JudgeResult> ScoreAsync(
        string question, string generatedAnswer, IReadOnlyList<string> expectedKeyPoints, CancellationToken ct = default)
    {
        var keyPoints = string.Join("\n", expectedKeyPoints.Select(p => $"- {p}"));
        var userPrompt = EvalPrompts.JudgeUser
            .Replace("{question}", question)
            .Replace("{answer}", generatedAnswer)
            .Replace("{keyPoints}", keyPoints);

        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, EvalPrompts.JudgeSystem),
                    new ChatMessage(ChatRole.User, userPrompt)
                ],
                new ChatOptions { MaxOutputTokens = 300, Temperature = 0.0f },
                ct);

            var text = string.Join("\n", response.Messages.Select(m => m.Text));
            return JudgeResponseParser.Parse(text);
        }
        catch (Exception ex)
        {
            return new JudgeResult(null, $"Erreur lors de l'appel au juge : {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Lancer les tests, vérifier qu'ils passent**

Run: `dotnet test src/Back/AIExperience.slnx --filter FidelityJudgeTests` (depuis `Step3/`)
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 5: `GoldenDatasetLoader` (testé)

**Files:**
- Create: `Step3/src/Back/AIExperience.Eval/GoldenDataset/GoldenQuestionDto.cs`
- Create: `Step3/src/Back/AIExperience.Eval/GoldenDataset/GoldenDatasetLoader.cs`
- Test: `Step3/src/Back/AIExperience.Tests/GoldenDatasetLoaderTests.cs`

**Interfaces:**
- Produces: `ExpectedSourceDto(string FileName, int? PageNumber = null, double? StartTimeSeconds = null, double? EndTimeSeconds = null)`,
  `GoldenQuestionDto(string Id, string Question, IReadOnlyList<string> ExpectedAnswerKeyPoints, IReadOnlyList<ExpectedSourceDto> ExpectedSources)`,
  `GoldenDatasetDto(IReadOnlyList<GoldenQuestionDto> Questions)`, `GoldenDatasetLoader.Load(string path) : GoldenDatasetDto`
  — utilisés par `EvalRunner` (Task 7).

- [ ] **Step 1: Écrire les tests (échouent)**

```csharp
// Step3/src/Back/AIExperience.Tests/GoldenDatasetLoaderTests.cs
using AIExperience.Eval.GoldenDataset;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="GoldenDatasetLoader"/> : chargement nominal et les 3 échecs rapides et
/// explicites (fichier introuvable, aucune question, id dupliqué, source manquante).
/// </summary>
public sealed class GoldenDatasetLoaderTests
{
    private static string WriteTempDataset(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid()}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_JeuNominal_ChargeLesQuestions()
    {
        var path = WriteTempDataset("""
        {
          "questions": [
            {
              "id": "q1",
              "question": "Quelle est la capitale ?",
              "expectedAnswerKeyPoints": ["Paris"],
              "expectedSources": [{ "fileName": "doc.pdf", "pageNumber": 1 }]
            }
          ]
        }
        """);

        var dataset = GoldenDatasetLoader.Load(path);

        dataset.Questions.Should().HaveCount(1);
        dataset.Questions[0].Id.Should().Be("q1");
        dataset.Questions[0].ExpectedSources[0].FileName.Should().Be("doc.pdf");
        dataset.Questions[0].ExpectedSources[0].PageNumber.Should().Be(1);
    }

    [Fact]
    public void Load_FichierIntrouvable_LeveFileNotFoundException()
    {
        var action = () => GoldenDatasetLoader.Load("chemin/inexistant.json");

        action.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_AucuneQuestion_LeveInvalidOperationException()
    {
        var path = WriteTempDataset("""{ "questions": [] }""");

        var action = () => GoldenDatasetLoader.Load(path);

        action.Should().Throw<InvalidOperationException>().WithMessage("*aucune question*");
    }

    [Fact]
    public void Load_IdDuplique_LeveInvalidOperationException()
    {
        var path = WriteTempDataset("""
        {
          "questions": [
            { "id": "q1", "question": "Q1 ?", "expectedAnswerKeyPoints": ["a"], "expectedSources": [{ "fileName": "d.pdf" }] },
            { "id": "q1", "question": "Q2 ?", "expectedAnswerKeyPoints": ["b"], "expectedSources": [{ "fileName": "d.pdf" }] }
          ]
        }
        """);

        var action = () => GoldenDatasetLoader.Load(path);

        action.Should().Throw<InvalidOperationException>().WithMessage("*dupliqués*");
    }

    [Fact]
    public void Load_QuestionSansSourceAttendue_LeveInvalidOperationException()
    {
        var path = WriteTempDataset("""
        {
          "questions": [
            { "id": "q1", "question": "Q1 ?", "expectedAnswerKeyPoints": ["a"], "expectedSources": [] }
          ]
        }
        """);

        var action = () => GoldenDatasetLoader.Load(path);

        action.Should().Throw<InvalidOperationException>().WithMessage("*aucune source attendue*");
    }
}
```

- [ ] **Step 2: Vérifier que les tests échouent**

Run: `dotnet test src/Back/AIExperience.slnx --filter GoldenDatasetLoaderTests` (depuis `Step3/`)
Expected: FAIL — `AIExperience.Eval.GoldenDataset` introuvable.

- [ ] **Step 3: Implémenter les DTOs**

```csharp
// Step3/src/Back/AIExperience.Eval/GoldenDataset/GoldenQuestionDto.cs
namespace AIExperience.Eval.GoldenDataset;

/// <summary>Source attendue référencée par nom de fichier (pas par DocumentId, qui change à chaque ré-ingestion).</summary>
public sealed record ExpectedSourceDto(
    string FileName,
    int? PageNumber = null,
    double? StartTimeSeconds = null,
    double? EndTimeSeconds = null);

/// <summary>Une entrée du jeu de données golden.</summary>
public sealed record GoldenQuestionDto(
    string Id,
    string Question,
    IReadOnlyList<string> ExpectedAnswerKeyPoints,
    IReadOnlyList<ExpectedSourceDto> ExpectedSources);

/// <summary>Racine du fichier JSON du jeu de données golden.</summary>
public sealed record GoldenDatasetDto(IReadOnlyList<GoldenQuestionDto> Questions);
```

- [ ] **Step 4: Implémenter `GoldenDatasetLoader`**

```csharp
// Step3/src/Back/AIExperience.Eval/GoldenDataset/GoldenDatasetLoader.cs
using System.Text.Json;

namespace AIExperience.Eval.GoldenDataset;

/// <summary>
/// Charge et valide le jeu de données golden depuis un fichier JSON. Échoue vite (exception,
/// message explicite) sur une entrée malformée plutôt que de laisser un run produire un rapport
/// silencieusement faussé.
/// </summary>
public static class GoldenDatasetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static GoldenDatasetDto Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Jeu de données golden introuvable : {path}");

        var json = File.ReadAllText(path);
        var dataset = JsonSerializer.Deserialize<GoldenDatasetDto>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Jeu de données golden vide ou illisible : {path}");

        Validate(dataset);
        return dataset;
    }

    private static void Validate(GoldenDatasetDto dataset)
    {
        if (dataset.Questions.Count == 0)
            throw new InvalidOperationException("Le jeu de données golden ne contient aucune question.");

        var duplicateIds = dataset.Questions
            .GroupBy(q => q.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Count > 0)
            throw new InvalidOperationException($"Identifiants de question dupliqués : {string.Join(", ", duplicateIds)}");

        foreach (var question in dataset.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Question))
                throw new InvalidOperationException($"Question '{question.Id}' : texte de question vide.");
            if (question.ExpectedSources.Count == 0)
                throw new InvalidOperationException($"Question '{question.Id}' : aucune source attendue.");
        }
    }
}
```

- [ ] **Step 5: Lancer les tests, vérifier qu'ils passent**

Run: `dotnet test src/Back/AIExperience.slnx --filter GoldenDatasetLoaderTests` (depuis `Step3/`)
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 6: Reporting — `EvalRunReport`, `EvalReportWriter`, `RunComparer` (testés)

**Files:**
- Create: `Step3/src/Back/AIExperience.Eval/Reporting/EvalQuestionResult.cs`
- Create: `Step3/src/Back/AIExperience.Eval/Reporting/EvalRunReport.cs`
- Create: `Step3/src/Back/AIExperience.Eval/Reporting/EvalReportWriter.cs`
- Create: `Step3/src/Back/AIExperience.Eval/Reporting/RunComparer.cs`
- Test: `Step3/src/Back/AIExperience.Tests/RunComparerTests.cs`

**Interfaces:**
- Consumes: `AIExperience.Rag.Domain.Enums.RagStrategy`, `AIExperience.Rag.Infrastructure.Options.RagOptions`
  (classe déjà existante, propriétés publiques `get;set;` — sérialisable telle quelle).
- Produces:
  - `EvalQuestionResult(string QuestionId, string Question, string Answer, RagStrategy StrategyUsed, bool RecallHit, int? RecallRank, double? FidelityScore, string? FidelityRationale, long DurationMs, string? Error = null)`
  - `EvalRunReport(DateTimeOffset RunAt, RagOptions RagOptionsSnapshot, IReadOnlyList<EvalQuestionResult> Questions, double MeanRecallAtK, double MeanReciprocalRank, double? MeanFidelityScore, int FidelityParseFailures)`
    avec `EvalRunReport.Build(DateTimeOffset, RagOptions, IReadOnlyList<EvalQuestionResult>) : EvalRunReport`
  - `EvalReportWriter.Write(EvalRunReport, string outputDirectory) : string` (chemin écrit),
    `EvalReportWriter.Read(string path) : EvalRunReport`
  - `RecallChange` (enum `Unchanged`/`HitToMiss`/`MissToHit`), `QuestionComparison`, `RunComparison`,
    `RunComparer.Compare(EvalRunReport before, EvalRunReport after) : RunComparison`
  — utilisés par `EvalRunner` (Task 7) et `Program.cs` (Task 8).

- [ ] **Step 1: Écrire les tests de `RunComparer` (échouent)**

```csharp
// Step3/src/Back/AIExperience.Tests/RunComparerTests.cs
using AIExperience.Eval.Reporting;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Infrastructure.Options;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="RunComparer"/> : détection des changements de recall (hit/miss),
/// delta de fidélité, et robustesse quand une question n'existe que dans un des deux rapports.
/// </summary>
public sealed class RunComparerTests
{
    private static EvalRunReport MakeReport(params EvalQuestionResult[] questions) =>
        EvalRunReport.Build(DateTimeOffset.UtcNow, new RagOptions(), questions);

    private static EvalQuestionResult MakeQuestion(string id, bool recallHit, double? fidelity) =>
        new(id, "Q ?", "Réponse", RagStrategy.Adaptive, recallHit, recallHit ? 1 : null, fidelity, "justification", 100);

    [Fact]
    public void Compare_RecallHitDevientMiss_EstDetecte()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));
        var after = MakeReport(MakeQuestion("q1", recallHit: false, fidelity: 0.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions[0].RecallChange.Should().Be(RecallChange.HitToMiss);
    }

    [Fact]
    public void Compare_RecallMissDevientHit_EstDetecte()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: false, fidelity: 0.0));
        var after = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions[0].RecallChange.Should().Be(RecallChange.MissToHit);
    }

    [Fact]
    public void Compare_DeltaDeFideliteCalcule()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 0.5));
        var after = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions[0].FidelityDelta.Should().Be(0.5);
    }

    [Fact]
    public void Compare_QuestionAbsenteDUnDesDeuxRapports_NeLeveAucuneException()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));
        var after = MakeReport(MakeQuestion("q2", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.Questions.Should().HaveCount(2);
        comparison.Questions.Single(q => q.QuestionId == "q1").RecallHitAfter.Should().BeNull();
        comparison.Questions.Single(q => q.QuestionId == "q2").RecallHitBefore.Should().BeNull();
    }

    [Fact]
    public void Compare_DeltaAgregeDeRecallAtK()
    {
        var before = MakeReport(MakeQuestion("q1", recallHit: false, fidelity: 0.0));
        var after = MakeReport(MakeQuestion("q1", recallHit: true, fidelity: 1.0));

        var comparison = RunComparer.Compare(before, after);

        comparison.RecallAtKDelta.Should().Be(1.0);
    }
}
```

- [ ] **Step 2: Vérifier que les tests échouent**

Run: `dotnet test src/Back/AIExperience.slnx --filter RunComparerTests` (depuis `Step3/`)
Expected: FAIL — `AIExperience.Eval.Reporting` introuvable.

- [ ] **Step 3: Implémenter `EvalQuestionResult`**

```csharp
// Step3/src/Back/AIExperience.Eval/Reporting/EvalQuestionResult.cs
using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Eval.Reporting;

/// <summary>Résultat d'une question golden pour un run donné. Error non-null si AskAsync a levé
/// une exception pour cette question (voir EvalRunner) — les autres champs restent alors neutres.</summary>
public sealed record EvalQuestionResult(
    string QuestionId,
    string Question,
    string Answer,
    RagStrategy StrategyUsed,
    bool RecallHit,
    int? RecallRank,
    double? FidelityScore,
    string? FidelityRationale,
    long DurationMs,
    string? Error = null);
```

- [ ] **Step 4: Implémenter `EvalRunReport`**

```csharp
// Step3/src/Back/AIExperience.Eval/Reporting/EvalRunReport.cs
using AIExperience.Rag.Infrastructure.Options;

namespace AIExperience.Eval.Reporting;

/// <summary>
/// Rapport agrégé d'un run d'évaluation. RagOptionsSnapshot permet, en comparant deux rapports,
/// de savoir QUOI a changé entre deux runs (pas seulement les métriques).
/// </summary>
public sealed record EvalRunReport(
    DateTimeOffset RunAt,
    RagOptions RagOptionsSnapshot,
    IReadOnlyList<EvalQuestionResult> Questions,
    double MeanRecallAtK,
    double MeanReciprocalRank,
    double? MeanFidelityScore,
    int FidelityParseFailures)
{
    public static EvalRunReport Build(DateTimeOffset runAt, RagOptions ragOptions, IReadOnlyList<EvalQuestionResult> questions)
    {
        var meanRecall = questions.Count == 0 ? 0.0 : questions.Count(q => q.RecallHit) / (double)questions.Count;
        var meanReciprocalRank = questions.Count == 0 ? 0.0
            : questions.Average(q => q is { RecallHit: true, RecallRank: { } rank } ? 1.0 / rank : 0.0);

        var fidelityScores = questions.Where(q => q.FidelityScore is not null).Select(q => q.FidelityScore!.Value).ToList();
        double? meanFidelity = fidelityScores.Count > 0 ? fidelityScores.Average() : null;

        // Un échec de parsing se distingue d'une question en échec (Error renseigné) : seul le
        // premier compte comme "échec de parsing" au sens du rapport.
        var parseFailures = questions.Count(q => q.FidelityScore is null && q.Error is null);

        return new EvalRunReport(runAt, ragOptions, questions, meanRecall, meanReciprocalRank, meanFidelity, parseFailures);
    }
}
```

- [ ] **Step 5: Implémenter `EvalReportWriter`**

```csharp
// Step3/src/Back/AIExperience.Eval/Reporting/EvalReportWriter.cs
using System.Text.Json;

namespace AIExperience.Eval.Reporting;

/// <summary>Écrit/lit un EvalRunReport en JSON horodaté — format d'échange entre les runs `run` et `compare`.</summary>
public static class EvalReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Write(EvalRunReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var fileName = $"{report.RunAt:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    public static EvalRunReport Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EvalRunReport>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Rapport illisible : {path}");
    }
}
```

- [ ] **Step 6: Implémenter `RunComparer`**

```csharp
// Step3/src/Back/AIExperience.Eval/Reporting/RunComparer.cs
namespace AIExperience.Eval.Reporting;

public enum RecallChange { Unchanged, HitToMiss, MissToHit }

public sealed record QuestionComparison(
    string QuestionId,
    bool? RecallHitBefore,
    bool? RecallHitAfter,
    RecallChange RecallChange,
    double? FidelityScoreBefore,
    double? FidelityScoreAfter,
    double? FidelityDelta);

public sealed record RunComparison(
    IReadOnlyList<QuestionComparison> Questions,
    double RecallAtKDelta,
    double MeanReciprocalRankDelta,
    double? MeanFidelityScoreDelta);

/// <summary>
/// Compare deux rapports de run pour un avant/après (ex. avant/après un correctif du pipeline
/// RAG). Une question absente de l'un des deux rapports (id désynchronisé, jeu de données modifié
/// entre les deux runs) est comparée avec des valeurs "avant"/"après" à null plutôt que de faire
/// échouer la comparaison.
/// </summary>
public static class RunComparer
{
    public static RunComparison Compare(EvalRunReport before, EvalRunReport after)
    {
        var beforeById = before.Questions.ToDictionary(q => q.QuestionId);
        var afterById = after.Questions.ToDictionary(q => q.QuestionId);
        var allIds = beforeById.Keys.Union(afterById.Keys);

        var comparisons = allIds.Select(id =>
        {
            beforeById.TryGetValue(id, out var b);
            afterById.TryGetValue(id, out var a);

            var recallChange = (b?.RecallHit, a?.RecallHit) switch
            {
                (true, false) => RecallChange.HitToMiss,
                (false, true) => RecallChange.MissToHit,
                _ => RecallChange.Unchanged
            };

            double? fidelityDelta = b?.FidelityScore is { } fb && a?.FidelityScore is { } fa ? fa - fb : null;

            return new QuestionComparison(id, b?.RecallHit, a?.RecallHit, recallChange, b?.FidelityScore, a?.FidelityScore, fidelityDelta);
        }).ToList();

        double? meanFidelityDelta = before.MeanFidelityScore is { } mfb && after.MeanFidelityScore is { } mfa
            ? mfa - mfb : null;

        return new RunComparison(
            comparisons,
            after.MeanRecallAtK - before.MeanRecallAtK,
            after.MeanReciprocalRank - before.MeanReciprocalRank,
            meanFidelityDelta);
    }
}
```

- [ ] **Step 7: Lancer les tests, vérifier qu'ils passent**

Run: `dotnet test src/Back/AIExperience.slnx --filter RunComparerTests` (depuis `Step3/`)
Expected: PASS (5 tests).

- [ ] **Step 8: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 7: `EvalRunner` (orchestration)

**Files:**
- Create: `Step3/src/Back/AIExperience.Eval/EvalRunner.cs`

**Interfaces:**
- Consumes: `IRagPipelineService.AskAsync(RagQuery, CancellationToken) : Task<RagResponse>` (existant),
  `IDocumentRepository.GetAllAsync(CancellationToken) : Task<IEnumerable<Document>>` (existant,
  `Document.Id`/`FileName`/`CreatedAt`), `GoldenDatasetLoader.Load` (Task 5), `RecallCalculator.Evaluate`
  (Task 2), `FidelityJudge.ScoreAsync` (Task 4), `EvalRunReport.Build` (Task 6).
- Produces: `EvalRunner(IRagPipelineService, IDocumentRepository, FidelityJudge, IOptions<RagOptions>).RunAsync(string datasetPath, CancellationToken ct = default) : Task<EvalRunReport>`
  — utilisé par `Program.cs` (Task 8).

Pas de test unitaire dédié (dépend de la vraie base + du vrai LLM, comme le reste du pipeline non
mocké — voir §8 de la spec) ; vérifié manuellement en Task 9.

- [ ] **Step 1: Implémenter `EvalRunner`**

```csharp
// Step3/src/Back/AIExperience.Eval/EvalRunner.cs
using System.Diagnostics;
using AIExperience.Eval.GoldenDataset;
using AIExperience.Eval.Judge;
using AIExperience.Eval.Metrics;
using AIExperience.Eval.Reporting;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Domain.Models;
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace AIExperience.Eval;

/// <summary>
/// Orchestre un run d'évaluation complet : résolution du corpus, exécution du pipeline RAG pour
/// chaque question golden, calcul du recall et de la fidélité, agrégation en rapport.
/// </summary>
public sealed class EvalRunner(
    IRagPipelineService ragPipelineService,
    IDocumentRepository documentRepository,
    FidelityJudge fidelityJudge,
    IOptions<RagOptions> ragOptions)
{
    // UserId dédié : ConversationRepository filtre les sessions par UserId, donc les sessions
    // générées par un run n'apparaissent jamais dans la barre latérale de conversation réelle.
    // Sans effet sur la récupération documentaire elle-même (non filtrée par utilisateur, S-1).
    private const string EvalUserId = "eval-harness";

    public async Task<EvalRunReport> RunAsync(string datasetPath, CancellationToken ct = default)
    {
        var dataset = GoldenDatasetLoader.Load(datasetPath);
        var documentIdsByFileName = await ResolveCorpusAsync(dataset, ct);

        var results = new List<EvalQuestionResult>();
        foreach (var question in dataset.Questions)
            results.Add(await EvaluateQuestionAsync(question, documentIdsByFileName, ct));

        return EvalRunReport.Build(DateTimeOffset.UtcNow, ragOptions.Value, results);
    }

    private async Task<Dictionary<string, Guid>> ResolveCorpusAsync(GoldenDatasetDto dataset, CancellationToken ct)
    {
        var documents = (await documentRepository.GetAllAsync(ct))
            .GroupBy(d => d.FileName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAt).First().Id);

        var requiredFileNames = dataset.Questions
            .SelectMany(q => q.ExpectedSources)
            .Select(s => s.FileName)
            .Distinct()
            .ToList();

        var missing = requiredFileNames.Where(f => !documents.ContainsKey(f)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Document(s) attendu(s) absent(s) du corpus : {string.Join(", ", missing)}");

        return documents;
    }

    private async Task<EvalQuestionResult> EvaluateQuestionAsync(
        GoldenQuestionDto question, Dictionary<string, Guid> documentIdsByFileName, CancellationToken ct)
    {
        var expectedSources = question.ExpectedSources
            .Select(s => new ExpectedSource(
                documentIdsByFileName[s.FileName], s.PageNumber, s.StartTimeSeconds, s.EndTimeSeconds))
            .ToList();

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await ragPipelineService.AskAsync(new RagQuery
            {
                Question = question.Question,
                Strategy = RagStrategy.Adaptive,
                UseLlm = true,
                UseRag = true,
                IncludeHistory = false,
                SessionId = Guid.Empty,
                UserId = EvalUserId
            }, ct);

            var recall = RecallCalculator.Evaluate(expectedSources, response.Citations);
            var fidelity = await fidelityJudge.ScoreAsync(
                question.Question, response.Answer, question.ExpectedAnswerKeyPoints, ct);
            sw.Stop();

            return new EvalQuestionResult(
                question.Id, question.Question, response.Answer, response.StrategyUsed,
                recall.Hit, recall.Rank, fidelity.Score, fidelity.Rationale, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Une question en échec (LLM indisponible, etc.) ne doit pas faire perdre les 11
            // autres résultats du run — dégradation par question, pas par run entier.
            sw.Stop();
            return new EvalQuestionResult(
                question.Id, question.Question, string.Empty, RagStrategy.Adaptive,
                RecallHit: false, RecallRank: null, FidelityScore: null, FidelityRationale: null,
                DurationMs: sw.ElapsedMilliseconds, Error: ex.Message);
        }
    }
}
```

- [ ] **Step 2: Vérifier que le projet compile**

Run: `dotnet build src/Back/AIExperience.slnx` (depuis `Step3/`)
Expected: Build réussi.

- [ ] **Step 3: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 8: `Program.cs` (CLI `run`/`compare`) + isolation DI

**Files:**
- Modify: `Step3/src/Back/AIExperience.Eval/Program.cs` (remplace le placeholder de Task 1)

**Interfaces:**
- Consumes: `EvalRunner.RunAsync` (Task 7), `EvalReportWriter.Write`/`Read`, `RunComparer.Compare`
  (Task 6).

- [ ] **Step 1: Remplacer `Program.cs`**

```csharp
// Step3/src/Back/AIExperience.Eval/Program.cs
using AIExperience.Eval;
using AIExperience.Eval.Judge;
using AIExperience.Eval.Reporting;
using AIExperience.Rag.Application;
using AIExperience.Rag.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication();
builder.Services.AddSingleton<FidelityJudge>();
builder.Services.AddScoped<EvalRunner>();

var app = builder.Build();

var command = args.Length > 0 ? args[0] : "run";

switch (command)
{
    case "run":
        await RunAsync(args);
        break;
    case "compare":
        Compare(args);
        break;
    default:
        Console.WriteLine($"Commande inconnue : {command}. Utiliser 'run' ou 'compare'.");
        break;
}

async Task RunAsync(string[] cliArgs)
{
    var datasetPath = GetOption(cliArgs, "--dataset") ?? "eval/golden-dataset.json";
    var outDir = GetOption(cliArgs, "--out") ?? "eval/runs";

    using var scope = app.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<EvalRunner>();

    Console.WriteLine($"=== Run d'évaluation — jeu de données : {datasetPath} ===\n");
    var report = await runner.RunAsync(datasetPath);
    var reportPath = EvalReportWriter.Write(report, outDir);

    foreach (var q in report.Questions)
    {
        var recallLabel = q.RecallHit ? $"HIT (rang {q.RecallRank})" : "MISS";
        var fidelityLabel = q.FidelityScore is { } score ? score.ToString("0.0") : "N/A";
        var errorLabel = q.Error is { } err ? $" — ERREUR: {err}" : string.Empty;
        Console.WriteLine($"[{q.QuestionId}] recall={recallLabel} fidélité={fidelityLabel}{errorLabel}");
    }

    Console.WriteLine($"\nRecall@K moyen    : {report.MeanRecallAtK:P0}");
    Console.WriteLine($"MRR moyen         : {report.MeanReciprocalRank:0.00}");
    Console.WriteLine($"Fidélité moyenne  : {(report.MeanFidelityScore is { } f ? f.ToString("0.00") : "N/A")}");
    Console.WriteLine($"Échecs de parsing : {report.FidelityParseFailures}");
    Console.WriteLine($"\nRapport écrit : {reportPath}");
}

void Compare(string[] cliArgs)
{
    if (cliArgs.Length < 3)
    {
        Console.WriteLine("Usage : compare <run-avant.json> <run-apres.json>");
        return;
    }

    var before = EvalReportWriter.Read(cliArgs[1]);
    var after = EvalReportWriter.Read(cliArgs[2]);
    var comparison = RunComparer.Compare(before, after);

    Console.WriteLine($"=== Comparaison {cliArgs[1]} → {cliArgs[2]} ===\n");
    foreach (var q in comparison.Questions)
    {
        var fidelityDelta = q.FidelityDelta is { } d ? d.ToString("+0.00;-0.00;0") : "N/A";
        Console.WriteLine($"[{q.QuestionId}] recall: {q.RecallChange} | fidélité: {fidelityDelta}");
    }

    Console.WriteLine($"\nΔ Recall@K moyen   : {comparison.RecallAtKDelta:+0.00;-0.00;0}");
    Console.WriteLine($"Δ MRR moyen        : {comparison.MeanReciprocalRankDelta:+0.00;-0.00;0}");
    Console.WriteLine($"Δ Fidélité moyenne : {(comparison.MeanFidelityScoreDelta is { } fd ? fd.ToString("+0.00;-0.00;0") : "N/A")}");

    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    Console.WriteLine("\n--- RagOptions avant ---");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(before.RagOptionsSnapshot, jsonOptions));
    Console.WriteLine("\n--- RagOptions après ---");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(after.RagOptionsSnapshot, jsonOptions));
}

static string? GetOption(string[] cliArgs, string name)
{
    var index = Array.IndexOf(cliArgs, name);
    return index >= 0 && index + 1 < cliArgs.Length ? cliArgs[index + 1] : null;
}
```

- [ ] **Step 2: Vérifier que le projet compile et s'exécute (aide/erreur attendue, pas de dataset encore)**

Run (depuis `Step3/`): `dotnet run --project src/Back/AIExperience.Eval -- run`
Expected: le programme démarre, tente de charger `eval/golden-dataset.json`, échoue avec
`System.IO.FileNotFoundException: Jeu de données golden introuvable : eval/golden-dataset.json`
(normal — créé en Task 9).

- [ ] **Step 3: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 9: Jeu de données golden réel + `.gitignore`

**Files:**
- Create: `Step3/eval/golden-dataset.json`
- Modify: `Step3/.gitignore`

**Interfaces:**
- Aucune — fichier de données consommé par `GoldenDatasetLoader` (Task 5) via `EvalRunner` (Task 7).

- [ ] **Step 1: Créer `Step3/eval/golden-dataset.json`**

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

- [ ] **Step 2: Ajouter `eval/runs/` au `.gitignore` de Step3**

Ajouter à la fin de `Step3/.gitignore` :

```
# Rapports d'exécution du harnais d'évaluation RAG (R-13/T-4) — artefacts horodatés,
# non versionnés (dépendent de l'état du LLM/corpus local au moment du run).
eval/runs/
```

- [ ] **Step 3: Vérifier que le loader accepte le fichier réel**

Run (depuis `Step3/`) : `dotnet run --project src/Back/AIExperience.Eval -- run`
Expected : le programme charge les 12 questions sans erreur de validation et commence à interroger
la base (peut échouer plus loin si Postgres/LM Studio ne tournent pas — normal, vérifié en Task 10).

- [ ] **Step 4: Commit**

Ne PAS commiter (consigne explicite).

---

### Task 10: Vérification manuelle end-to-end (`run` puis `compare`)

**Files:** aucun (vérification manuelle, pas de code).

Prérequis : `docker-compose up -d` (Postgres sur 5433) lancé depuis `Step3/`, LM Studio actif sur
`http://localhost:1234` avec les modèles configurés dans `appsettings.json`, et les 4 documents du
corpus déjà ingérés (déjà le cas en dev, confirmé en phase de conception).

- [ ] **Step 1: Premier run réel**

Run (depuis `Step3/`) : `dotnet run --project src/Back/AIExperience.Eval -- run`

Expected : le programme résout les 4 documents par nom, exécute les 12 questions, affiche un
tableau récapitulatif par question (recall hit/miss + rang, score de fidélité) puis les agrégats
(Recall@K moyen, MRR moyen, fidélité moyenne, échecs de parsing), et écrit
`eval/runs/<horodatage>.json`. Vérifier à l'œil que le fichier existe et contient bien 12 entrées
dans `questions` (`Get-Content eval/runs/<fichier>.json | ConvertFrom-Json | Select -Expand
questions | Measure-Object` doit renvoyer `Count : 12`).

- [ ] **Step 2: Relecture qualitative du rapport**

Ouvrir le JSON généré et relire manuellement 2-3 `FidelityRationale` (le juge étant un modèle local
faible, cette relecture humaine est le principal garde-fou de qualité — voir §2 point 6 de la spec).
Noter si des questions golden méritent un ajustement de formulation (`expectedAnswerKeyPoints` trop
strict par rapport à une reformulation légitime du LLM) — ajuster `eval/golden-dataset.json` si besoin.

- [ ] **Step 3: Second run et comparaison**

Relancer : `dotnet run --project src/Back/AIExperience.Eval -- run`
Puis : `dotnet run --project src/Back/AIExperience.Eval -- compare eval/runs/<premier>.json eval/runs/<second>.json`

Expected : la commande affiche, question par question, `Unchanged` pour la plupart (même
configuration, corpus stable) et les deltas agrégés proches de 0 — confirme que le mode `compare`
fonctionne avant de s'en servir pour un vrai avant/après (ex. activation/désactivation d'une option
`RagOptions`).

- [ ] **Step 4: Lancer la suite de tests complète**

Run (depuis `Step3/`) : `dotnet test src/Back/AIExperience.slnx`
Expected : tous les tests passent, y compris les nouveaux (`RecallCalculatorTests`,
`JudgeResponseParserTests`, `FidelityJudgeTests`, `GoldenDatasetLoaderTests`, `RunComparerTests`).

---

### Task 11: Documentation

**Files:**
- Modify: `Step3/src/Back/CLAUDE.md`
- Modify: `Step3/PLAN-AMELIORATION-RAG.md`
- Modify: `Step3/DETTE-TECHNIQUE.md`

- [ ] **Step 1: Ajouter la commande au `Step3/src/Back/CLAUDE.md`**, section « Commandes
  essentielles », après le bloc `dotnet run --project src/Back/AIExperience.App.Console` :

```markdown
dotnet run --project src/Back/AIExperience.Eval -- run       # harnais d'évaluation RAG (recall@k + fidélité)
dotnet run --project src/Back/AIExperience.Eval -- compare <run-a.json> <run-b.json>   # avant/après
```

- [ ] **Step 2: Marquer R-13 livré dans `Step3/PLAN-AMELIORATION-RAG.md`**

Repérer la ligne `| R-13 | Aucun **harnais d'évaluation** (...)` (§B.4, tableau des constats) et la
section détaillée `#### R-13 — Pas de harnais d'évaluation` (fin de fichier) : ajouter une mention
« ✅ Livré » avec un renvoi vers `src/Back/AIExperience.Eval` et `eval/golden-dataset.json`, dans le
même style que les autres points marqués livrés du fichier (ex. R-17, R-20).

- [ ] **Step 3: Marquer T-4 livré dans `Step3/DETTE-TECHNIQUE.md`**

Repérer la ligne `| **T-4** | Harnais d'évaluation RAG | ...` (tableau « Les cinq à traiter en
premier ») et la section `### T-4. Harnais d'évaluation RAG` : même traitement.

- [ ] **Step 4: Commit**

Ne PAS commiter (consigne explicite).
