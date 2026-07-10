using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Domain.Models;

/// <summary>
/// Compteurs optionnels décrivant l'avancement fin d'une étape. Chaque champ n'a de sens que pour
/// certaines étapes (les lots pour l'embedding, les segments pour la transcription) et reste
/// <c>null</c> ailleurs.
/// </summary>
public sealed record IngestionProgressCounters(
    int? BatchIndex = null,
    int? BatchCount = null,
    int? ChunksDone = null,
    int? ChunksTotal = null,
    int? SegmentsDone = null,
    int? SegmentsTotal = null)
{
    /// <summary>Instance sans aucun compteur renseigné (étapes atomiques).</summary>
    public static readonly IngestionProgressCounters Empty = new();
}

/// <summary>
/// État instantané de l'avancement d'une ingestion : l'étape courante, son pourcentage (si connu)
/// et des compteurs de détail. Persisté en base (colonne JSONB) et poussé au front en temps réel.
/// </summary>
public sealed record IngestionProgress(
    IngestionStage Stage,
    int? Percent,
    IngestionProgressCounters Counters,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Fabrique un instantané de progression horodaté à l'instant courant (UTC).
    /// </summary>
    /// <param name="stage">Étape en cours.</param>
    /// <param name="percent">Pourcentage de l'étape (0-100), ou <c>null</c> si indéterminé/atomique.</param>
    /// <param name="counters">Compteurs de détail, ou <c>null</c> pour aucun.</param>
    public static IngestionProgress Create(
        IngestionStage stage,
        int? percent = null,
        IngestionProgressCounters? counters = null)
        => new(stage, percent, counters ?? IngestionProgressCounters.Empty, DateTimeOffset.UtcNow);
}
