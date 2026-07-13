using AIExperience.Rag.Application.Common.Cqrs;
using FluentAssertions;

namespace AIExperience.Tests.Cqrs;

/// <summary>Tests TDD du maillon <see cref="ValidationStep{TCommand,TResponse}"/>.</summary>
public sealed class ValidationStepTests
{
    private sealed record FakeCommand(string FileName) : ICommand<string>;

    private sealed class AlwaysPassingValidator : ICommandValidator<FakeCommand>
    {
        public ValueTask ValidateAsync(FakeCommand command, ValidationErrors errors, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class RejectsEmptyFileNameValidator : ICommandValidator<FakeCommand>
    {
        public ValueTask ValidateAsync(FakeCommand command, ValidationErrors errors, CancellationToken ct)
        {
            errors.NotEmpty(command.FileName, nameof(command.FileName), "Le nom du fichier est obligatoire.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RejectsShortFileNameValidator : ICommandValidator<FakeCommand>
    {
        public ValueTask ValidateAsync(FakeCommand command, ValidationErrors errors, CancellationToken ct)
        {
            errors.AddIf(command.FileName.Length < 3, nameof(command.FileName), "Le nom du fichier est trop court.");
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task InvokeAsync_NoValidatorRegistered_CallsNext()
    {
        var step = new ValidationStep<FakeCommand, string>([]);
        var nextCalled = false;

        var result = await step.InvokeAsync(new FakeCommand("a.pdf"), ct => { nextCalled = true; return Task.FromResult("ok"); }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_AllValidatorsPass_CallsNext()
    {
        var step = new ValidationStep<FakeCommand, string>([new AlwaysPassingValidator()]);

        var result = await step.InvokeAsync(new FakeCommand("a.pdf"), _ => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_ValidatorFails_ThrowsWithoutCallingNext()
    {
        var step = new ValidationStep<FakeCommand, string>([new RejectsEmptyFileNameValidator()]);
        var nextCalled = false;

        var act = () => step.InvokeAsync(new FakeCommand(""), ct => { nextCalled = true; return Task.FromResult("ok"); }, CancellationToken.None);

        await act.Should().ThrowAsync<CommandValidationException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_MultipleValidators_AggregatesErrorsFromAll()
    {
        var step = new ValidationStep<FakeCommand, string>([new RejectsEmptyFileNameValidator(), new RejectsShortFileNameValidator()]);

        var act = () => step.InvokeAsync(new FakeCommand(""), _ => Task.FromResult("ok"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CommandValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
    }
}
