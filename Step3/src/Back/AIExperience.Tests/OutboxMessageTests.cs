using AIExperience.Rag.Domain.Entities;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="OutboxMessage"/> : création, marquage comme traité et
/// comptabilisation des tentatives en échec.
/// </summary>
public sealed class OutboxMessageTests
{
    [Fact]
    public void Create_SetsEventTypeAndPayload()
    {
        var message = OutboxMessage.Create("document-ingestion-requested", "{\"documentId\":\"abc\"}");

        message.EventType.Should().Be("document-ingestion-requested");
        message.Payload.Should().Be("{\"documentId\":\"abc\"}");
    }

    [Fact]
    public void Create_IsNotProcessedByDefault()
    {
        var message = OutboxMessage.Create("document-ingestion-requested", "{}");

        message.ProcessedAt.Should().BeNull();
        message.RetryCount.Should().Be(0);
    }

    [Fact]
    public void MarkProcessed_SetsProcessedAt()
    {
        var message = OutboxMessage.Create("document-ingestion-requested", "{}");

        message.MarkProcessed();

        message.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailedAttempt_IncrementsRetryCountWithoutMarkingProcessed()
    {
        var message = OutboxMessage.Create("document-ingestion-requested", "{}");

        message.MarkFailedAttempt("connexion base de données interrompue");

        message.RetryCount.Should().Be(1);
        message.Error.Should().Be("connexion base de données interrompue");
        message.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void MarkFailedAttempt_CalledTwice_AccumulatesRetryCount()
    {
        var message = OutboxMessage.Create("document-ingestion-requested", "{}");

        message.MarkFailedAttempt("échec 1");
        message.MarkFailedAttempt("échec 2");

        message.RetryCount.Should().Be(2);
        message.Error.Should().Be("échec 2");
    }
}
