namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>
/// Levée par <see cref="ValidationStep{TCommand,TResponse}"/> quand au moins une règle de
/// validation échoue.
/// </summary>
public sealed class CommandValidationException(string commandName, IReadOnlyList<ValidationError> errors)
    : Exception($"La commande {commandName} a échoué la validation : {string.Join(" | ", errors.Select(e => $"{e.Property}: {e.Message}"))}")
{
    /// <summary>Nom du type de la commande qui a échoué la validation.</summary>
    public string CommandName { get; } = commandName;

    /// <summary>Erreurs de validation détaillées, propriété par propriété.</summary>
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
