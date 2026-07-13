namespace AIExperience.Rag.Application.Common;

/// <summary>
/// Options temporaires tant que l'authentification utilisateur n'est pas implémentée.
/// Centralise l'UserId fixe utilisé en développement, lié à la section "DevAuth" de
/// appsettings.json (binding effectué en Infrastructure).
/// </summary>
public sealed class DevAuthOptions
{
    /// <summary>Nom de la section dans appsettings.json.</summary>
    public const string SectionName = "DevAuth";

    /// <summary>UserId fixe attribué à tous les documents/jobs créés tant qu'il n'y a pas de vrai compte utilisateur.</summary>
    public required string DefaultUserId { get; set; }
}
