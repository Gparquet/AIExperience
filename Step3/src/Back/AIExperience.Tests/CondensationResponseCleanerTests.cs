using AIExperience.Rag.Infrastructure.AI.Rag;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="CondensationResponseCleaner"/>.
/// Logique pure, testable sans dépendance LLM — même esprit que <see cref="BatchedRerankResponseParserTests"/>.
/// </summary>
public sealed class CondensationResponseCleanerTests
{
    [Fact]
    public void Clean_ReponsePropre_EstRetourneeTelleQuelle()
    {
        CondensationResponseCleaner.Clean("Quels sont les horaires du magasin le week-end ?")
            .Should().Be("Quels sont les horaires du magasin le week-end ?");
    }

    [Fact]
    public void Clean_AvecPreambuleDeBruit_EstNettoyee()
    {
        // Un petit modèle local ajoute parfois un préambule malgré la consigne stricte du prompt.
        CondensationResponseCleaner.Clean("Question reformulée : Quels sont les horaires du magasin le week-end ?")
            .Should().Be("Quels sont les horaires du magasin le week-end ?");
    }

    [Fact]
    public void Clean_AvecGuillemets_LesRetire()
    {
        CondensationResponseCleaner.Clean("\"Quels sont les horaires du magasin le week-end ?\"")
            .Should().Be("Quels sont les horaires du magasin le week-end ?");
    }

    [Fact]
    public void Clean_ReponseVide_RetourneNull()
    {
        CondensationResponseCleaner.Clean("").Should().BeNull();
    }

    [Fact]
    public void Clean_ReponseWhitespaceUniquement_RetourneNull()
    {
        CondensationResponseCleaner.Clean("   \n  ").Should().BeNull();
    }

    [Fact]
    public void Clean_ReponseNulle_RetourneNull()
    {
        CondensationResponseCleaner.Clean(null).Should().BeNull();
    }

    [Fact]
    public void Clean_ReponseTropLongue_RetourneNull()
    {
        // Une question condensée reste courte : une réponse bien plus longue signale que le modèle
        // a halluciné une réponse complète au lieu de reformuler la question.
        var tooLong = new string('a', 501);

        CondensationResponseCleaner.Clean(tooLong).Should().BeNull();
    }
}
