using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Représente une requête entrante vers le pipeline RAG.
/// Encapsule la question de l'utilisateur ainsi que tous les paramètres de configuration de la recherche.
/// </summary>
public sealed record RagQuery
{
    /// <summary>Question posée par l'utilisateur en langage naturel.</summary>
    public required string Question { get; init; }

    /// <summary>Identifiant de la session de conversation pour l'injection de l'historique.</summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// Liste des identifiants de documents à interroger.
    /// Si vide, tous les documents accessibles sont interrogés.
    /// </summary>
    public IReadOnlyList<Guid> DocumentIds { get; init; } = [];

    /// <summary>Stratégie RAG à utiliser. Par défaut : <see cref="RagStrategy.Adaptive"/>.</summary>
    public RagStrategy Strategy { get; init; } = RagStrategy.Adaptive;

    /// <summary>Indique si l'historique de conversation doit être injecté dans le prompt.</summary>
    public bool IncludeHistory { get; init; } = true;

    /// <summary>Nombre maximum de tours de conversation à inclure dans le contexte.</summary>
    public int MaxHistoryTurns { get; init; } = 5;
}
