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
