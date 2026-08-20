using HostMcp.Core.Providers;
using HostMcp.Core.Services;
using HostMcp.Core.Terminal;
using Xunit;

namespace HostMcp.Core.Tests;

/// <summary>Delay provider that records requested delays and returns immediately.</summary>
internal sealed class RecordingDelayProvider : IDelayProvider
{
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}

public class ReadySynchronizerTests
{
    private static TerminalOptions FastOptions() => new()
    {
        PollInterval = TimeSpan.FromMilliseconds(1),
        ReadyTimeout = TimeSpan.FromSeconds(5),
        WaitForTextTimeout = TimeSpan.FromSeconds(5),
        SettleDelay = TimeSpan.FromMilliseconds(7),
    };

    [Fact]
    public async Task WaitForReady_ReturnsImmediately_WhenKeyboardIsAlreadyUnlocked()
    {
        using var session = new FakeTerminalSession();
        var delay = new RecordingDelayProvider();
        var sync = new ReadySynchronizer(FastOptions(), delay);

        var oia = await sync.WaitForReadyAsync(session);

        Assert.False(oia.InputInhibited);
        Assert.Equal([TimeSpan.FromMilliseconds(7)], delay.Delays);
    }

    [Fact]
    public async Task WaitForReady_PollsUntilTheHostReleasesTheKeyboard()
    {
        using var session = new FakeTerminalSession { InhibitPollsPerAid = 3 };
        session.AddField(1, 1, 8);
        await session.SendKeysAsync(HostMcp.Core.Models.MnemonicParser.Parse("[enter]"));

        var delay = new RecordingDelayProvider();
        var sync = new ReadySynchronizer(FastOptions(), delay);

        var oia = await sync.WaitForReadyAsync(session);

        Assert.False(oia.InputInhibited);

        // Three inhibited polls produce three poll delays, plus the trailing settle delay.
        Assert.Equal(4, delay.Delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(7), delay.Delays[^1]);
    }

    [Fact]
    public async Task WaitForReady_Throws_WhenTheKeyboardStaysLocked()
    {
        using var session = new FakeTerminalSession { InhibitPollsPerAid = int.MaxValue };
        await session.SendKeysAsync(HostMcp.Core.Models.MnemonicParser.Parse("[enter]"));

        var sync = new ReadySynchronizer(FastOptions(), new RecordingDelayProvider());

        var ex = await Assert.ThrowsAsync<TerminalTimeoutException>(
            () => sync.WaitForReadyAsync(session, TimeSpan.Zero));

        Assert.Contains("inhibited", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WaitForScreen_ReturnsTheScreenSatisfyingThePredicate()
    {
        using var session = new FakeTerminalSession();
        session.Poke(1, 1, "READY");

        var sync = new ReadySynchronizer(FastOptions(), new RecordingDelayProvider());

        var screen = await sync.WaitForScreenAsync(session, s => s.Find("READY") is not null, "READY");

        Assert.NotNull(screen.Find("READY"));
    }

    [Fact]
    public async Task WaitForScreen_Throws_WhenThePredicateNeverBecomesTrue()
    {
        using var session = new FakeTerminalSession();
        var sync = new ReadySynchronizer(FastOptions(), new RecordingDelayProvider());

        var ex = await Assert.ThrowsAsync<TerminalTimeoutException>(
            () => sync.WaitForScreenAsync(session, _ => false, "the impossible", TimeSpan.Zero));

        Assert.Contains("the impossible", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForReady_HonoursCancellation()
    {
        using var session = new FakeTerminalSession { InhibitPollsPerAid = int.MaxValue };
        await session.SendKeysAsync(HostMcp.Core.Models.MnemonicParser.Parse("[enter]"));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sync = new ReadySynchronizer(FastOptions(), new RecordingDelayProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sync.WaitForReadyAsync(session, null, cts.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_RejectNonPositivePollInterval(int milliseconds)
    {
        var options = new TerminalOptions { PollInterval = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
