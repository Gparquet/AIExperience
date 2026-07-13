using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Domain.Enums;
using FluentAssertions;

namespace AIExperience.Tests.Cqrs;

/// <summary>Tests TDD de <see cref="UploadDocumentValidator"/>, portage des règles FluentValidation vers <see cref="ICommandValidator{TCommand}"/>.</summary>
public sealed class UploadDocumentValidatorTests
{
    private static UploadDocumentCommand ValidCommand(Action<UploadDocumentCommandBuilder>? configure = null)
    {
        var builder = new UploadDocumentCommandBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Petit constructeur de commande valide par défaut, pour ne faire varier qu'un seul champ par test.</summary>
    private sealed class UploadDocumentCommandBuilder
    {
        private string _fileName = "document.pdf";
        private string _contentType = "application/pdf";
        private long _fileSizeBytes = 1024;
        private string _userId = "user-1";
        private DocumentMetadata? _metadata = DocumentMetadata.Create(title: "Titre valide", language: "fr");

        public UploadDocumentCommandBuilder WithFileName(string value) { _fileName = value; return this; }
        public UploadDocumentCommandBuilder WithContentType(string value) { _contentType = value; return this; }
        public UploadDocumentCommandBuilder WithFileSizeBytes(long value) { _fileSizeBytes = value; return this; }
        public UploadDocumentCommandBuilder WithUserId(string value) { _userId = value; return this; }
        public UploadDocumentCommandBuilder WithMetadata(DocumentMetadata? value) { _metadata = value; return this; }
        public UploadDocumentCommandBuilder WithTitle(string title) { _metadata = DocumentMetadata.Create(title: title, language: "fr"); return this; }

        public UploadDocumentCommand Build() => new()
        {
            FileName = _fileName,
            ContentType = _contentType,
            FileSizeBytes = _fileSizeBytes,
            UserId = _userId,
            DocumentMetadata = _metadata!,
            FilePath = "unused.pdf"
        };
    }

    private static async Task<ValidationErrors> ValidateAsync(UploadDocumentCommand command)
    {
        var errors = new ValidationErrors();
        await new UploadDocumentValidator().ValidateAsync(command, errors, CancellationToken.None);
        return errors;
    }

    [Fact]
    public async Task ValidateAsync_ValidCommand_HasNoErrors()
    {
        var errors = await ValidateAsync(ValidCommand());

        errors.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_EmptyFileName_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithFileName("")));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.FileName));
    }

    [Fact]
    public async Task ValidateAsync_FileNameTooLong_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithFileName(new string('a', 252) + ".pdf")));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.FileName));
    }

    [Theory]
    [InlineData("document.doc")]
    [InlineData("document.xls")]
    [InlineData("document.exe")]
    public async Task ValidateAsync_DisallowedExtension_AddsError(string fileName)
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithFileName(fileName)));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.FileName));
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("video.mp4")]
    [InlineData("audio.mp3")]
    [InlineData("notes.md")]
    public async Task ValidateAsync_AllowedExtension_HasNoFileNameError(string fileName)
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithFileName(fileName)));

        errors.Errors.Should().NotContain(e => e.Property == nameof(UploadDocumentCommand.FileName));
    }

    [Fact]
    public async Task ValidateAsync_EmptyContentType_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithContentType("")));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.ContentType));
    }

    [Fact]
    public async Task ValidateAsync_DisallowedContentType_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithContentType("application/x-msdownload")));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.ContentType));
    }

    [Fact]
    public async Task ValidateAsync_GenericOctetStreamContentType_HasNoContentTypeError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithContentType("application/octet-stream")));

        errors.Errors.Should().NotContain(e => e.Property == nameof(UploadDocumentCommand.ContentType));
    }

    [Fact]
    public async Task ValidateAsync_ZeroFileSize_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithFileSizeBytes(0)));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.FileSizeBytes));
    }

    [Fact]
    public async Task ValidateAsync_FileSizeAboveMax_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithFileSizeBytes(51 * 1024 * 1024)));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.FileSizeBytes));
    }

    [Fact]
    public async Task ValidateAsync_EmptyUserId_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithUserId("")));

        errors.Errors.Should().Contain(e => e.Property == nameof(UploadDocumentCommand.UserId));
    }

    [Fact]
    public async Task ValidateAsync_EmptyTitle_AddsError()
    {
        var errors = await ValidateAsync(ValidCommand(b => b.WithTitle("")));

        errors.Errors.Should().Contain(e => e.Property.Contains("Title"));
    }
}
