namespace AIExperience.Rag.Domain.Interfaces.Services;

public interface ITextNomalize
{
    /// <summary>
    /// Normalise le texte extrait d'un document pour le rendre plus cohérent et facile à traiter.
    /// Par exemple, cela peut inclure la suppression des espaces inutiles, la correction de la casse, etc.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public string Normalize(string text);
}
