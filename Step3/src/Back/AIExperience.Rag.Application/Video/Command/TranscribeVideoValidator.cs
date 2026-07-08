using FluentValidation;

namespace AIExperience.Rag.Application.Video.Command
{
    public sealed class TranscribeVideoValidator : AbstractValidator<TranscribeVideoCommand>
    {
        private static readonly string[] SupportedExtensions =
            [".mp4", ".mkv", ".webm", ".avi", ".mov", ".wav", ".mp3", ".m4a", ".ogg", ".flac"];

        public TranscribeVideoValidator()
        {
            RuleFor(x => x.FilePath)
                .NotEmpty().WithMessage("Le chemin du fichier est requis.")
                .Must(File.Exists).WithMessage("Le fichier spécifié n'existe pas.")
                .Must(path => SupportedExtensions.Contains(
                    Path.GetExtension(path).ToLowerInvariant()))
                .WithMessage($"Format non supporté. Formats acceptés : {string.Join(", ", SupportedExtensions)}");

            RuleFor(x => x.Language)
                .NotEmpty()
                .MaximumLength(5);
        }
    }
}
