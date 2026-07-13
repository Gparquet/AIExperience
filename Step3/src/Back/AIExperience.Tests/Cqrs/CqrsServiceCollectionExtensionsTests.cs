using AIExperience.Rag.Application.Common.Cqrs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIExperience.Tests.Cqrs;

/// <summary>
/// Tests TDD de <c>AddCqrs</c> : découverte par réflexion des handlers/validateurs et composition
/// par défaut de la chaîne (Logging enveloppe Validation).
/// </summary>
public sealed class CqrsServiceCollectionExtensionsTests
{
    private sealed record FakeCommand(string FileName) : ICommand<string>;

    private sealed class FakeHandler : ICommandHandler<FakeCommand, string>
    {
        public Task<string> HandleAsync(FakeCommand command, CancellationToken cancellationToken) => Task.FromResult($"handled:{command.FileName}");
    }

    private sealed class RejectsEmptyFileNameValidator : ICommandValidator<FakeCommand>
    {
        public ValueTask ValidateAsync(FakeCommand command, ValidationErrors errors, CancellationToken ct)
        {
            errors.NotEmpty(command.FileName, nameof(command.FileName), "Le nom du fichier est obligatoire.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<LogLevel> Levels { get; } = [];
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Levels);
        public void Dispose() { }

        private sealed class RecordingLogger(List<LogLevel> levels) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => levels.Add(logLevel);
        }
    }

    private static (ICommandDispatcher Dispatcher, RecordingLoggerProvider Logs) BuildDispatcher()
    {
        var loggerProvider = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        services.AddCqrs(typeof(CqrsServiceCollectionExtensionsTests).Assembly);

        return (services.BuildServiceProvider().GetRequiredService<ICommandDispatcher>(), loggerProvider);
    }

    [Fact]
    public async Task AddCqrs_ScansAssembly_RegistersHandlerAutomatically()
    {
        var (dispatcher, _) = BuildDispatcher();

        var result = await dispatcher.SendAsync(new FakeCommand("a.pdf"));

        result.Should().Be("handled:a.pdf");
    }

    [Fact]
    public async Task AddCqrs_ScansAssembly_RegistersValidatorAutomatically()
    {
        var (dispatcher, _) = BuildDispatcher();

        var act = () => dispatcher.SendAsync(new FakeCommand(""));

        await act.Should().ThrowAsync<CommandValidationException>();
    }

    [Fact]
    public async Task AddCqrs_ValidationFails_LoggingStepStillLogsTheError()
    {
        // Ce test distingue l'ordre de composition : si Logging enveloppe bien Validation (comme
        // prévu), il voit passer l'exception de validation et la journalise en erreur. Si l'ordre
        // était inversé, Logging ne serait jamais atteint et aucune erreur ne serait journalisée.
        var (dispatcher, logs) = BuildDispatcher();

        var act = () => dispatcher.SendAsync(new FakeCommand(""));
        await act.Should().ThrowAsync<CommandValidationException>();

        logs.Levels.Should().Contain(LogLevel.Error);
    }
}
