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
}
