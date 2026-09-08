using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Rag.Domain.Interfaces.Services.AI;

/// <summary>
/// Réécrit une question de suivi elliptique en question autonome à partir de l'historique de
/// conversation.
/// </summary>
public interface IQueryCondensationService
{
    /// <summary>
    /// Condense <paramref name="question"/> en question autonome à partir de <paramref name="history"/>.
    /// Retourne la question inchangée si l'historique est vide, si l'appel LLM échoue, ou si sa
    /// réponse est inexploitable — la condensation ne doit jamais faire échouer la récupération.
    /// </summary>
    /// <param name="question">Question de suivi telle que posée par l'utilisateur.</param>
    /// <param name="history">Tours précédents de la conversation, du plus ancien au plus récent.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>La question condensée, ou <paramref name="question"/> inchangée en cas de repli.</returns>
    Task<string> CondenseAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken ct = default);
}
