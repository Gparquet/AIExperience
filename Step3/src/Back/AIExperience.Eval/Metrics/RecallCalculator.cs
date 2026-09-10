using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Eval.Metrics;

/// <summary>
/// Calcule le recall au niveau des citations finales du pipeline RAG (après fusion hybride et
/// reranking) plutôt qu'au niveau de la récupération vectorielle brute — c'est ce que voit
/// réellement l'utilisateur, et cela ne nécessite aucune instrumentation du pipeline de production.
/// </summary>
public static class RecallCalculator
{
    public static RecallResult Evaluate(IReadOnlyList<ExpectedSource> expectedSources, IReadOnlyList<Citation> citations)
    {
        for (var i = 0; i < citations.Count; i++)
        {
            if (expectedSources.Any(expected => Matches(expected, citations[i])))
                return new RecallResult(true, i + 1);
        }
        return new RecallResult(false, null);
    }

    private static bool Matches(ExpectedSource expected, Citation citation)
    {
        if (citation.DocumentId != expected.DocumentId)
            return false;

        if (expected.PageNumber is { } page && citation.PageNumber != page)
            return false;

        if (expected.StartTimeSeconds is { } start && expected.EndTimeSeconds is { } end)
        {
            // Chevauchement d'intervalles [start, end] : la citation doit couvrir au moins une
            // partie de la plage attendue, pas nécessairement l'englober exactement (le chunking
            // temporel peut légèrement décaler les bornes d'un chunk à la ré-ingestion).
            if (citation.StartTime is not { } citStart || citation.EndTime is not { } citEnd)
                return false;
            if (!(citStart.TotalSeconds < end && citEnd.TotalSeconds > start))
                return false;
        }

        return true;
    }
}
