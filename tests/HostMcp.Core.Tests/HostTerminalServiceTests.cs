using HostMcp.Core.Models;
using HostMcp.Core.Providers;
using HostMcp.Core.Services;
using HostMcp.Core.Terminal;
using Xunit;

namespace HostMcp.Core.Tests;

/// <summary>
/// End-to-end tests of the tool surface against the in-memory provider. They deliberately use no
/// emulator so they run identically on a developer machine and in CI.
/// </summary>
public class HostTerminalServiceTests
{
    private static (IHostTerminalService Service, TerminalSessionManager Manager, FakeTerminalProvider Provider) Create()
    {
        var provider = new FakeTerminalProvider("A", "B");
        var options = new TerminalOptions
        {
            Provider = "fake",
            PollInterval = TimeSpan.FromMilliseconds(1),
            SettleDelay = TimeSpan.Zero,
            ReadyTimeout = TimeSpan.FromSeconds(5),
            WaitForTextTimeout = TimeSpan.FromSeconds(5),
        };

        var manager = new TerminalSessionManager([provider], options);
        var sync = new ReadySynchronizer(options, new RecordingDelayProvider());
        return (new HostTerminalService(manager, options, sync), manager, provider);
    }

    private static void ArrangeSignOnScreen(FakeTerminalSession session)
    {
        session.Poke(1, 1, "SIGN ON");
        session.AddField(3, 1, 9, FieldAttributes.Protected, "USERID:");
        session.AddField(3, 11, 8);
        session.AddField(4, 1, 9, FieldAttributes.Protected, "PASSWORD:");
        session.AddField(4, 11, 8, FieldAttributes.Hidden);
    }

    [Fact]
    public async Task ListSessions_ReportsTheProviderSessions()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            var result = await service.ListSessionsAsync();

