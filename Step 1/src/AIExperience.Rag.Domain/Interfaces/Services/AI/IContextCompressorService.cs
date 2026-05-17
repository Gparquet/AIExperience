using AIExperience.Rag.Domain.Entities;

namespace AIExperience.Rag.Domain.Interfaces.Services.AI;

/// <summary>
/// Service de compression contextuelle des chunks avant injection dans le prompt.
/// Réduit le bruit en n'extrayant que les phrases directement pertinentes
/// pour la question, diminuant ainsi la consommation de tokens.
/// </summary>
public interface IContextCompressorService
{
    /// <summary>
    /// Compresse les chunks en n'extrayant que les portions pertinentes par rapport à la question.
    /// </summary>
    /// <param name="question">Question originale de l'utilisateur.</param>
    /// <param name="chunks">Chunks à compresser après reranking.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    /// <returns>Chunks avec un contenu compressé ne retenant que les phrases utiles.</returns>
    Task<IReadOnlyList<DocumentChunk>> CompressAsync(
        string question,
        IEnumerable<DocumentChunk> chunks,
        CancellationToken ct = default);
}
