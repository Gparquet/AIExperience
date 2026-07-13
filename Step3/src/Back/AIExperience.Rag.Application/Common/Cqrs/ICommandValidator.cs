namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>
/// Règles de validation d'une commande — une classe dédiée par commande ayant besoin d'être
/// validée. Remplace <c>AbstractValidator{T}</c> de FluentValidation.
/// </summary>
public interface ICommandValidator<in TCommand>
{
    /// <summary>Évalue les règles de validation et accumule les erreurs éventuelles dans <paramref name="errors"/>.</summary>
    ValueTask ValidateAsync(TCommand command, ValidationErrors errors, CancellationToken cancellationToken);
}
