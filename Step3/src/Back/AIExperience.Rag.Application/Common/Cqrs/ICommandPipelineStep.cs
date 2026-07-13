namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>Représente la suite de la chaîne de traitement (le maillon suivant, ou le handler final).</summary>
public delegate Task<TResponse> CommandPipelineDelegate<TResponse>(CancellationToken cancellationToken);

/// <summary>
/// Maillon transverse composé autour d'un handler de commande — même modèle qu'un middleware HTTP.
/// Chaque maillon reçoit la commande et le délégué vers le maillon suivant : il peut agir avant,
/// après, ou court-circuiter la chaîne.
/// </summary>
public interface ICommandPipelineStep<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    /// <summary>Traite la commande, en délégant tout ou partie du travail au maillon suivant via <paramref name="next"/>.</summary>
    Task<TResponse> InvokeAsync(TCommand command, CommandPipelineDelegate<TResponse> next, CancellationToken cancellationToken);
}
