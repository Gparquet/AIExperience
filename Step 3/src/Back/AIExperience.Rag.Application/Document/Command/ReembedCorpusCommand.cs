using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Ré-embed tous les chunks déjà persistés, sans repasser par l'extraction/chunking des documents sources.
/// sont incompatibles avec des requêtes désormais préfixées — la comparaison cosinus perd son sens.
/// </summary>
public sealed record ReembedCorpusCommand : IRequest<ReembedCorpusResult>;
