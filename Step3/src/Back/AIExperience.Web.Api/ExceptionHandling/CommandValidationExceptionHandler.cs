using AIExperience.Rag.Application.Common.Cqrs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AIExperience.Web.Api.ExceptionHandling;

/// <summary>
/// Convertit une <see cref="CommandValidationException"/> (levée par <see cref="ValidationStep{TCommand,TResponse}"/>
/// quand une commande échoue sa validation) en réponse 400 <see cref="ValidationProblemDetails"/>.
/// Corrige la dette de l'ancien pipeline MediatR : l'exception de validation n'y était interceptée
/// nulle part côté Web.Api et remontait en 500 au lieu d'une 400 exploitable par le front.
/// </summary>
public sealed class CommandValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CommandValidationException validationException)
            return false;

        var errorsByProperty = validationException.Errors
            .GroupBy(e => e.Property)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(
            new ValidationProblemDetails(errorsByProperty)
            {
                Title = "La commande a échoué la validation.",
                Status = StatusCodes.Status400BadRequest
            },
            cancellationToken);

        return true;
    }
}
