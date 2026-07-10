using AIExperience.Rag.Application.Jobs;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="IngestionSignal"/> : vérifie qu'un <c>Pulse</c> réveille
/// immédiatement une attente en cours, et qu'en son absence l'attente expire normalement au
/// bout du délai fourni (repli sur la sonde périodique du worker).
/// </summary>
public sealed class IngestionSignalTests
{
    [Fact]
    public async Task Pulse_WakesUpAPendingWait()
    {
        var signal = new IngestionSignal();
        var waitTask = signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        signal.Pulse();

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.Should().Be(waitTask, "un Pulse doit réveiller l'attente sans attendre le délai complet");
    }

    [Fact]
    public async Task WaitAsync_WithoutPulse_ExpiresAfterTimeout()
    {
        var signal = new IngestionSignal();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await signal.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(90));
    }

    [Fact]
    public void Pulse_CalledMultipleTimesWithoutAnyWait_DoesNotThrow()
    {
        var signal = new IngestionSignal();

        var act = () =>
        {
            signal.Pulse();
            signal.Pulse();
            signal.Pulse();
        };

        act.Should().NotThrow();
    }
}
