using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Rag.Infrastructure.AI.Rag;

/// <summary>
/// Implémente l'algorithme Reciprocal Rank Fusion (RRF) pour fusionner plusieurs listes de chunks.
/// Formule : score_rrf(d) = Σ 1 / (k + rang(d))  où k = 60.
/// Source : Cormack, Clarke, Buettcher (2009) — "Reciprocal Rank Fusion outperforms Condorcet
/// and individual Rank Learning Methods".
/// k=60 est la valeur empiriquement optimale pour la recherche documentaire.
/// </summary>
public static class ReciprocalRankFusion
{
    /// <summary>
    /// Constante de lissage RRF. k=60 est la valeur recommandée dans le papier original.
    /// Une valeur plus élevée aplatit les scores (favorise le consensus) ;
    /// une valeur plus faible amplifie les premiers rangs (favorise les têtes de liste).
    /// </summary>
    private const int K = 60;

    /// <summary>
    /// Fusionne plusieurs listes de résultats de recherche en une liste unique classée par score RRF.
    /// Un chunk qui apparaît en tête de plusieurs listes reçoit un score cumulé élevé.
    /// Chaque chunk n'apparaît qu'une seule fois dans le résultat final.
    /// </summary>
    /// <param name="resultLists">Ensemble de listes ordonnées, chacune issue d'une requête différente.</param>
    /// <returns>Liste fusionnée triée par score RRF décroissant.</returns>
    public static IReadOnlyList<(DocumentChunk Chunk, double Score)> Fuse(
        IEnumerable<IReadOnlyList<(DocumentChunk Chunk, double Score)>> resultLists)
    {
        // ChunkId → (Chunk, score RRF cumulé sur toutes les listes)
        var scores = new Dictionary<Guid, (DocumentChunk Chunk, double RrfScore)>();

        foreach (var resultList in resultLists)
        {
            foreach (var (chunk, _, rank) in resultList.Select((r, i) => (r.Chunk, r.Score, i)))
            {
                // rang + 1 car les index commencent à 0
                var rrfScore = 1.0 / (K + rank + 1);

                if (scores.TryGetValue(chunk.Id, out var existing))
                    scores[chunk.Id] = (existing.Chunk, existing.RrfScore + rrfScore);
                else
                    scores[chunk.Id] = (chunk, rrfScore);
            }
        }

        return scores.Values
            .OrderByDescending(v => v.RrfScore)
            .Select(v => (v.Chunk, v.RrfScore))
            .ToList();
    }
}
