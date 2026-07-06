using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour la propriété <see cref="Document.ContentHash"/> (détection de doublon à l'upload).
/// </summary>
public sealed class DocumentContentHashTests
{
    [Fact]
    public void Create_WithContentHash_SetsContentHash()
    {
        var document = Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport"),
            chunkingStrategy: ChunkingStrategy.Recursive,
            contentHash: "abc123");

        document.ContentHash.Should().Be("abc123");
    }

    [Fact]
    public void Create_WithoutContentHash_DefaultsToEmptyString()
    {
        var document = Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport"));

        document.ContentHash.Should().BeEmpty();
    }
}
