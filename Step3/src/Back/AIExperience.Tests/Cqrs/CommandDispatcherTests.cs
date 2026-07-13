using AIExperience.Rag.Application.Common.Cqrs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AIExperience.Tests.Cqrs;

/// <summary>Tests TDD du <see cref="CommandDispatcher"/> : résolution du handler et composition des maillons.</summary>
public sealed class CommandDispatcherTests
{
    private sealed record FakeCommand(string FileName) : ICommand<string>;

    private sealed class FakeHandler : ICommandHandler<FakeCommand, string>
    {
        public Task<string> HandleAsync(FakeCommand command, CancellationToken cancellationToken) => Task.FromResult($"handled:{command.FileName}");
    }

    /// <summary>Maillon de test qui enregistre son passage (avant/après l'appel au maillon suivant) dans une trace partagée.</summary>
    private sealed class TracingStep(string name, List<string> trace) : ICommandPipelineStep<FakeCommand, string>
    {
        public async Task<string> InvokeAsync(FakeCommand command, CommandPipelineDelegate<string> next, CancellationToken ct)
        {
            trace.Add($"{name}:before");
            var result = await next(ct);
            trace.Add($"{name}:after");
            return result;
        }
    }

    private static ICommandDispatcher BuildDispatcher(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<ICommandHandler<FakeCommand, string>, FakeHandler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<ICommandDispatcher>();
    }

    [Fact]
    public async Task SendAsync_NoStepsRegistered_InvokesHandlerDirectly()
    {
        var dispatcher = BuildDispatcher();

        var result = await dispatcher.SendAsync(new FakeCommand("a.pdf"));

        result.Should().Be("handled:a.pdf");
    }

    [Fact]
    public async Task SendAsync_StepsRegistered_InvokedInRegistrationOrder_FirstRegisteredIsOutermost()
    {
        var trace = new List<string>();
        var dispatcher = BuildDispatcher(services =>
        {
            services.AddScoped<ICommandPipelineStep<FakeCommand, string>>(_ => new TracingStep("Outer", trace));
            services.AddScoped<ICommandPipelineStep<FakeCommand, string>>(_ => new TracingStep("Inner", trace));
        });

        await dispatcher.SendAsync(new FakeCommand("a.pdf"));

        trace.Should().ContainInOrder("Outer:before", "Inner:before", "Inner:after", "Outer:after");
    }

    [Fact]
    public async Task SendAsync_StepThrows_HandlerNotInvokedAndExceptionPropagates()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<ICommandHandler<FakeCommand, string>, FakeHandler>();
        services.AddScoped<ICommandPipelineStep<FakeCommand, string>>(_ =>
            new ThrowingStep());
        var dispatcher = services.BuildServiceProvider().GetRequiredService<ICommandDispatcher>();

        var act = () => dispatcher.SendAsync(new FakeCommand("a.pdf"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ThrowingStep : ICommandPipelineStep<FakeCommand, string>
    {
        public Task<string> InvokeAsync(FakeCommand command, CommandPipelineDelegate<string> next, CancellationToken ct)
            => throw new InvalidOperationException("échec du maillon");
    }
}
