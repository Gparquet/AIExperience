using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AIExperience.Rag.Application.Jobs;

/// <summary>
/// Consomme en continu la table outbox et déclenche, pour chaque message non traité, la commande
/// correspondante — ingestion de document ou transcription vidéo. Tourne dans le même
/// process que l'API : un redémarrage retrouve naturellement tout message resté non traité au
/// prochain tour de sonde, sans code de reprise dédié — c'est la sonde elle-même qui joue ce rôle.
/// </summary>
public sealed class IngestionWorker(
    IngestionSignal signal,
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un incident pendant le sondage lui-même (ex. base momentanément indisponible)
                // ne doit jamais arrêter le worker : on relogue et on retente au tour suivant.
                logger.LogError(ex, "Erreur inattendue lors du sondage de la file d'ingestion.");
            }

            // Réveillé soit par un signal immédiat (nouveau job mis en file), soit par l'intervalle
            // de sonde, qui sert de filet de sécurité si un signal a été manqué.
            await signal.WaitAsync(pollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        IReadOnlyList<OutboxMessage> messages;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            messages = await outboxRepository.GetUnprocessedAsync(options.Value.BatchSize, ct);
        }

        // Traitement séquentiel : Whisper/FFmpeg sont des services partagés dont la réentrance
        // concurrente n'est pas garantie, un seul job à la fois suffit pour ce lot.
        foreach (var message in messages)
            await ProcessOneAsync(message, ct);
    }

    private async Task ProcessOneAsync(OutboxMessage message, CancellationToken ct)
    {
        // Un scope dédié par message : chaque traitement obtient sa propre instance de contexte
        // EF Core, jamais partagée ni entre messages ni avec la boucle de sondage elle-même.
        await using var scope = scopeFactory.CreateAsyncScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        try
        {
            var payload = JsonSerializer.Deserialize<IngestionJobPayload>(message.Payload)
                ?? throw new InvalidOperationException($"Message outbox {message.Id} sans payload exploitable.");

            switch (message.EventType)
            {
                case IngestionEventTypes.DocumentIngestionRequested:
                    await dispatcher.SendAsync(new Document.Command.IngestDocumentCommand { DocumentId = payload.DocumentId }, ct);
                    break;
                case IngestionEventTypes.VideoTranscriptionRequested:
                    await dispatcher.SendAsync(new Video.Command.ProcessVideoTranscriptionJobCommand { DocumentId = payload.DocumentId }, ct);
                    break;
                default:
                    logger.LogWarning("Type d'événement outbox inconnu, ignoré : {EventType}", message.EventType);
                    break;
            }

            message.MarkProcessed();
        }
        catch (OperationCanceledException)
        {
            // Arrêt du worker en cours de traitement : le message reste non traité (ProcessedAt
            // toujours null), il sera repris tel quel au prochain tour de sonde.
            throw;
        }
        catch (Exception ex)
        {
            // La commande visée encadre déjà ses propres échecs métier (document marqué en erreur) ;
            // arriver ici signifie que le message lui-même n'a pas pu être délivré (payload corrompu,
            // document introuvable, base momentanément indisponible...). On retente un nombre limité
            // de fois avant d'abandonner, pour ne pas boucler indéfiniment sur un message cassé.
            logger.LogError(ex, "Échec du traitement du message outbox {MessageId} ({EventType}), tentative {Attempt}",
                message.Id, message.EventType, message.RetryCount + 1);
            message.MarkFailedAttempt(ex.Message);

            if (message.RetryCount >= options.Value.MaxRetryAttempts)
                message.MarkProcessed();
        }

        await outboxRepository.UpdateAsync(message, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
