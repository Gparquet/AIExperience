namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>
/// Handler métier d'une commande — ne contient que la logique métier ; les préoccupations
/// transverses (validation, journalisation...) sont composées autour par des <see cref="ICommandPipelineStep{TCommand,TResponse}"/>.
/// </summary>
public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    /// <summary>Exécute la logique métier de la commande.</summary>
    Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
