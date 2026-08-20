using HostMcp.Core.Models;

namespace HostMcp.Core.Terminal;

/// <summary>
/// A live connection to one host terminal presentation space (a 3270 or 5250 screen).
/// </summary>
/// <remarks>
/// Implementations are thin adapters over an emulator API. They must NOT implement
/// host synchronization themselves: waiting for the keyboard to unlock is handled centrally
/// by <see cref="HostMcp.Core.Services.ReadySynchronizer"/> so every write path behaves identically.
/// </remarks>
public interface ITerminalSession : IDisposable
{
    /// <summary>Gets the emulator short name of the session (for example "A").</summary>
    string SessionName { get; }

    /// <summary>Gets the provider that created this session.</summary>
    string ProviderName { get; }

    /// <summary>Gets a value indicating whether the session is currently connected.</summary>
    bool IsConnected { get; }

    /// <summary>Gets the number of rows of the presentation space.</summary>
    int Rows { get; }

    /// <summary>Gets the number of columns of the presentation space.</summary>
    int Columns { get; }

    /// <summary>Reads the complete presentation space including fields, cursor and OIA status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured screen snapshot.</returns>
    Task<ScreenModel> GetScreenAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the current Operator Information Area status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OIA status.</returns>
    Task<OiaStatus> GetOiaAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads text from a 1-based coordinate.</summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="length">Number of characters to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text read from the presentation space.</returns>
    Task<string> GetTextAsync(int row, int column, int length, CancellationToken cancellationToken = default);

    /// <summary>Writes text at a 1-based coordinate without submitting the screen.</summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="text">Text to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the text has been placed in the presentation space.</returns>
    Task SetTextAsync(int row, int column, string text, CancellationToken cancellationToken = default);

    /// <summary>Sends a parsed keystroke sequence to the emulator.</summary>
    /// <param name="keystrokes">Parsed keystrokes (literal text and mnemonics).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the keystrokes have been delivered.</returns>
    Task SendKeysAsync(IReadOnlyList<Keystroke> keystrokes, CancellationToken cancellationToken = default);

    /// <summary>Reads the cursor position.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 1-based cursor position.</returns>
    Task<CursorPosition> GetCursorAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves the cursor to a 1-based coordinate.</summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the cursor has been moved.</returns>
    Task SetCursorAsync(int row, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfers a file between the workstation and the host using IND$FILE.
    /// </summary>
    /// <param name="request">The transfer request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    /// <exception cref="NotSupportedException">The provider does not implement file transfer.</exception>
    Task<FileTransferResult> TransferFileAsync(FileTransferRequest request, CancellationToken cancellationToken = default);
}
