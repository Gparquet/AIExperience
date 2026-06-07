using FluentValidation;
using MediatR;

namespace AIExperience.Rag.Application.Common.Behaviors;

/// <summary>
/// Behavior MediatR de validation automatique.
/// Exécute tous les <see cref="IValidator{T}"/> enregistrés pour la commande/requête
/// avant que le handler ne soit appelé. Lève une <see cref="ValidationException"/> si des erreurs sont détectées.
/// </summary>
/// <typeparam name="TRequest">Type de la commande ou requête MediatR.</typeparam>
/// <typeparam name="TResponse">Type de la réponse attendue.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    /// <summary>
    /// Intercepte la requête, exécute les validateurs et passe au handler suivant si tout est valide.
    /// </summary>
    /// <param name="request">La commande ou requête à valider.</param>
    /// <param name="next">Délégué vers le handler suivant dans le pipeline.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>La réponse du handler si la validation réussit.</returns>
    /// <exception cref="ValidationException">Si au moins une règle de validation échoue.</exception>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next(cancellationToken);
    }
}
