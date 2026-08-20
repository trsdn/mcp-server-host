namespace HostMcp.Core.Terminal;

/// <summary>Direction of an IND$FILE transfer.</summary>
public enum FileTransferDirection
{
    /// <summary>Workstation to host (IND$FILE PUT).</summary>
    Send,

    /// <summary>Host to workstation (IND$FILE GET).</summary>
    Receive,
}

/// <summary>
/// A request to transfer a file between the workstation and the host with IND$FILE.
/// </summary>
/// <param name="Direction">Transfer direction.</param>
/// <param name="LocalPath">Absolute workstation path.</param>
/// <param name="HostFile">Host dataset or member name, for example <c>USER.DATA(MEMBER)</c>.</param>
/// <param name="Options">Emulator-specific transfer options, for example <c>ASCII CRLF</c>.</param>
/// <param name="Timeout">Maximum time to wait for the transfer to complete.</param>
public sealed record FileTransferRequest(
    FileTransferDirection Direction,
    string LocalPath,
    string HostFile,
    string? Options = null,
    TimeSpan? Timeout = null);

/// <summary>
/// The outcome of an IND$FILE transfer.
/// </summary>
/// <param name="Success">True when the transfer completed successfully.</param>
/// <param name="BytesTransferred">Number of bytes transferred, when reported by the emulator.</param>
/// <param name="Message">Emulator status or error message.</param>
public sealed record FileTransferResult(bool Success, long BytesTransferred = 0, string? Message = null);
