using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Représente la réponse produite par le pipeline RAG suite à une <see cref="RagQuery"/>.
/// Contient la réponse générée, les citations des sources et les métriques d'exécution.
/// </summary>
public sealed record RagResponse
{
    /// <summary>Réponse générée par le modèle en langage naturel.</summary>
    public required string Answer { get; init; }

    /// <summary>Liste des citations de sources ayant servi à construire la réponse.</summary>
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    /// <summary>Stratégie RAG effectivement utilisée pour cette requête.</summary>
    public RagStrategy StrategyUsed { get; init; }

    /// <summary>Nombre total de tokens consommés (prompt + completion).</summary>
    public int TotalTokens { get; init; }

    /// <summary>Durée totale du pipeline RAG en millisecondes (embedding + recherche + génération).</summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Identifiant de la session de conversation à laquelle appartient cet échange.
    /// Toujours renseigné par le pipeline : nouvelle session créée si la requête n'en portait pas,
    /// ou session existante réutilisée. Permet au front de rattacher la réponse à un fil de discussion.
    /// </summary>
    public Guid SessionId { get; init; }
}
