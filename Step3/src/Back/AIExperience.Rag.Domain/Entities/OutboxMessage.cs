namespace AIExperience.Rag.Domain.Entities;

/// <summary>
/// Représente un événement à traiter en arrière-plan, écrit dans la même transaction que le
/// changement d'état qui l'a déclenché (ex. la création d'un document). Le worker qui consomme
/// ces messages n'a besoin de rien d'autre que ce qui est stocké ici pour rejouer un traitement
/// interrompu, y compris après un redémarrage brutal du process.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Identifiant unique du message.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Nature de l'événement (ex. "document-ingestion-requested"), utilisée par le worker pour choisir quelle commande exécuter.</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>Données de l'événement sérialisées en JSON (ex. l'identifiant du document concerné).</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>Date et heure d'écriture du message (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Date de traitement effectif. Reste <c>null</c> tant que le message n'a pas été traité —
    /// c'est ce champ qui permet au worker de retrouver, après un crash, tout ce qui restait à faire.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Dernière erreur rencontrée lors d'une tentative de traitement, le cas échéant.</summary>
    public string? Error { get; private set; }

    /// <summary>Nombre de tentatives de traitement effectuées sans succès jusqu'ici.</summary>
    public int RetryCount { get; private set; }

    private OutboxMessage() { }

    /// <summary>Crée un nouveau message à traiter.</summary>
    /// <param name="eventType">Nature de l'événement.</param>
    /// <param name="payload">Données associées, sérialisées en JSON.</param>
    public static OutboxMessage Create(string eventType, string payload)
        => new() { EventType = eventType, Payload = payload };

    /// <summary>Marque le message comme traité — qu'il ait réussi ou échoué proprement en aval ; il ne sera plus jamais rejoué.</summary>
    public void MarkProcessed()
        => ProcessedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Enregistre une tentative en échec avant même d'avoir pu exécuter la commande visée (ex.
    /// désérialisation du <see cref="Payload"/> impossible). Le message reste éligible à une
    /// nouvelle tentative tant que <see cref="ProcessedAt"/> n'est pas renseigné.
    /// </summary>
    /// <param name="error">Description de l'erreur rencontrée.</param>
    public void MarkFailedAttempt(string error)
    {
        Error = error;
        RetryCount++;
    }
}
