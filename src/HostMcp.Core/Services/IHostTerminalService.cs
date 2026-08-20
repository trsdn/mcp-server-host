using System.ComponentModel;
using HostMcp.Core.Attributes;

namespace HostMcp.Core.Services;

/// <summary>
/// The complete host terminal automation surface. Every method becomes exactly one MCP tool
/// through the <c>HostMcp.Generators.Mcp</c> source generator, and the CLI binds to the same
/// contract, so the tool list can never drift from the implementation.
/// </summary>
[McpTool("HostTerminalTools", "HostMcp.Core.Services.ToolServices.HostTerminal")]
public interface IHostTerminalService
{
    /// <summary>Lists the terminal sessions the configured emulator exposes.</summary>
    /// <param name="provider">Provider override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The discovered sessions.</returns>
    [Description("Lists host terminal sessions (3270/5250) exposed by the detected emulator. Call this first to learn which session short names ('A', 'B', ...) exist.")]
    Task<ListSessionsResult> ListSessionsAsync(
        [Description("Optional provider override: 'auto' (default), 'pcomm', 'ehllapi' or 'fake'.")] string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Connects to a terminal session.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="provider">Provider override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection details including the presentation space size.</returns>
    [Description("Connects to a host terminal session by its emulator short name and keeps it open for subsequent tools.")]
    Task<ConnectResult> ConnectSessionAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Optional provider override: 'auto' (default), 'pcomm', 'ehllapi' or 'fake'.")] string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Disconnects a terminal session.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    [Description("Releases a previously connected host terminal session. Does not log the user off the host.")]
    Task<ToolResult> DisconnectSessionAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the full structured screen.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The structured screen snapshot.</returns>
    [Description("Reads the full screen: dimensions, plain text, field list with positions/lengths/attributes, cursor position and OIA status. Hidden (password) fields are returned with empty text.")]
    Task<ScreenResult> GetScreenAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a single field by index or by position.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="fieldIndex">Zero-based field index.</param>
    /// <param name="row">1-based row inside the field.</param>
    /// <param name="column">1-based column inside the field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The field.</returns>
    [Description("Reads one field, addressed either by its zero-based index from get_screen or by any row/column inside it.")]
    Task<FieldResult> ReadFieldAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Zero-based field index from get_screen. Omit when using row/column.")] int? fieldIndex = null,
        [Description("1-based row inside the target field. Used when fieldIndex is omitted.")] int? row = null,
        [Description("1-based column inside the target field. Used when fieldIndex is omitted.")] int? column = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes into a single field.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="text">Text to write.</param>
    /// <param name="fieldIndex">Zero-based field index.</param>
    /// <param name="row">1-based row inside the field.</param>
    /// <param name="column">1-based column inside the field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The field after the write.</returns>
    [Description("Writes text into one unprotected field, addressed by index or by a row/column inside it. The text is truncated to the field length. Does not submit the screen; use send_keys with an AID mnemonic for that.")]
    Task<FieldResult> WriteFieldAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Text to place in the field.")] string text,
        [Description("Zero-based field index from get_screen. Omit when using row/column.")] int? fieldIndex = null,
        [Description("1-based row inside the target field. Used when fieldIndex is omitted.")] int? row = null,
        [Description("1-based column inside the target field. Used when fieldIndex is omitted.")] int? column = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads text at a coordinate.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="length">Number of characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text read.</returns>
    [Description("Reads a fixed number of characters from a 1-based row/column coordinate, ignoring field boundaries.")]
    Task<TextResult> GetTextAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("1-based row.")] int row,
        [Description("1-based column.")] int column,
        [Description("Number of characters to read.")] int length,
        CancellationToken cancellationToken = default);

    /// <summary>Writes text at a coordinate.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="text">Text to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    [Description("Writes text at a 1-based row/column coordinate. Fails when the target position is protected. Does not submit the screen.")]
    Task<TextResult> SetTextAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("1-based row.")] int row,
        [Description("1-based column.")] int column,
        [Description("Text to write.")] string text,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a keystroke sequence and waits for the host.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="keys">Keystroke string.</param>
    /// <param name="waitForReady">Whether to wait for the keyboard to unlock.</param>
    /// <param name="timeoutSeconds">Wait timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The screen after the host finished processing.</returns>
    [Description("Sends keystrokes and, by default, waits until the host releases the keyboard, then returns the resulting screen. Syntax: literal text mixed with mnemonics in brackets, for example 'USER01[tab]SECRET[enter]'. Supported mnemonics include [enter], [clear], [tab], [backtab], [home], [pf1]-[pf24], [pa1]-[pa3], [attn], [sysreq], [eraseeof], [reset] and the arrow keys.")]
    Task<ScreenResult> SendKeysAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Keystroke sequence, for example 'USER01[tab]SECRET[enter]'. Write '[[' for a literal bracket.")] string keys,
        [Description("Wait for the OIA keyboard lock to clear before returning. Default true; only set false for keystrokes that never reach the host.")] bool waitForReady = true,
        [Description("Timeout in seconds for the readiness wait. Defaults to the configured ready timeout (30s).")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>Waits until the keyboard unlocks.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="timeoutSeconds">Wait timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wait outcome.</returns>
    [Description("Waits until the OIA reports the keyboard is no longer inhibited, meaning the host has finished the current transaction.")]
    Task<WaitResult> WaitForReadyAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Timeout in seconds. Defaults to the configured ready timeout (30s).")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>Waits until a text appears.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="text">Text to wait for.</param>
    /// <param name="row">Optional 1-based row.</param>
    /// <param name="column">Optional 1-based column.</param>
    /// <param name="ignoreCase">Whether to compare case-insensitively.</param>
    /// <param name="timeoutSeconds">Wait timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The screen that satisfied the condition.</returns>
    [Description("Waits until a text appears on the screen, either anywhere or at an exact 1-based row/column. Use this after send_keys when the host redraws in multiple steps.")]
    Task<ScreenResult> WaitForTextAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Text to wait for.")] string text,
        [Description("Optional 1-based row. When set together with column, the text must appear exactly at that position.")] int? row = null,
        [Description("Optional 1-based column. When set together with row, the text must appear exactly at that position.")] int? column = null,
        [Description("Compare case-insensitively. Default false.")] bool ignoreCase = false,
        [Description("Timeout in seconds. Defaults to 30s.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>Searches the presentation space.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="text">Text to search for.</param>
    /// <param name="ignoreCase">Whether to compare case-insensitively.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The position of the first match, when found.</returns>
    [Description("Searches the current screen for a text and returns the 1-based position of the first match. Does not wait.")]
    Task<SearchResult> SearchTextAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Text to search for.")] string text,
        [Description("Compare case-insensitively. Default false.")] bool ignoreCase = false,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the cursor position.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 1-based cursor position.</returns>
    [Description("Returns the 1-based cursor position of the session.")]
    Task<CursorResult> GetCursorAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        CancellationToken cancellationToken = default);

    /// <summary>Moves the cursor.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new cursor position.</returns>
    [Description("Moves the cursor to a 1-based row/column coordinate.")]
    Task<CursorResult> SetCursorAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("1-based row.")] int row,
        [Description("1-based column.")] int column,
        CancellationToken cancellationToken = default);

    /// <summary>Transfers a file with IND$FILE.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="direction">Transfer direction.</param>
    /// <param name="localPath">Workstation path.</param>
    /// <param name="hostFile">Host dataset name.</param>
    /// <param name="options">Emulator specific transfer options.</param>
    /// <param name="timeoutSeconds">Transfer timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transfer outcome.</returns>
    [Description("Transfers a file between the workstation and the host with IND$FILE. NOT IMPLEMENTED YET: the tool validates its arguments and returns a structured 'not implemented' error. See docs/TOOLS.md.")]
    Task<TransferResult> TransferFileAsync(
        [Description("Emulator short session name, for example 'A'.")] string session,
        [Description("Transfer direction: 'send' (workstation to host) or 'receive' (host to workstation).")] string direction,
        [Description("Absolute workstation file path.")] string localPath,
        [Description("Host dataset or member name, for example 'USER.DATA(MEMBER)'.")] string hostFile,
        [Description("Emulator specific transfer options, for example 'ASCII CRLF'.")] string? options = null,
        [Description("Transfer timeout in seconds. Defaults to 300s.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>Executes several steps sequentially.</summary>
    /// <param name="steps">JSON array of steps.</param>
    /// <param name="stopOnError">Whether to stop at the first failing step.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-step outcomes.</returns>
    [Description("Executes several host terminal tools sequentially in one round trip, waiting for host readiness between steps. 'steps' is a JSON array such as [{\"tool\":\"send_keys\",\"session\":\"A\",\"keys\":\"LOGON[enter]\"},{\"tool\":\"wait_for_text\",\"session\":\"A\",\"text\":\"PASSWORD\"},{\"tool\":\"get_screen\",\"session\":\"A\"}]. Each object needs a 'tool' property; the remaining properties are that tool's arguments.")]
    Task<BatchResult> BatchAsync(
        [Description("JSON array of step objects, each with a 'tool' property plus that tool's arguments.")] string steps,
        [Description("Stop at the first failing step. Default true.")] bool stopOnError = true,
        CancellationToken cancellationToken = default);
}
