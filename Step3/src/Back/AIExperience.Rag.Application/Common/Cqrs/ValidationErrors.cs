namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>Une erreur de validation associée à la propriété de commande fautive.</summary>
public sealed record ValidationError(string Property, string Message);

/// <summary>
/// Collecteur d'erreurs de validation utilisé par les <see cref="ICommandValidator{TCommand}"/>.
/// Remplace l'API de FluentValidation par un équivalent minimal, sans dépendance externe.
/// </summary>
public sealed class ValidationErrors
{
    private readonly List<ValidationError> _errors = [];

    /// <summary>Indique si au moins une règle a échoué.</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Erreurs accumulées, dans l'ordre où les règles ont été évaluées.</summary>
    public IReadOnlyList<ValidationError> Errors => _errors;

    /// <summary>Ajoute une erreur si la condition d'échec est vraie.</summary>
    public ValidationErrors AddIf(bool failed, string property, string message)
    {
        if (failed)
            _errors.Add(new ValidationError(property, message));
        return this;
    }

    /// <summary>Raccourci : la valeur ne doit être ni nulle ni vide/blanche.</summary>
    public ValidationErrors NotEmpty(string? value, string property, string message)
        => AddIf(string.IsNullOrWhiteSpace(value), property, message);

    /// <summary>Raccourci : longueur maximale d'une chaîne (une valeur nulle est ignorée par cette règle).</summary>
    public ValidationErrors MaxLength(string? value, int max, string property, string message)
        => AddIf(value is not null && value.Length > max, property, message);
}
