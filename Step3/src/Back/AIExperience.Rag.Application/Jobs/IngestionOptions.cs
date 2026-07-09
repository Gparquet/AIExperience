namespace AIExperience.Rag.Application.Jobs;

/// <summary>
/// Options de configuration du worker d'ingestion en arrière-plan.
/// Lié à la section "Ingestion" de appsettings.json (binding effectué en Infrastructure/Web.Api).
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>Nom de la section dans appsettings.json.</summary>
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Répertoire où les fichiers uploadés sont copiés avant traitement, pour qu'ils survivent à
    /// la requête HTTP qui les a reçus (le worker les lit bien après que la réponse est repartie).
    /// </summary>
    public required string WorkDirectory { get; set; }

    /// <summary>Intervalle, en secondes, entre deux sondes de la table outbox en l'absence de réveil immédiat.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Nombre maximum de messages traités par tour de sonde.</summary>
    public int BatchSize { get; set; } = 5;

    /// <summary>
    /// Nombre de tentatives avant d'abandonner un message qui échoue avant même d'atteindre la
    /// commande visée (ex. payload corrompu) — au-delà, il est marqué traité pour éviter de le
    /// retenter indéfiniment à chaque sonde. N'affecte pas les échecs métier normaux (document
    /// marqué en erreur), qui ne sont jamais retentés puisque la commande a déjà fait son travail.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;
}
