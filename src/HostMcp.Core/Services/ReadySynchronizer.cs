using System.Diagnostics;
using System.Globalization;
using HostMcp.Core.Models;
using HostMcp.Core.Terminal;

namespace HostMcp.Core.Services;

/// <summary>
/// Abstraction over time so synchronization can be unit tested without real waiting.
/// </summary>
public interface IDelayProvider
{
    /// <summary>Waits for the given duration.</summary>
    /// <param name="delay">Duration to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes after the delay.</returns>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Default <see cref="IDelayProvider"/> backed by <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
public sealed class RealDelayProvider : IDelayProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static RealDelayProvider Instance { get; } = new();

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
}

/// <summary>
/// Central host synchronization.
/// </summary>
/// <remarks>
/// <para>
/// Terminal automation fails almost exclusively because of missing synchronization: the client
/// sends an AID key and reads the screen before the host has finished redrawing it. This class is
/// the single place that solves that problem. Every write path in
/// <see cref="HostTerminalService"/> routes through <see cref="WaitForReadyAsync"/>, so no tool
/// has to remember to wait.
/// </para>
/// <para>
/// The wait polls the Operator Information Area (OIA). The keyboard is locked
/// (<see cref="OiaStatus.InputInhibited"/>) while the host is processing; when it unlocks, an
/// optional settle delay covers hosts that unlock marginally before the final screen write lands.
/// </para>
/// </remarks>
public sealed class ReadySynchronizer
{
    private readonly TerminalOptions _options;
    private readonly IDelayProvider _delay;

    /// <summary>Initializes a new instance of the <see cref="ReadySynchronizer"/> class.</summary>
    /// <param name="options">Timing configuration.</param>
    /// <param name="delay">Delay provider; defaults to real time.</param>
    public ReadySynchronizer(TerminalOptions options, IDelayProvider? delay = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _delay = delay ?? RealDelayProvider.Instance;
    }

    /// <summary>
    /// Waits until the session's keyboard is unlocked.
    /// </summary>
    /// <param name="session">Session to poll.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OIA status observed once the keyboard unlocked.</returns>
    /// <exception cref="TerminalTimeoutException">The keyboard stayed locked for the whole timeout.</exception>
    public async Task<OiaStatus> WaitForReadyAsync(
        ITerminalSession session,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var limit = timeout ?? _options.ReadyTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var oia = await session.GetOiaAsync(cancellationToken).ConfigureAwait(false);
            if (!oia.InputInhibited)
            {
                await _delay.DelayAsync(_options.SettleDelay, cancellationToken).ConfigureAwait(false);
                return oia;
            }

            if (stopwatch.Elapsed >= limit)
            {
                throw new TerminalTimeoutException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Session '{session.SessionName}' kept the keyboard inhibited for {limit.TotalSeconds:0.##}s. The host is still processing or the screen requires operator intervention (OIA: {oia.StatusText ?? "input inhibited"})."));
            }

            await _delay.DelayAsync(_options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until a predicate over the screen becomes true.
    /// </summary>
    /// <param name="session">Session to poll.</param>
    /// <param name="predicate">Predicate evaluated against each screen snapshot.</param>
    /// <param name="description">Description of the condition, used in the timeout message.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The screen snapshot that satisfied the predicate.</returns>
    /// <exception cref="TerminalTimeoutException">The predicate did not become true within the timeout.</exception>
    public async Task<ScreenModel> WaitForScreenAsync(
        ITerminalSession session,
        Func<ScreenModel, bool> predicate,
        string description,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(predicate);

        var limit = timeout ?? _options.WaitForTextTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var screen = await session.GetScreenAsync(cancellationToken).ConfigureAwait(false);
            if (!screen.Oia.InputInhibited && predicate(screen))
            {
                return screen;
            }

            if (stopwatch.Elapsed >= limit)
            {
                throw new TerminalTimeoutException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Timed out after {limit.TotalSeconds:0.##}s waiting for {description} on session '{session.SessionName}'."));
            }

            await _delay.DelayAsync(_options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
