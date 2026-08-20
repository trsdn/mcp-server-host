using HostMcp.Core.Models;
using HostMcp.Core.Providers;
using HostMcp.Core.Services;
using HostMcp.Core.Terminal;
using Xunit;

namespace HostMcp.Core.Tests;

public class FakeTerminalProviderTests
{
    [Fact]
    public async Task Provider_IsAlwaysAvailable_SoTestsAndCiNeedNoEmulator()
    {
        var provider = new FakeTerminalProvider();

        var availability = await provider.IsAvailableAsync();

        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public void Provider_HasTheLowestAutoDetectionPriority()
        => Assert.Equal(int.MaxValue, new FakeTerminalProvider().Priority);

    [Fact]
    public async Task Connect_UnknownSession_ThrowsWithTheAvailableNames()
    {
        var provider = new FakeTerminalProvider("A");

        var ex = await Assert.ThrowsAsync<TerminalException>(() => provider.ConnectAsync("Z"));

        Assert.Contains("A", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_TabMovesBetweenUnprotectedFields()
    {
        using var session = new FakeTerminalSession();
        session.AddField(1, 1, 6, FieldAttributes.Protected, "LABEL:");
        session.AddField(1, 10, 8);
        session.AddField(2, 10, 8);

        await session.SendKeysAsync(MnemonicParser.Parse("[tab]"));
        Assert.Equal(new CursorPosition(1, 10), await session.GetCursorAsync());

        await session.SendKeysAsync(MnemonicParser.Parse("[tab]"));
        Assert.Equal(new CursorPosition(2, 10), await session.GetCursorAsync());

        await session.SendKeysAsync(MnemonicParser.Parse("[tab]"));
        Assert.Equal(new CursorPosition(1, 10), await session.GetCursorAsync());
    }

    [Fact]
    public async Task Session_TypingIntoAProtectedField_Throws()
    {
        using var session = new FakeTerminalSession();
        session.AddField(1, 1, 6, FieldAttributes.Protected, "LABEL:");

        await Assert.ThrowsAsync<TerminalException>(() => session.SendKeysAsync(MnemonicParser.Parse("X")));
    }

    [Fact]
    public async Task Session_AidKeyLocksTheKeyboardForTheConfiguredNumberOfPolls()
    {
        using var session = new FakeTerminalSession { InhibitPollsPerAid = 2 };

        await session.SendKeysAsync(MnemonicParser.Parse("[enter]"));

        Assert.True((await session.GetOiaAsync()).InputInhibited);
        Assert.True((await session.GetOiaAsync()).InputInhibited);
        Assert.False((await session.GetOiaAsync()).InputInhibited);
    }

    [Fact]
    public async Task Session_NonAidKeyDoesNotLockTheKeyboard()
    {
        using var session = new FakeTerminalSession { InhibitPollsPerAid = 5 };

        await session.SendKeysAsync(MnemonicParser.Parse("[home]"));

        Assert.False((await session.GetOiaAsync()).InputInhibited);
    }

    [Fact]
    public async Task Session_ClearWipesTheScreen()
    {
        using var session = new FakeTerminalSession();
        session.Poke(1, 1, "DATA");

        await session.SendKeysAsync(MnemonicParser.Parse("[clear]"));
        var screen = await session.GetScreenAsync();

        Assert.Null(screen.Find("DATA"));
    }

    [Fact]
    public async Task Session_DisposedSessionRejectsFurtherUse()
    {
        var session = new FakeTerminalSession();
        session.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.GetScreenAsync());
    }

    [Fact]
    public async Task SessionManager_ExplicitUnknownProvider_ExplainsWhatIsRegistered()
    {
        using var manager = new TerminalSessionManager([new FakeTerminalProvider()], new TerminalOptions());

        var ex = await Assert.ThrowsAsync<NoProviderAvailableException>(
            () => manager.ResolveProviderAsync("pcomm"));

        Assert.Contains("fake", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionManager_NoProviderAtAll_FailsWithActionableGuidance()
    {
        using var manager = new TerminalSessionManager([new UnavailableProvider()], new TerminalOptions());

        var ex = await Assert.ThrowsAsync<NoProviderAvailableException>(() => manager.ResolveProviderAsync());

        Assert.Contains("No host terminal emulator was detected", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nothing installed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionManager_ReusesAnAlreadyConnectedSession()
    {
        using var manager = new TerminalSessionManager(
            [new FakeTerminalProvider("A")],
            new TerminalOptions { Provider = "fake" });

        var first = await manager.ConnectAsync("A");
        var second = await manager.ConnectAsync("A");

        Assert.Same(first, second);
        Assert.True(manager.Disconnect("A"));
        Assert.False(manager.Disconnect("A"));
    }

    [Fact]
    public void BuildBuffer_ProducesAFlatFixedSizeBuffer()
    {
        var buffer = FakeTerminalProvider.BuildBuffer(2, 5, "AB", "CDEFGHI");

        Assert.Equal(10, buffer.Length);
        Assert.Equal("AB   CDEFG", buffer);
    }

    private sealed class UnavailableProvider : ITerminalProvider
    {
        public string Name => "none";

        public string Description => "Provider that is never available.";

        public int Priority => 1;

        public Task<ProviderAvailability> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ProviderAvailability.Unavailable("nothing installed"));

        public Task<IReadOnlyList<TerminalSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TerminalSessionInfo>>([]);

        public Task<ITerminalSession> ConnectAsync(string sessionName, CancellationToken cancellationToken = default)
            => throw new NoProviderAvailableException("nothing installed");
    }
}