            Assert.True(result.Success);
            Assert.Equal("fake", result.Provider);
            Assert.Equal(["A", "B"], result.Sessions.Select(s => s.Name).Order());
        }
    }

    [Fact]
    public async Task ConnectSession_ReportsThePresentationSpaceSize()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            var result = await service.ConnectSessionAsync("A");

            Assert.True(result.Success);
            Assert.Equal(24, result.Rows);
            Assert.Equal(80, result.Columns);
        }
    }

    [Fact]
    public async Task DisconnectSession_ReportsFailure_WhenNothingWasOpen()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            var result = await service.DisconnectSessionAsync("A");

            Assert.False(result.Success);
        }
    }

    [Fact]
    public async Task GetScreen_ReturnsFieldsCursorAndOia()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            ArrangeSignOnScreen(provider.Sessions["A"]);

            var screen = await service.GetScreenAsync("A");

            Assert.True(screen.Success);
            Assert.Equal(4, screen.Fields.Count);
            Assert.StartsWith("SIGN ON", screen.Text, StringComparison.Ordinal);
            Assert.Equal(1, screen.CursorRow);
            Assert.False(screen.Oia.InputInhibited);
        }
    }

    [Fact]
    public async Task GetScreen_MasksHiddenFieldContent()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            var session = provider.Sessions["A"];
            session.AddField(1, 1, 8, FieldAttributes.Hidden, "SECRET");

            var screen = await service.GetScreenAsync("A");

            Assert.Equal(string.Empty, screen.Fields[0].Text);
            Assert.True(screen.Fields[0].Hidden);
        }
    }

    [Fact]
    public async Task ReadField_ResolvesByIndexAndByCoordinate()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            ArrangeSignOnScreen(provider.Sessions["A"]);

            var byIndex = await service.ReadFieldAsync("A", fieldIndex: 0);
            var byCoordinate = await service.ReadFieldAsync("A", row: 3, column: 3);

            Assert.Equal("USERID:", byIndex.Field!.Text);
            Assert.Equal(byIndex.Field.Index, byCoordinate.Field!.Index);
        }
    }

    [Fact]
    public async Task ReadField_WithoutAnyAddress_Fails()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            ArrangeSignOnScreen(provider.Sessions["A"]);

            await Assert.ThrowsAsync<ArgumentException>(() => service.ReadFieldAsync("A"));
        }
    }

    [Fact]
    public async Task WriteField_PadsAndTruncatesToTheFieldLength()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            ArrangeSignOnScreen(provider.Sessions["A"]);

            var result = await service.WriteFieldAsync("A", "USER01", fieldIndex: 1);

            Assert.True(result.Success);
            Assert.Equal("USER01", result.Field!.Text);

            var truncated = await service.WriteFieldAsync("A", "VERYLONGUSERNAME", fieldIndex: 1);
            Assert.Equal("VERYLONG", truncated.Field!.Text);
        }
    }

    [Fact]
    public async Task WriteField_RejectsProtectedFields()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            ArrangeSignOnScreen(provider.Sessions["A"]);

            var ex = await Assert.ThrowsAsync<TerminalException>(
                () => service.WriteFieldAsync("A", "X", fieldIndex: 0));

            Assert.Contains("protected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GetTextAndSetText_UseOneBasedCoordinates()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            provider.Sessions["A"].AddField(5, 10, 5);

            await service.SetTextAsync("A", 5, 10, "ABCDE");
            var text = await service.GetTextAsync("A", 5, 10, 5);

            Assert.Equal("ABCDE", text.Text);
            Assert.Equal(5, text.Row);
            Assert.Equal(10, text.Column);
        }
    }

    [Fact]
    public async Task SendKeys_WaitsForTheHostAndReturnsTheNewScreen()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            var session = provider.Sessions["A"];
            ArrangeSignOnScreen(session);
            session.InhibitPollsPerAid = 2;
            session.EnqueueHostResponse(s =>
            {
                s.Clear();
                s.Poke(1, 1, "WELCOME TO THE HOST");
            });

            var screen = await service.SendKeysAsync("A", "[tab]USER01[enter]");

            Assert.True(screen.Success);
            Assert.Contains("WELCOME TO THE HOST", screen.Text, StringComparison.Ordinal);
            Assert.False(screen.Oia.InputInhibited);
            Assert.Equal("[tab]USER01[enter]", session.SentKeys[^1]);
        }
    }

    [Fact]
    public async Task SendKeys_RejectsUnknownMnemonics()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            await Assert.ThrowsAsync<FormatException>(() => service.SendKeysAsync("A", "[nope]"));
        }
    }

    [Fact]
    public async Task WaitForReady_ReportsElapsedTime()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            var result = await service.WaitForReadyAsync("A");

            Assert.True(result.Success);
            Assert.NotNull(result.Oia);
            Assert.False(result.Oia!.InputInhibited);
        }
    }

    [Fact]
    public async Task WaitForText_MatchesAnywhereAndAtAnExactPosition()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            provider.Sessions["A"].Poke(4, 6, "READY");

            var anywhere = await service.WaitForTextAsync("A", "READY");
            var exact = await service.WaitForTextAsync("A", "READY", row: 4, column: 6);

            Assert.True(anywhere.Success);
            Assert.True(exact.Success);
        }
    }

    [Fact]
    public async Task WaitForText_TimesOut_WhenTheTextNeverAppears()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            await Assert.ThrowsAsync<TerminalTimeoutException>(
                () => service.WaitForTextAsync("A", "NEVER", timeoutSeconds: 0));
        }
    }

    [Fact]
    public async Task SearchText_ReturnsTheOneBasedPosition()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            provider.Sessions["A"].Poke(2, 3, "TARGET");

            var found = await service.SearchTextAsync("A", "TARGET");
            var missing = await service.SearchTextAsync("A", "ABSENT");

            Assert.True(found.Found);
            Assert.Equal(2, found.Row);
            Assert.Equal(3, found.Column);
            Assert.False(missing.Found);
        }
    }

    [Fact]
    public async Task GetCursorAndSetCursor_RoundTrip()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            await service.SetCursorAsync("A", 7, 12);
            var cursor = await service.GetCursorAsync("A");

            Assert.Equal(7, cursor.Row);
            Assert.Equal(12, cursor.Column);
        }
    }

    [Fact]
    public async Task SetCursor_RejectsOutOfRangeCoordinates()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetCursorAsync("A", 99, 1));
        }
    }

    [Fact]
    public async Task TransferFile_IsNotImplementedButValidatesItsArguments()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.TransferFileAsync("A", "sideways", "C:\\tmp\\a.txt", "USER.DATA"));

            var result = await service.TransferFileAsync("A", "send", "C:\\tmp\\a.txt", "USER.DATA");

            Assert.False(result.Success);
            Assert.Contains("not implemented", result.ErrorMessage ?? result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Batch_ExecutesStepsInOrder()
    {
        var (service, manager, provider) = Create();
        using (manager)
        {
            var session = provider.Sessions["A"];
            ArrangeSignOnScreen(session);
            session.EnqueueHostResponse(s =>
            {
                s.Clear();
                s.Poke(1, 1, "MAIN MENU");
            });

            const string Steps = """
                [
                  {"tool":"write_field","session":"A","field_index":1,"text":"USER01"},
                  {"tool":"send_keys","session":"A","keys":"[enter]"},
                  {"tool":"wait_for_text","session":"A","text":"MAIN MENU"},
                  {"tool":"get_cursor","session":"A"}
                ]
                """;

            var result = await service.BatchAsync(Steps);

            Assert.True(result.Success);
            Assert.Equal(4, result.StepsExecuted);
            Assert.All(result.Steps, step => Assert.True(step.Success));
            Assert.Equal(["write_field", "send_keys", "wait_for_text", "get_cursor"], result.Steps.Select(s => s.Tool));
        }
    }

    [Fact]
    public async Task Batch_StopsAtTheFirstFailure_ByDefault()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            const string Steps = """
                [
                  {"tool":"get_cursor","session":"A"},
                  {"tool":"send_keys","session":"A","keys":"[nope]"},
                  {"tool":"get_cursor","session":"A"}
                ]
                """;

            var result = await service.BatchAsync(Steps);

            Assert.False(result.Success);
            Assert.Equal(2, result.StepsExecuted);
            Assert.False(result.Steps[1].Success);
        }
    }

    [Fact]
    public async Task Batch_ContinuesPastFailures_WhenStopOnErrorIsFalse()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            const string Steps = """
                [
                  {"tool":"send_keys","session":"A","keys":"[nope]"},
                  {"tool":"get_cursor","session":"A"}
                ]
                """;

            var result = await service.BatchAsync(Steps, stopOnError: false);

            Assert.False(result.Success);
            Assert.Equal(2, result.StepsExecuted);
            Assert.True(result.Steps[1].Success);
        }
    }

    [Fact]
    public async Task Batch_RejectsMalformedInput()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.BatchAsync("not json"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.BatchAsync("[]"));

            // A step object without a 'tool' property is reported as a failing step rather than
            // aborting the whole batch, so the caller sees which step was malformed.
            var result = await service.BatchAsync("""[{"session":"A"}]""");
            Assert.False(result.Success);
            Assert.False(result.Steps[0].Success);
        }
    }

    [Fact]
    public async Task Batch_RejectsUnknownTools()
    {
        var (service, manager, _) = Create();
        using (manager)
        {
            var result = await service.BatchAsync("""[{"tool":"launch_missiles","session":"A"}]""");

            Assert.False(result.Success);
            Assert.Contains("launch_missiles", result.Steps[0].ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
