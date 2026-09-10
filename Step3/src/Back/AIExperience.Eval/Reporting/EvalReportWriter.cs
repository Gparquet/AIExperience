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
