using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>
/// Maillon d'observabilité : chronomètre l'ensemble de la chaîne (y compris les maillons suivants,
/// comme la validation) et journalise l'issue. Une annulation (arrêt d'un worker, client
/// déconnecté) n'est pas un échec métier et n'est donc pas journalisée en erreur.
/// </summary>
public sealed class LoggingStep<TCommand, TResponse>(ILogger<LoggingStep<TCommand, TResponse>> logger)
    : ICommandPipelineStep<TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> InvokeAsync(TCommand command, CommandPipelineDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken);
            logger.LogInformation("Commande {Command} exécutée en {ElapsedMs} ms", typeof(TCommand).Name, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Échec de la commande {Command} après {ElapsedMs} ms", typeof(TCommand).Name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
