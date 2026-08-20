using System.Globalization;
using HostMcp.Core.Terminal;

namespace HostMcp.Core.Services;

/// <summary>
/// Owns provider selection and the set of currently connected sessions.
/// </summary>
/// <remarks>
/// Provider resolution never throws at construction time. When no emulator is installed the
/// failure surfaces as a <see cref="NoProviderAvailableException"/> on first use, which the tool
/// layer turns into a structured error result instead of crashing the MCP server.
/// </remarks>
public sealed class TerminalSessionManager : IDisposable
{
    private readonly IReadOnlyList<ITerminalProvider> _providers;
    private readonly TerminalOptions _options;
    private readonly Dictionary<string, ITerminalSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="TerminalSessionManager"/> class.</summary>
    /// <param name="providers">Registered providers.</param>
    /// <param name="options">Configuration.</param>
    public TerminalSessionManager(IEnumerable<ITerminalProvider> providers, TerminalOptions options)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _providers = [.. providers.OrderBy(p => p.Priority)];
        _options = options;
    }

    /// <summary>Gets the registered providers in auto-detection order.</summary>
    public IReadOnlyList<ITerminalProvider> Providers => _providers;

    /// <summary>
    /// Resolves the provider to use.
    /// </summary>
    /// <param name="providerOverride">Explicit provider name, or null/"auto" for auto-detection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved provider.</returns>
    /// <exception cref="NoProviderAvailableException">No usable provider was found.</exception>
    public async Task<ITerminalProvider> ResolveProviderAsync(
        string? providerOverride = null,
        CancellationToken cancellationToken = default)
    {
        var requested = string.IsNullOrWhiteSpace(providerOverride) ? _options.Provider : providerOverride;

        if (!string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var named = _providers.FirstOrDefault(p => string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase))
                ?? throw new NoProviderAvailableException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unknown provider '{requested}'. Registered providers: {string.Join(", ", _providers.Select(p => p.Name))}."));

            var availability = await named.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
            return availability.IsAvailable
                ? named
                : throw new NoProviderAvailableException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Provider '{named.Name}' is not usable on this machine: {availability.Reason}"));
        }

        var reasons = new List<string>();
        foreach (var provider in _providers)
        {
            var availability = await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
            if (availability.IsAvailable)
            {
                return provider;
            }

            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"{provider.Name}: {availability.Reason}"));
        }

        throw new NoProviderAvailableException(
            "No host terminal emulator was detected. Install IBM Personal Communications (PCOMM) or another EHLLAPI capable emulator, or set the provider to 'fake' for offline exploration. Probe results: "
            + string.Join("; ", reasons));
    }

    /// <summary>
    /// Connects a session and caches it for subsequent tool calls.
    /// </summary>
    /// <param name="sessionName">Emulator short session name.</param>
    /// <param name="providerOverride">Explicit provider name, or null for the configured default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connected session.</returns>
    public async Task<ITerminalSession> ConnectAsync(
        string sessionName,
        string? providerOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue(sessionName, out var existing) && existing.IsConnected)
            {
                return existing;
            }

            var provider = await ResolveProviderAsync(providerOverride, cancellationToken).ConfigureAwait(false);
            var session = await provider.ConnectAsync(sessionName, cancellationToken).ConfigureAwait(false);
            _sessions[sessionName] = session;
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns a connected session, connecting on demand when it was not opened explicitly.
    /// </summary>
    /// <param name="sessionName">Emulator short session name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connected session.</returns>
    public Task<ITerminalSession> GetOrConnectAsync(string sessionName, CancellationToken cancellationToken = default)
        => ConnectAsync(sessionName, providerOverride: null, cancellationToken);

    /// <summary>
    /// Disconnects and forgets a session.
    /// </summary>
    /// <param name="sessionName">Emulator short session name.</param>
    /// <returns>True when a session was disconnected, false when none was open.</returns>
    public bool Disconnect(string sessionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        _gate.Wait();
        try
        {
            if (!_sessions.Remove(sessionName, out var session))
            {
                return false;
            }

            session.Dispose();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var session in _sessions.Values)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
                // Disposal of a dead emulator connection must never mask the real shutdown reason.
            }
        }

        _sessions.Clear();
        _gate.Dispose();
    }
}
