namespace HostMcp.Core.Terminal;

/// <summary>
/// Descriptor of a terminal session that a provider can connect to.
/// </summary>
/// <param name="Name">Emulator short session name, for example "A".</param>
/// <param name="ProviderName">Name of the provider that reported the session.</param>
/// <param name="Connected">True when the session is currently connected to a host.</param>
/// <param name="Rows">Rows of the presentation space, when known.</param>
/// <param name="Columns">Columns of the presentation space, when known.</param>
/// <param name="Description">Optional emulator-specific description (window title, profile name).</param>
public sealed record TerminalSessionInfo(
    string Name,
    string ProviderName,
    bool Connected,
    int Rows = 0,
    int Columns = 0,
    string? Description = null);

/// <summary>
/// Discovers and opens terminal sessions for one emulator technology.
/// </summary>
/// <remarks>
/// Providers must be constructible without a running emulator. Availability is reported
/// through <see cref="IsAvailableAsync"/> so that a missing emulator produces a clear tool
/// result instead of a startup crash.
/// </remarks>
public interface ITerminalProvider
{
    /// <summary>Gets the stable provider name, for example "pcomm" or "ehllapi".</summary>
    string Name { get; }

    /// <summary>Gets the human readable provider description.</summary>
    string Description { get; }

    /// <summary>
    /// Gets the auto-detection priority. Providers with a lower value are probed first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Probes whether the underlying emulator automation surface is usable on this machine.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An availability result including the reason when unavailable.</returns>
    Task<ProviderAvailability> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the sessions the emulator currently exposes.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The discovered sessions.</returns>
    Task<IReadOnlyList<TerminalSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Connects to a session by its emulator short name.</summary>
    /// <param name="sessionName">Emulator short session name, for example "A".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connected session.</returns>
    Task<ITerminalSession> ConnectAsync(string sessionName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a provider availability probe.
/// </summary>
/// <param name="IsAvailable">True when the provider can be used on this machine.</param>
/// <param name="Reason">Human readable explanation, always populated when unavailable.</param>
public sealed record ProviderAvailability(bool IsAvailable, string? Reason = null)
{
    /// <summary>An available result.</summary>
    public static ProviderAvailability Available { get; } = new(true);

    /// <summary>Creates an unavailable result.</summary>
    /// <param name="reason">Explanation shown to the caller.</param>
    /// <returns>An unavailable result.</returns>
    public static ProviderAvailability Unavailable(string reason) => new(false, reason);
}
