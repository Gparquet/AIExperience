namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>Point d'entrée unique d'envoi des commandes — remplace <c>ISender</c> de MediatR.</summary>
public interface ICommandDispatcher
{
    /// <summary>Résout le handler de la commande, compose la chaîne de maillons transverses, puis exécute le tout.</summary>
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
}
