using System.Globalization;
using HostMcp.Core.Terminal;

namespace HostMcp.ComInterop.Pcomm;

/// <summary>
/// Primary provider: IBM Personal Communications (PCOMM) via COM/OLE automation.
/// </summary>
/// <remarks>
/// The provider is safe to construct on a machine without PCOMM: availability is only probed in
/// <see cref="IsAvailableAsync"/>, and a missing registration is reported as a clear message rather
/// than an exception at server startup.
/// </remarks>
public sealed class PcommTerminalProvider : ITerminalProvider
{
    /// <summary>The stable provider name.</summary>
    public const string ProviderName = "pcomm";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Description => "IBM Personal Communications (PCOMM) via COM/OLE automation (autECLSession).";

    /// <summary>Gets the auto-detection priority. PCOMM is the richest surface, so it is probed first.</summary>
    public int Priority => 10;

    /// <inheritdoc />
    public Task<ProviderAvailability> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(ProviderAvailability.Unavailable("PCOMM automation requires Windows."));
        }

        using var probe = ComObject.TryCreate(PcommProgIds.ConnectionList);
        if (probe is null)
        {
            return Task.FromResult(ProviderAvailability.Unavailable(string.Create(
                CultureInfo.InvariantCulture,
                $"COM ProgID '{PcommProgIds.ConnectionList}' is not registered for this {(Environment.Is64BitProcess ? "64" : "32")}-bit process. Install IBM Personal Communications, or run the win-x86 build if PCOMM registered its automation objects as 32-bit (see docs/PLATFORM.md).")));
        }

        return Task.FromResult(ProviderAvailability.Available);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TerminalSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var list = ComObject.Create(PcommProgIds.ConnectionList);
        list.Call("Refresh");

        var count = list.GetValue<int>("Count");
        var sessions = new List<TerminalSessionInfo>(count);

        for (var i = 1; i <= count; i++)
        {
            using var item = list.CallObject("Item", i);
            var name = item.GetValue<string>("Name") ?? string.Empty;
            var ready = SafeBool(item, "Ready");
            var started = SafeBool(item, "Started", defaultValue: true);

            sessions.Add(new TerminalSessionInfo(
                name,
                ProviderName,
                ready,
                Description: string.Create(CultureInfo.InvariantCulture, $"PCOMM connection (started: {started}, ready: {ready})")));
        }

        return Task.FromResult<IReadOnlyList<TerminalSessionInfo>>(sessions);
    }

    /// <inheritdoc />
    public Task<ITerminalSession> ConnectAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        cancellationToken.ThrowIfCancellationRequested();

        var session = ComObject.Create(PcommProgIds.Session);
        try
        {
            session.Call("SetConnectionByName", sessionName);
            return Task.FromResult<ITerminalSession>(new PcommTerminalSession(sessionName, session));
        }
        catch (Exception ex)
        {
            session.Dispose();
            throw new TerminalException(string.Create(
                CultureInfo.InvariantCulture,
                $"Could not attach to PCOMM session '{sessionName}': {ex.Message}. Use list_sessions to see the available short names."), ex);
        }
    }

    private static bool SafeBool(ComObject item, string property, bool defaultValue = false)
    {
        try
        {
            return item.GetValue<bool>(property);
        }
        catch (Exception)
        {
            return defaultValue;
        }
    }
}
