namespace HostMcp.Core.Models;

/// <summary>
/// Operator Information Area (OIA) status of a terminal session.
/// The OIA is the definitive source for "is the host ready for input?".
/// </summary>
/// <param name="InputInhibited">
/// True while the keyboard is locked because the host is still processing the last transaction.
/// All write operations must wait until this is false.
/// </param>
/// <param name="Alarm">True when the host raised the audible alarm with the last screen write.</param>
/// <param name="CommunicationError">True when the OIA signals a communication check condition.</param>
/// <param name="StatusText">Raw OIA status text as reported by the emulator, when available.</param>
public sealed record OiaStatus(
    bool InputInhibited,
    bool Alarm = false,
    bool CommunicationError = false,
    string? StatusText = null)
{
    /// <summary>An OIA status representing a ready, unlocked keyboard.</summary>
    public static OiaStatus Ready { get; } = new(InputInhibited: false);

    /// <summary>An OIA status representing a locked keyboard (host still working).</summary>
    public static OiaStatus Inhibited { get; } = new(InputInhibited: true);
}
