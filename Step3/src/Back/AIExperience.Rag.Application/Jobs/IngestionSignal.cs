namespace AIExperience.Rag.Application.Jobs;

/// <summary>
/// Réveille le worker d'ingestion dès qu'un nouveau job vient d'être mis en file, plutôt que de le
/// laisser attendre le prochain tour de sonde périodique. Purement un raccourci de latence : rien
/// ne dépend de sa fiabilité — si un signal est perdu (redémarrage, course improbable), la sonde
/// périodique du worker retrouve de toute façon le message correspondant dans la table outbox.
/// </summary>
public sealed class IngestionSignal
{
    private readonly SemaphoreSlim _semaphore = new(0);

    /// <summary>Réveille le worker immédiatement s'il est en train d'attendre.</summary>
    public void Pulse()
    {
        // Un seul jeton en attente suffit : le worker relance une sonde complète de la table
        // au réveil, pas la peine d'empiler les réveils pour plusieurs jobs mis en file d'un coup.
        if (_semaphore.CurrentCount == 0)
            _semaphore.Release();
    }

    /// <summary>Attend soit un <see cref="Pulse"/>, soit l'expiration du délai fourni.</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct)
        => _semaphore.WaitAsync(timeout, ct);
}
