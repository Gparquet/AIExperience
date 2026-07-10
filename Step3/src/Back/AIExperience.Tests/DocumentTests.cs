using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="Document.SetDetectedLanguage"/> (constat I-6 du plan Lot 2).
/// Vérifie la mise à jour de la langue détectée sur les métadonnées du document.
/// </summary>
public sealed class DocumentTests
{
    private static Document CreateDocument(string initialLanguage = "fr")
        => Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport", language: initialLanguage));

    [Fact]
    public void SetDetectedLanguage_UpdatesMetadataLanguage()
    {
        var document = CreateDocument(initialLanguage: "fr");

        document.SetDetectedLanguage("en");

        document.Metadata.Language.Should().Be("en");
    }

    [Fact]
    public void SetDetectedLanguage_PreservesOtherMetadataFields()
    {
        var document = Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport annuel", author: "Service Finance", language: "fr"));

        document.SetDetectedLanguage("en");

        document.Metadata.Title.Should().Be("Rapport annuel");
        document.Metadata.Author.Should().Be("Service Finance");
    }

    [Fact]
    public void SetDetectedLanguage_UpdatesUpdatedAtTimestamp()
    {
        var document = CreateDocument();
        var before = document.UpdatedAt;

        Thread.Sleep(10);
        document.SetDetectedLanguage("en");

        document.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public void Create_WithoutExplicitId_GeneratesANewId()
    {
        var document = CreateDocument();

        document.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_WithExplicitId_UsesThatIdInsteadOfGeneratingOne()
    {
        var imposedId = Guid.NewGuid();

        var document = Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport"),
            id: imposedId);

        document.Id.Should().Be(imposedId);
    }

    [Fact]
    public void Create_DefaultsToIndexInRagTrueAndCleanTranscriptionWithLlmFalse()
    {
        var document = CreateDocument();

        document.IndexInRag.Should().BeTrue();
        document.CleanTranscriptionWithLlm.Should().BeFalse();
    }

    [Fact]
    public void Create_WithIndexInRagFalse_DisablesIndexing()
    {
        var document = Document.Create(
            fileName: "note-vocale.mp3",
            contentType: "audio/mpeg",
            fileSizeBytes: 2048,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Note vocale"),
            indexInRag: false,
            cleanTranscriptionWithLlm: true);

        document.IndexInRag.Should().BeFalse();
        document.CleanTranscriptionWithLlm.Should().BeTrue();
    }

    [Fact]
    public void SetTranscription_StoresRawAndCleanedText()
    {
        var document = CreateDocument();

        document.SetTranscription("texte brut transcrit", "texte nettoyé par le LLM");

        document.RawTranscription.Should().Be("texte brut transcrit");
        document.CleanedTranscription.Should().Be("texte nettoyé par le LLM");
    }

    [Fact]
    public void SetTranscription_WithoutCleanedVersion_LeavesCleanedTranscriptionNull()
    {
        var document = CreateDocument();

        document.SetTranscription("texte brut transcrit", null);

        document.CleanedTranscription.Should().BeNull();
    }

    [Fact]
    public void SetTranscription_UpdatesUpdatedAtTimestamp()
    {
        var document = CreateDocument();
        var before = document.UpdatedAt;

        Thread.Sleep(10);
        document.SetTranscription("texte brut", null);

        document.UpdatedAt.Should().BeAfter(before);
    }
}
