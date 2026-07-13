using AIExperience.Rag.Application.Common.Cqrs;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace AIExperience.Tests.Cqrs;

/// <summary>Tests TDD du maillon <see cref="LoggingStep{TCommand,TResponse}"/>.</summary>
public sealed class LoggingStepTests
{
    private sealed record FakeCommand(string FileName) : ICommand<string>;

    /// <summary>Faux logger qui enregistre le niveau de chaque appel, sans dépendre d'un framework de mock.</summary>
    private sealed class RecordingLogger : ILogger<LoggingStep<FakeCommand, string>>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    [Fact]
    public async Task InvokeAsync_NextSucceeds_ReturnsResponseAndLogsInformation()
    {
        var logger = new RecordingLogger();
        var step = new LoggingStep<FakeCommand, string>(logger);

        var result = await step.InvokeAsync(new FakeCommand("a.pdf"), _ => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
        logger.Levels.Should().ContainSingle(l => l == LogLevel.Information);
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_LogsErrorAndRethrows()
    {
        var logger = new RecordingLogger();
        var step = new LoggingStep<FakeCommand, string>(logger);
        var thrown = new InvalidOperationException("échec métier");

        var act = () => step.InvokeAsync(new FakeCommand("a.pdf"), _ => Task.FromException<string>(thrown), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(thrown);
        logger.Levels.Should().ContainSingle(l => l == LogLevel.Error);
    }

    [Fact]
    public async Task InvokeAsync_NextThrowsOperationCanceled_PropagatesWithoutErrorLog()
    {
        var logger = new RecordingLogger();
        var step = new LoggingStep<FakeCommand, string>(logger);

        var act = () => step.InvokeAsync(new FakeCommand("a.pdf"), _ => Task.FromCanceled<string>(new CancellationToken(true)), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Levels.Should().NotContain(LogLevel.Error);
    }
}
