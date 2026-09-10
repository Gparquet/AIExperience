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
        // Chemin absolu affiché explicitement : "path" est résolu par rapport au répertoire de
        // travail du processus, qui varie selon d'où la commande est lancée — sans ce détail,
        // un chemin relatif introuvable ne dit pas où l'outil a réellement cherché.
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Jeu de données golden introuvable : {path} (résolu en {Path.GetFullPath(path)})");

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
