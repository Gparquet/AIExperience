namespace AIExperience.Rag.Domain.Interfaces.Services.AI;

/// <summary>
/// Génère un document hypothétique à partir d'une question utilisateur.
/// Utilisé par la stratégie HyDE pour améliorer la qualité de la recherche vectorielle :
/// l'embedding du document fictif est géométriquement plus proche des vrais chunks que celui de la question brute.
/// </summary>
public interface IHydeService
{
    /// <summary>
    /// Génère un court passage factuel qui répondrait à la question donnée.
    /// L'embedding de ce document hypothétique est ensuite utilisé à la place
    /// de la question pour la recherche pgvector.
    /// </summary>
    /// <param name="question">La question de l'utilisateur.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Le texte du document hypothétique généré par le LLM.</returns>
    Task<string> GenerateHypotheticalDocAsync(string question, CancellationToken ct = default);
}
