namespace HostMcp.Core.Services;

/// <summary>
/// Configuration for host terminal automation.
/// </summary>
public sealed class TerminalOptions
{
    /// <summary>
    /// Gets or sets the provider to use. <c>auto</c> probes all registered providers in
    /// priority order; otherwise the value must match a provider name (for example <c>pcomm</c>
    /// or <c>ehllapi</c>).
    /// </summary>
    public string Provider { get; set; } = "auto";

    /// <summary>
    /// Gets or sets how long to wait for the keyboard to unlock after a host interaction.
    /// </summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the interval between OIA polls while waiting for the keyboard to unlock.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets a settle delay applied after the keyboard unlocks. Some hosts unlock the
    /// keyboard fractionally before the final screen write lands.
    /// </summary>
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Gets or sets the default timeout for text-wait operations.
    /// </summary>
    public TimeSpan WaitForTextTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Validates the options and throws when a value is not usable.
    /// </summary>
    /// <exception cref="InvalidOperationException">A timing value is zero or negative.</exception>
    public void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("PollInterval must be greater than zero.");
        }

        if (ReadyTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ReadyTimeout must be greater than zero.");
        }

        if (WaitForTextTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("WaitForTextTimeout must be greater than zero.");
        }

        if (SettleDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("SettleDelay must not be negative.");
        }
    }
}
