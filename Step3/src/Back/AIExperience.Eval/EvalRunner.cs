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
