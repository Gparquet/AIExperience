namespace AIExperience.Rag.Application.Services;

/// <summary>Un sous-lot d'éléments contigus : index de départ (inclus) et nombre d'éléments.</summary>
public readonly record struct IngestionBatch(int Start, int Count);

/// <summary>
/// Découpe utilitaire, pure et testable, pour l'embedding en sous-lots. Isolée du service pour
/// pouvoir vérifier le calcul « lot i/n » sans monter tout le pipeline d'ingestion.
/// </summary>
public static class IngestionBatching
{
    /// <summary>Découpe <paramref name="total"/> éléments en sous-lots d'au plus <paramref name="batchSize"/>.</summary>
    public static IReadOnlyList<IngestionBatch> Split(int total, int batchSize)
    {
        if (batchSize < 1) batchSize = 1;
        var batches = new List<IngestionBatch>();
        for (var start = 0; start < total; start += batchSize)
            batches.Add(new IngestionBatch(start, Math.Min(batchSize, total - start)));
        return batches;
    }
}
