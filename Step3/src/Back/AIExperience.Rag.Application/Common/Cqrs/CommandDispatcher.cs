using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>
/// Résout le handler d'une commande et l'enveloppe des maillons transverses enregistrés pour son
/// type, dans l'ordre de leur enregistrement DI (le premier enregistré est le plus externe — il
/// voit passer les échecs des maillons suivants, y compris la validation).
/// </summary>
public sealed class CommandDispatcher(IServiceProvider provider) : ICommandDispatcher
{
    // Fermer le type générique par réflexion est coûteux : on ne le fait qu'une fois par type de
    // commande, puis on réutilise l'instance mise en cache (comme le fait MediatR lui-même).
    private static readonly ConcurrentDictionary<Type, object> WrapperCache = new();

    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        var wrapper = (CommandWrapper<TResponse>)WrapperCache.GetOrAdd(command.GetType(),
            commandType => Activator.CreateInstance(
                typeof(CommandWrapperImpl<,>).MakeGenericType(commandType, typeof(TResponse)))!);

        return wrapper.HandleAsync(command, provider, cancellationToken);
    }

    /// <summary>Pont non générique vers générique : permet à <see cref="SendAsync{TResponse}"/> d'appeler un handler fortement typé sans connaître son type de commande à la compilation.</summary>
    private abstract class CommandWrapper<TResponse>
    {
        public abstract Task<TResponse> HandleAsync(ICommand<TResponse> command, IServiceProvider serviceProvider, CancellationToken cancellationToken);
    }

    private sealed class CommandWrapperImpl<TCommand, TResponse> : CommandWrapper<TResponse>
        where TCommand : ICommand<TResponse>
    {
        public override Task<TResponse> HandleAsync(ICommand<TResponse> command, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var typedCommand = (TCommand)command;
            var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

            // Cœur de la chaîne : l'appel du handler métier.
            CommandPipelineDelegate<TResponse> pipeline = token => handler.HandleAsync(typedCommand, token);

            // Enroule les maillons autour du handler, du plus proche (dernier enregistré) au plus
            // externe (premier enregistré), pour que l'ordre d'exécution suive l'ordre d'enregistrement.
            foreach (var step in serviceProvider.GetServices<ICommandPipelineStep<TCommand, TResponse>>().Reverse())
            {
                var next = pipeline;
                var current = step;
                pipeline = token => current.InvokeAsync(typedCommand, next, token);
            }

            return pipeline(cancellationToken);
        }
    }
}
