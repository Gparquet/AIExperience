namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>
/// Marque une commande d'écriture produisant une réponse de type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">Type de la réponse renvoyée après traitement de la commande.</typeparam>
public interface ICommand<TResponse>;
