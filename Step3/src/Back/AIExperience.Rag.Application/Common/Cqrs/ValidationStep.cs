namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>
/// Maillon de validation : exécute tous les <see cref="ICommandValidator{TCommand}"/> enregistrés
/// pour la commande et bloque l'exécution si au moins une règle échoue. Une commande sans
/// validateur enregistré traverse ce maillon sans coût.
/// </summary>
public sealed class ValidationStep<TCommand, TResponse>(IEnumerable<ICommandValidator<TCommand>> validators)
    : ICommandPipelineStep<TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> InvokeAsync(TCommand command, CommandPipelineDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        foreach (var validator in validators)
            await validator.ValidateAsync(command, errors, cancellationToken);

        if (errors.HasErrors)
            throw new CommandValidationException(typeof(TCommand).Name, errors.Errors);

        return await next(cancellationToken);
    }
}
