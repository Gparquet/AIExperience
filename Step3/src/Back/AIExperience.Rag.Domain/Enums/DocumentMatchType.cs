namespace AIExperience.Rag.Domain.Enums;

/// <summary>Type de correspondance détecté entre un fichier uploadé et un document existant de même nom.</summary>
public enum DocumentMatchType
{
    /// <summary>Nom de fichier et hash de contenu strictement identiques.</summary>
    ExactDuplicate,

    /// <summary>Même nom de fichier, contenu différent — probable nouvelle version du document.</summary>
    SameNameDifferentContent
}
