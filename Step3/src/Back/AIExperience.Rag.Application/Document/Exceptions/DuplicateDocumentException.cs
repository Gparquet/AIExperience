using AIExperience.Rag.Domain.Enums;

namespace AIExperience.Rag.Application.Document.Exceptions;

/// <summary>
/// Levée par <see cref="Command.UploadDocumentHandler"/> lorsqu'un document de même nom existe déjà
/// (contenu identique ou différent, voir <see cref="MatchType"/>) et que l'appelant n'a pas confirmé
/// son remplacement via <see cref="Command.UploadDocumentCommand.ReplaceDocumentId"/>.
/// </summary>
public sealed class DuplicateDocumentException(
    Guid existingDocumentId, string existingFileName, DateTimeOffset existingCreatedAt, DocumentMatchType matchType)
    : Exception(DuplicateDocumentException.BuildMessage(existingFileName, existingDocumentId, matchType))
{
    /// <summary>Identifiant du document existant en conflit.</summary>
    public Guid ExistingDocumentId { get; } = existingDocumentId;

    /// <summary>Nom de fichier du document existant en conflit.</summary>
    public string ExistingFileName { get; } = existingFileName;

    /// <summary>Date de création du document existant en conflit.</summary>
    public DateTimeOffset ExistingCreatedAt { get; } = existingCreatedAt;

    /// <summary>Type de correspondance détecté : contenu identique ou nom identique avec contenu différent.</summary>
    public DocumentMatchType MatchType { get; } = matchType;

    private static string BuildMessage(string existingFileName, Guid existingDocumentId, DocumentMatchType matchType) =>
        matchType == DocumentMatchType.ExactDuplicate
            ? $"Un document identique (\"{existingFileName}\") existe déjà (Id: {existingDocumentId})."
            : $"Une version différente de \"{existingFileName}\" existe déjà (Id: {existingDocumentId}).";
}
