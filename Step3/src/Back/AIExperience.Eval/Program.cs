using System.Runtime.CompilerServices;
using AIExperience.Eval;
using AIExperience.Eval.Judge;
using AIExperience.Eval.Reporting;
using AIExperience.Rag.Application;
using AIExperience.Rag.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ContentRootPath forcé sur le dossier de sortie (et non le répertoire d'exécution de la
// commande) : "dotnet run --project ... -- run" depuis Step3/ laisse le cwd du processus sur
// Step3/, où appsettings.json n'existe pas — sans ce réglage, la config AI reste vide et
// AddAIClients échoue à la construction du client OpenAI (ApiKey vide).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
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
    var datasetPath = GetOption(cliArgs, "--dataset") ?? Path.Combine(Step3EvalDir(), "golden-dataset.json");
    var outDir = GetOption(cliArgs, "--out") ?? Path.Combine(Step3EvalDir(), "runs");

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

// [CallerFilePath] capture le chemin de CE fichier (Program.cs) à la compilation — indépendant
// du répertoire courant du processus au lancement, qui varie selon d'où "dotnet run" est invoqué
// (Step3/, src/Back/, ou ailleurs) et a été la source de plusieurs échecs de résolution de
// "eval/golden-dataset.json" en chemin relatif. Program.cs vit dans
// Step3/src/Back/AIExperience.Eval/ : remonter 3 niveaux atteint Step3/.
static string Step3EvalDir([CallerFilePath] string sourceFile = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "..", "eval"));
