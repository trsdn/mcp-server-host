using HostMcp.Core.Models;
using HostMcp.Core.Terminal;

namespace HostMcp.Core.Services;

/// <summary>Result envelope shared by all tool responses.</summary>
/// <param name="Success">True when the operation succeeded.</param>
/// <param name="ErrorMessage">Populated when <paramref name="Success"/> is false.</param>
public record ToolResult(bool Success, string? ErrorMessage = null)
{
    /// <summary>A generic successful result.</summary>
    public static ToolResult Ok { get; } = new(true);
}

/// <summary>Response of the session discovery tool.</summary>
/// <param name="Provider">Name of the provider that answered.</param>
/// <param name="Sessions">Discovered sessions.</param>
/// <param name="Success">True when discovery succeeded.</param>
/// <param name="ErrorMessage">Populated when discovery failed.</param>
public sealed record ListSessionsResult(
    string Provider,
    IReadOnlyList<TerminalSessionInfo> Sessions,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>Response after connecting to a session.</summary>
/// <param name="Session">Connected session short name.</param>
/// <param name="Provider">Provider that owns the session.</param>
/// <param name="Rows">Rows of the presentation space.</param>
/// <param name="Columns">Columns of the presentation space.</param>
/// <param name="Success">True when the connection succeeded.</param>
/// <param name="ErrorMessage">Populated when the connection failed.</param>
public sealed record ConnectResult(
    string Session,
    string Provider,
    int Rows,
    int Columns,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>A field as returned to MCP clients.</summary>
/// <param name="Index">Zero-based field index.</param>
/// <param name="Row">1-based row.</param>
/// <param name="Column">1-based column.</param>
/// <param name="Length">Field length.</param>
/// <param name="Protected">True when the field is read-only.</param>
/// <param name="Numeric">True when the field accepts numeric input only.</param>
/// <param name="Hidden">True when the field is non-display.</param>
/// <param name="Text">Field content with trailing blanks removed.</param>
public sealed record FieldDto(
    int Index,
    int Row,
    int Column,
    int Length,
    bool Protected,
    bool Numeric,
    bool Hidden,
    string Text)
{
    /// <summary>Projects a <see cref="ScreenField"/> onto the wire model.</summary>
    /// <param name="field">Source field.</param>
    /// <returns>The wire model.</returns>
    public static FieldDto From(ScreenField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new FieldDto(
            field.Index,
            field.Row,
            field.Column,
            field.Length,
            field.IsProtected,
            field.IsNumeric,
            field.IsHidden,
            field.IsHidden ? string.Empty : field.TrimmedText);
    }
}

/// <summary>Operator Information Area status as returned to MCP clients.</summary>
/// <param name="InputInhibited">True while the keyboard is locked.</param>
/// <param name="Alarm">True when the host raised the alarm.</param>
/// <param name="CommunicationError">True on a communication check.</param>
/// <param name="StatusText">Raw OIA text when available.</param>
public sealed record OiaDto(bool InputInhibited, bool Alarm, bool CommunicationError, string? StatusText)
{
    /// <summary>Projects an <see cref="OiaStatus"/> onto the wire model.</summary>
    /// <param name="oia">Source status.</param>
    /// <returns>The wire model.</returns>
    public static OiaDto From(OiaStatus oia)
    {
        ArgumentNullException.ThrowIfNull(oia);
        return new OiaDto(oia.InputInhibited, oia.Alarm, oia.CommunicationError, oia.StatusText);
    }
}

/// <summary>Full structured screen snapshot returned by <c>get_screen</c>.</summary>
/// <param name="Session">Session short name.</param>
/// <param name="Rows">Rows of the presentation space.</param>
/// <param name="Columns">Columns of the presentation space.</param>
/// <param name="Text">Full presentation space as newline separated text.</param>
/// <param name="Fields">Field list.</param>
/// <param name="CursorRow">1-based cursor row.</param>
/// <param name="CursorColumn">1-based cursor column.</param>
/// <param name="Oia">Operator Information Area status.</param>
/// <param name="Success">True when the read succeeded.</param>
/// <param name="ErrorMessage">Populated when the read failed.</param>
public sealed record ScreenResult(
    string Session,
    int Rows,
    int Columns,
    string Text,
    IReadOnlyList<FieldDto> Fields,
    int CursorRow,
    int CursorColumn,
    OiaDto Oia,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage)
{
    /// <summary>Projects a <see cref="ScreenModel"/> onto the wire model.</summary>
    /// <param name="session">Session short name.</param>
    /// <param name="screen">Source screen.</param>
    /// <returns>The wire model.</returns>
    public static ScreenResult From(string session, ScreenModel screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return new ScreenResult(
            session,
            screen.Rows,
            screen.Columns,
            screen.PlainText,
            [.. screen.Fields.Select(FieldDto.From)],
            screen.Cursor.Row,
            screen.Cursor.Column,
            OiaDto.From(screen.Oia));
    }
}

/// <summary>Response of text read operations.</summary>
/// <param name="Session">Session short name.</param>
/// <param name="Row">1-based row that was read.</param>
/// <param name="Column">1-based column that was read.</param>
/// <param name="Text">Text that was read.</param>
/// <param name="Success">True when the read succeeded.</param>
/// <param name="ErrorMessage">Populated when the read failed.</param>
public sealed record TextResult(
    string Session,
    int Row,
    int Column,
    string Text,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>Response of a field read.</summary>
/// <param name="Session">Session short name.</param>
/// <param name="Field">The field that was read.</param>
/// <param name="Success">True when the read succeeded.</param>
/// <param name="ErrorMessage">Populated when the read failed.</param>
public sealed record FieldResult(
    string Session,
    FieldDto? Field,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>Response of a search over the presentation space.</summary>
/// <param name="Session">Session short name.</param>
/// <param name="Found">True when the text was found.</param>
/// <param name="Row">1-based row of the first match, when found.</param>
/// <param name="Column">1-based column of the first match, when found.</param>
/// <param name="Success">True when the search executed.</param>
/// <param name="ErrorMessage">Populated when the search failed.</param>
public sealed record SearchResult(
    string Session,
    bool Found,
    int? Row = null,
    int? Column = null,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>Response of cursor queries and moves.</summary>
/// <param name="Session">Session short name.</param>
/// <param name="Row">1-based cursor row.</param>
/// <param name="Column">1-based cursor column.</param>
/// <param name="Success">True when the operation succeeded.</param>
/// <param name="ErrorMessage">Populated when the operation failed.</param>
public sealed record CursorResult(
    string Session,
    int Row,
    int Column,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>Response of wait operations.</summary>
/// <param name="Session">Session short name.</param>
/// <param name="ElapsedMilliseconds">Time spent waiting.</param>
/// <param name="Oia">OIA status observed at the end of the wait.</param>
/// <param name="Success">True when the wait condition was met.</param>
/// <param name="ErrorMessage">Populated when the wait timed out.</param>
public sealed record WaitResult(
    string Session,
    long ElapsedMilliseconds,
    OiaDto? Oia = null,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>Response of a file transfer request.</summary>
/// <param name="Session">Session short name.</param>
/// <param name="BytesTransferred">Bytes transferred when reported by the emulator.</param>
/// <param name="Message">Emulator status message.</param>
/// <param name="Success">True when the transfer succeeded.</param>
/// <param name="ErrorMessage">Populated when the transfer failed.</param>
public sealed record TransferResult(
    string Session,
    long BytesTransferred = 0,
    string? Message = null,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);

/// <summary>Outcome of a single step inside a batch.</summary>
/// <param name="Index">Zero-based step index.</param>
/// <param name="Tool">Tool name executed.</param>
/// <param name="Success">True when the step succeeded.</param>
/// <param name="Result">Serialized step result.</param>
/// <param name="ErrorMessage">Populated when the step failed.</param>
public sealed record BatchStepResult(
    int Index,
    string Tool,
    bool Success,
    object? Result = null,
    string? ErrorMessage = null);

/// <summary>Response of the batch tool.</summary>
/// <param name="Steps">Results of the executed steps, in order.</param>
/// <param name="StepsExecuted">Number of steps that were executed.</param>
/// <param name="Success">True when every executed step succeeded.</param>
/// <param name="ErrorMessage">Populated when a step failed.</param>
public sealed record BatchResult(
    IReadOnlyList<BatchStepResult> Steps,
    int StepsExecuted,
    bool Success = true,
    string? ErrorMessage = null) : ToolResult(Success, ErrorMessage);
