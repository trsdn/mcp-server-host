using System.Globalization;
using HostMcp.Core.Services;

namespace HostMcp.ComInterop;

/// <summary>
/// Composition root shared by the MCP server and the CLI.
/// </summary>
public static class HostTerminalFactory
{
    /// <summary>Environment variable selecting the provider (<c>auto</c>, <c>pcomm</c>, <c>ehllapi</c>, <c>fake</c>).</summary>
    public const string ProviderVariable = "HOSTMCP_PROVIDER";

    /// <summary>Environment variable overriding the ready timeout in seconds.</summary>
    public const string ReadyTimeoutVariable = "HOSTMCP_READY_TIMEOUT_SECONDS";

    /// <summary>Environment variable overriding the OIA poll interval in milliseconds.</summary>
    public const string PollIntervalVariable = "HOSTMCP_POLL_INTERVAL_MS";

    /// <summary>Environment variable overriding the assumed EHLLAPI presentation space rows.</summary>
    public const string RowsVariable = "HOSTMCP_ROWS";

    /// <summary>Environment variable overriding the assumed EHLLAPI presentation space columns.</summary>
    public const string ColumnsVariable = "HOSTMCP_COLUMNS";

    /// <summary>
    /// Reads <see cref="TerminalOptions"/> from environment variables, falling back to defaults.
    /// </summary>
    /// <returns>The configured options.</returns>
    public static TerminalOptions CreateOptions()
    {
        var options = new TerminalOptions();

        var provider = Environment.GetEnvironmentVariable(ProviderVariable);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            options.Provider = provider.Trim();
        }

        if (TryReadInt(ReadyTimeoutVariable, out var readySeconds) && readySeconds > 0)
        {
            options.ReadyTimeout = TimeSpan.FromSeconds(readySeconds);
            options.WaitForTextTimeout = TimeSpan.FromSeconds(readySeconds);
        }

        if (TryReadInt(PollIntervalVariable, out var pollMs) && pollMs > 0)
        {
            options.PollInterval = TimeSpan.FromMilliseconds(pollMs);
        }

        options.Validate();
        return options;
    }

    /// <summary>
    /// Creates the fully wired terminal service.
    /// </summary>
    /// <param name="options">Options, or null to read them from the environment.</param>
    /// <returns>The service and the session manager that owns the emulator connections.</returns>
    public static (IHostTerminalService Service, TerminalSessionManager Manager) Create(TerminalOptions? options = null)
    {
        var effective = options ?? CreateOptions();

        var rows = TryReadInt(RowsVariable, out var r) && r > 0 ? r : 24;
        var columns = TryReadInt(ColumnsVariable, out var c) && c > 0 ? c : 80;

        // The fake provider is only registered when explicitly requested, so auto-detection never
        // silently succeeds against an in-memory emulator on a machine that should use a real one.
        var includeFake = string.Equals(effective.Provider, "fake", StringComparison.OrdinalIgnoreCase);

        var manager = new TerminalSessionManager(
            TerminalProviderFactory.CreateDefault(includeFake, rows, columns),
            effective);

        return (new HostTerminalService(manager, effective), manager);
    }

    private static bool TryReadInt(string variable, out int value)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
