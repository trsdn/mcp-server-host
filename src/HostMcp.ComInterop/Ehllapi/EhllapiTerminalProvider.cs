using System.Globalization;
using HostMcp.Core.Terminal;

namespace HostMcp.ComInterop.Ehllapi;

/// <summary>
/// Fallback provider: generic EHLLAPI through the <c>hllapi</c> entry point of
/// <c>pcshll32.dll</c>.
/// </summary>
/// <remarks>
/// EHLLAPI is the lowest common denominator across host emulators. It gives no session
/// enumeration API, so the provider reports the conventional short names A-Z and lets the caller
/// pick one; connecting to a name that does not exist fails with a clear message.
/// </remarks>
public sealed class EhllapiTerminalProvider : ITerminalProvider
{
    /// <summary>The stable provider name.</summary>
    public const string ProviderName = "ehllapi";

    private readonly int _rows;
    private readonly int _columns;

    /// <summary>Initializes a new instance of the <see cref="EhllapiTerminalProvider"/> class.</summary>
    /// <param name="rows">Presentation space rows to assume (EHLLAPI cannot report the model).</param>
    /// <param name="columns">Presentation space columns to assume.</param>
    public EhllapiTerminalProvider(int rows = 24, int columns = 80)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        _rows = rows;
        _columns = columns;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Description => string.Create(
        CultureInfo.InvariantCulture,
        $"Generic EHLLAPI via {EhllapiNative.LibraryName} (hllapi), assuming a {_rows}x{_columns} presentation space.");

    /// <summary>Gets the auto-detection priority. EHLLAPI is probed after PCOMM automation.</summary>
    public int Priority => 20;

    /// <inheritdoc />
    public Task<ProviderAvailability> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(ProviderAvailability.Unavailable("EHLLAPI requires Windows."));
        }

        if (!NativeLibraryProbe.CanLoad(EhllapiNative.LibraryName, out var reason))
        {
            return Task.FromResult(ProviderAvailability.Unavailable(string.Create(
                CultureInfo.InvariantCulture,
                $"{EhllapiNative.LibraryName} could not be loaded into this {(Environment.Is64BitProcess ? "64" : "32")}-bit process: {reason}. {EhllapiNative.LibraryName} is a 32-bit library on most installations; use the win-x86 build (see docs/PLATFORM.md).")));
        }

        return Task.FromResult(ProviderAvailability.Available);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TerminalSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // EHLLAPI has no enumeration function. Report the conventional short name range so the
        // caller sees what connect_session accepts.
        var sessions = new List<TerminalSessionInfo>(26);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            sessions.Add(new TerminalSessionInfo(
                c.ToString(),
                ProviderName,
                Connected: false,
                _rows,
                _columns,
                "EHLLAPI short name (existence is only verified on connect)."));
        }

        return Task.FromResult<IReadOnlyList<TerminalSessionInfo>>(sessions);
    }

    /// <inheritdoc />
    public Task<ITerminalSession> ConnectAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new EhllapiTerminalSession(sessionName.Trim()[..1], _rows, _columns);
        try
        {
            // Force a round trip so an invalid short name fails here rather than on first use.
            session.GetCursorAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult<ITerminalSession>(session);
        }
        catch (Exception)
        {
            session.Dispose();
            throw;
        }
    }
}
