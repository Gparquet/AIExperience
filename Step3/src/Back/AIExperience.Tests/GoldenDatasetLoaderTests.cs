using AIExperience.Eval.GoldenDataset;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests de <see cref="GoldenDatasetLoader"/> : chargement nominal et les échecs rapides et
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
