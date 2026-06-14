using FluentValidation;

namespace AIExperience.Rag.Application.Document.Command;

public sealed class UploadDocumentValidator : AbstractValidator<UploadDocumentCommand>
{
    private static readonly string[] AllowedExtensions =
        [".pdf", ".docx", ".doc", ".xlsx", ".xls", ".txt",
         ".mp4", ".mkv", ".webm", ".avi", ".mov",          // vidéo
         ".wav", ".mp3", ".m4a", ".ogg", ".flac"];          // audio

    private static readonly string[] AllowedContentTypes =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "text/plain",
        // Vidéo
        "video/mp4", "video/x-matroska", "video/webm", "video/avi", "video/quicktime",
        // Audio
        "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/ogg", "audio/flac"
    ];

    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 Mo

    /// <summary>
    /// Initialise les règles de validation pour l'upload de document.
    /// </summary>
    public UploadDocumentValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Le nom du fichier est obligatoire.")
            .MaximumLength(255).WithMessage("Le nom du fichier ne peut pas dépasser 255 caractères.")
            .Must(HaveAllowedExtension).WithMessage($"Extension non autorisée. Extensions acceptées : {string.Join(", ", AllowedExtensions)}");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Le type MIME est obligatoire.")
            .Must(BeAllowedContentType).WithMessage("Type de fichier non supporté.");

        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0).WithMessage("Le fichier ne peut pas être vide.")
            .LessThanOrEqualTo(MaxFileSizeBytes).WithMessage("Le fichier ne peut pas dépasser 50 Mo.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("L'identifiant utilisateur est obligatoire.");

        RuleFor(x => x.DocumentMetadata)
            .NotNull().WithMessage("Les métadonnées du document sont obligatoires.");

        RuleFor(x => x.DocumentMetadata.Title)
            .NotEmpty().WithMessage("Le titre du document est obligatoire.")
            .MaximumLength(500).WithMessage("Le titre ne peut pas dépasser 500 caractères.")
            .When(x => x.DocumentMetadata is not null);
    }

    private static bool HaveAllowedExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return AllowedExtensions.Contains(extension);
    }

    private static bool BeAllowedContentType(string contentType) =>
        AllowedContentTypes.Contains(contentType.ToLowerInvariant());
}
