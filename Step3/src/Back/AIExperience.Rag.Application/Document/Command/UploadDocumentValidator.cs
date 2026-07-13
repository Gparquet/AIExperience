using AIExperience.Rag.Application.Common.Cqrs;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>Règles de validation de <see cref="UploadDocumentCommand"/>, exécutées par <see cref="ValidationStep{TCommand,TResponse}"/>.</summary>
public sealed class UploadDocumentValidator : ICommandValidator<UploadDocumentCommand>
{
    // ".doc" et ".xls" (formats binaires legacy Office) ont été retirés : aucun extracteur ne les
    // supporte (DocxTextExtractor ne gère que .docx, ExcelTextExtractor ne gère que .xlsx/.csv), donc
    // un upload avec ces extensions passait la validation puis échouait à l'extraction
    // (NotSupportedException), marquant le document Failed — un chemin bout-en-bout cassé pour un
    // format faussement annoncé comme supporté (revue finale, constat Important n°1).
    private static readonly string[] AllowedExtensions =
        [".pdf", ".docx", ".xlsx", ".txt", ".csv", ".pptx", ".md", ".json", ".html", ".htm",
         ".mp4", ".mkv", ".webm", ".avi", ".mov",          // vidéo
         ".wav", ".mp3", ".m4a", ".ogg", ".flac"];          // audio

    // "application/octet-stream" est ajouté car de nombreux navigateurs n'ont pas de type MIME
    // enregistré pour certaines extensions (notamment ".md") et retombent sur ce type générique de
    // "binaire inconnu" plutôt que sur "text/markdown"/"text/x-markdown" — sans cette entrée, un
    // upload ".md" pourtant listé comme supporté pouvait échouer au contrôle de content-type en
    // conditions réelles (revue finale, constat Important n°3). La whitelist d'extensions reste le
    // véritable filtre : ajouter ce type générique n'ouvre pas de contournement de cette whitelist.
    private static readonly string[] AllowedContentTypes =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "text/plain",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/markdown", "text/x-markdown",
        "application/json",
        "text/html",
        // Vidéo
        "video/mp4", "video/x-matroska", "video/webm", "video/avi", "video/quicktime",
        // Audio
        "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/ogg", "audio/flac",
        // Type MIME générique de repli envoyé par certains navigateurs pour des extensions sans
        // association MIME OS (voir commentaire ci-dessus, ex. ".md").
        "application/octet-stream"
    ];

    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 Mo

    public ValueTask ValidateAsync(UploadDocumentCommand command, ValidationErrors errors, CancellationToken cancellationToken)
    {
        errors.NotEmpty(command.FileName, nameof(command.FileName), "Le nom du fichier est obligatoire.")
              .MaxLength(command.FileName, 255, nameof(command.FileName), "Le nom du fichier ne peut pas dépasser 255 caractères.")
              .AddIf(!HaveAllowedExtension(command.FileName), nameof(command.FileName),
                  $"Extension non autorisée. Extensions acceptées : {string.Join(", ", AllowedExtensions)}");

        errors.NotEmpty(command.ContentType, nameof(command.ContentType), "Le type MIME est obligatoire.")
              .AddIf(!string.IsNullOrWhiteSpace(command.ContentType) && !BeAllowedContentType(command.ContentType),
                  nameof(command.ContentType), "Type de fichier non supporté.");

        errors.AddIf(command.FileSizeBytes <= 0, nameof(command.FileSizeBytes), "Le fichier ne peut pas être vide.")
              .AddIf(command.FileSizeBytes > MaxFileSizeBytes, nameof(command.FileSizeBytes), "Le fichier ne peut pas dépasser 50 Mo.");

        errors.NotEmpty(command.UserId, nameof(command.UserId), "L'identifiant utilisateur est obligatoire.");

        if (command.DocumentMetadata is null)
        {
            errors.AddIf(true, nameof(command.DocumentMetadata), "Les métadonnées du document sont obligatoires.");
        }
        else
        {
            errors.NotEmpty(command.DocumentMetadata.Title, $"{nameof(command.DocumentMetadata)}.{nameof(command.DocumentMetadata.Title)}",
                      "Le titre du document est obligatoire.")
                  .MaxLength(command.DocumentMetadata.Title, 500, $"{nameof(command.DocumentMetadata)}.{nameof(command.DocumentMetadata.Title)}",
                      "Le titre ne peut pas dépasser 500 caractères.");
        }

        return ValueTask.CompletedTask;
    }

    private static bool HaveAllowedExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return AllowedExtensions.Contains(extension);
    }

    private static bool BeAllowedContentType(string contentType) =>
        AllowedContentTypes.Contains(contentType.ToLowerInvariant());
}
