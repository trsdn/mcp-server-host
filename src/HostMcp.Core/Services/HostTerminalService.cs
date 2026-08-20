using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HostMcp.Core.Models;
using HostMcp.Core.Terminal;

namespace HostMcp.Core.Services;

/// <summary>
/// Default <see cref="IHostTerminalService"/> implementation.
/// </summary>
/// <remarks>
/// Synchronization is applied here, not in the tools: every method that can change host state
/// funnels through <see cref="ReadySynchronizer"/>, so the "send an AID key, then read the screen
/// too early" failure mode cannot be reintroduced by adding a new tool.
/// </remarks>
public sealed class HostTerminalService : IHostTerminalService
{
    private const int DefaultTransferTimeoutSeconds = 300;

    private readonly TerminalSessionManager _manager;
    private readonly ReadySynchronizer _sync;
    private readonly TerminalOptions _options;

    /// <summary>Initializes a new instance of the <see cref="HostTerminalService"/> class.</summary>
    /// <param name="manager">Session manager.</param>
    /// <param name="options">Configuration.</param>
    /// <param name="synchronizer">Optional synchronizer override (used by tests).</param>
    public HostTerminalService(TerminalSessionManager manager, TerminalOptions options, ReadySynchronizer? synchronizer = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _manager = manager;
        _options = options;
        _sync = synchronizer ?? new ReadySynchronizer(options);
    }

    /// <summary>JSON options used for batch step parsing and result rendering.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc />
    public async Task<ListSessionsResult> ListSessionsAsync(string? provider = null, CancellationToken cancellationToken = default)
    {
        var resolved = await _manager.ResolveProviderAsync(provider, cancellationToken).ConfigureAwait(false);
        var sessions = await resolved.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
        return new ListSessionsResult(resolved.Name, sessions);
    }

    /// <inheritdoc />
    public async Task<ConnectResult> ConnectSessionAsync(string session, string? provider = null, CancellationToken cancellationToken = default)
    {
        var connected = await _manager.ConnectAsync(session, provider, cancellationToken).ConfigureAwait(false);
        return new ConnectResult(connected.SessionName, connected.ProviderName, connected.Rows, connected.Columns);
    }

    /// <inheritdoc />
    public Task<ToolResult> DisconnectSessionAsync(string session, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_manager.Disconnect(session)
            ? ToolResult.Ok
            : new ToolResult(false, string.Create(CultureInfo.InvariantCulture, $"Session '{session}' was not open.")));
    }

    /// <inheritdoc />
    public async Task<ScreenResult> GetScreenAsync(string session, CancellationToken cancellationToken = default)
    {
        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var screen = await terminal.GetScreenAsync(cancellationToken).ConfigureAwait(false);
        return ScreenResult.From(terminal.SessionName, screen);
    }

    /// <inheritdoc />
    public async Task<FieldResult> ReadFieldAsync(
        string session,
        int? fieldIndex = null,
        int? row = null,
        int? column = null,
        CancellationToken cancellationToken = default)
    {
        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var screen = await terminal.GetScreenAsync(cancellationToken).ConfigureAwait(false);
        var field = ResolveField(screen, fieldIndex, row, column);
        return new FieldResult(terminal.SessionName, FieldDto.From(field));
    }

    /// <inheritdoc />
    public async Task<FieldResult> WriteFieldAsync(
        string session,
        string text,
        int? fieldIndex = null,
        int? row = null,
        int? column = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        await _sync.WaitForReadyAsync(terminal, cancellationToken: cancellationToken).ConfigureAwait(false);

        var screen = await terminal.GetScreenAsync(cancellationToken).ConfigureAwait(false);
        var field = ResolveField(screen, fieldIndex, row, column);

        if (field.IsProtected)
        {
            throw new TerminalException(string.Create(
                CultureInfo.InvariantCulture,
                $"Field {field.Index} at row {field.Row}, column {field.Column} is protected and cannot be written."));
        }

        var value = text.Length > field.Length ? text[..field.Length] : text.PadRight(field.Length);
        await terminal.SetTextAsync(field.Row, field.Column, value, cancellationToken).ConfigureAwait(false);

        var after = await terminal.GetScreenAsync(cancellationToken).ConfigureAwait(false);
        var updated = after.Fields.FirstOrDefault(f => f.Index == field.Index) ?? field with { Text = value };
        return new FieldResult(terminal.SessionName, FieldDto.From(updated));
    }

    /// <inheritdoc />
    public async Task<TextResult> GetTextAsync(string session, int row, int column, int length, CancellationToken cancellationToken = default)
    {
        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var text = await terminal.GetTextAsync(row, column, length, cancellationToken).ConfigureAwait(false);
        return new TextResult(terminal.SessionName, row, column, text);
    }

    /// <inheritdoc />
    public async Task<TextResult> SetTextAsync(string session, int row, int column, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        await _sync.WaitForReadyAsync(terminal, cancellationToken: cancellationToken).ConfigureAwait(false);
        await terminal.SetTextAsync(row, column, text, cancellationToken).ConfigureAwait(false);
        return new TextResult(terminal.SessionName, row, column, text);
    }

    /// <inheritdoc />
    public async Task<ScreenResult> SendKeysAsync(
        string session,
        string keys,
        bool waitForReady = true,
        int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var parsed = MnemonicParser.Parse(keys);

        // Never type into a locked keyboard: the host discards those keystrokes silently.
        await _sync.WaitForReadyAsync(terminal, ToTimeout(timeoutSeconds), cancellationToken).ConfigureAwait(false);
        await terminal.SendKeysAsync(parsed, cancellationToken).ConfigureAwait(false);

        if (waitForReady)
        {
            await _sync.WaitForReadyAsync(terminal, ToTimeout(timeoutSeconds), cancellationToken).ConfigureAwait(false);
        }

        var screen = await terminal.GetScreenAsync(cancellationToken).ConfigureAwait(false);
        return ScreenResult.From(terminal.SessionName, screen);
    }

    /// <inheritdoc />
    public async Task<WaitResult> WaitForReadyAsync(string session, int? timeoutSeconds = null, CancellationToken cancellationToken = default)
    {
        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        var oia = await _sync.WaitForReadyAsync(terminal, ToTimeout(timeoutSeconds), cancellationToken).ConfigureAwait(false);
        return new WaitResult(terminal.SessionName, stopwatch.ElapsedMilliseconds, OiaDto.From(oia));
    }

    /// <inheritdoc />
    public async Task<ScreenResult> WaitForTextAsync(
        string session,
        string text,
        int? row = null,
        int? column = null,
        bool ignoreCase = false,
        int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var exact = row.HasValue && column.HasValue;

        var description = exact
            ? string.Create(CultureInfo.InvariantCulture, $"'{text}' at row {row}, column {column}")
            : string.Create(CultureInfo.InvariantCulture, $"'{text}' anywhere on the screen");

        var screen = await _sync.WaitForScreenAsync(
            terminal,
            s => exact
                ? IsTextAt(s, row!.Value, column!.Value, text, comparison)
                : s.Buffer.Contains(text, comparison),
            description,
            timeoutSeconds.HasValue ? TimeSpan.FromSeconds(timeoutSeconds.Value) : _options.WaitForTextTimeout,
            cancellationToken).ConfigureAwait(false);

        return ScreenResult.From(terminal.SessionName, screen);
    }

    /// <inheritdoc />
    public async Task<SearchResult> SearchTextAsync(string session, string text, bool ignoreCase = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var screen = await terminal.GetScreenAsync(cancellationToken).ConfigureAwait(false);
        var match = screen.Find(text, ignoreCase);

        return match is null
            ? new SearchResult(terminal.SessionName, false)
            : new SearchResult(terminal.SessionName, true, match.Row, match.Column);
    }

    /// <inheritdoc />
    public async Task<CursorResult> GetCursorAsync(string session, CancellationToken cancellationToken = default)
    {
        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        var cursor = await terminal.GetCursorAsync(cancellationToken).ConfigureAwait(false);
        return new CursorResult(terminal.SessionName, cursor.Row, cursor.Column);
    }

    /// <inheritdoc />
    public async Task<CursorResult> SetCursorAsync(string session, int row, int column, CancellationToken cancellationToken = default)
    {
        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        await _sync.WaitForReadyAsync(terminal, cancellationToken: cancellationToken).ConfigureAwait(false);
        await terminal.SetCursorAsync(row, column, cancellationToken).ConfigureAwait(false);
        return new CursorResult(terminal.SessionName, row, column);
    }

    /// <inheritdoc />
    public async Task<TransferResult> TransferFileAsync(
        string session,
        string direction,
        string localPath,
        string hostFile,
        string? options = null,
        int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(direction);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostFile);

        var parsedDirection = direction.ToLowerInvariant() switch
        {
            "send" or "put" or "upload" => FileTransferDirection.Send,
            "receive" or "get" or "download" => FileTransferDirection.Receive,
            _ => throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Unknown transfer direction '{direction}'. Use 'send' or 'receive'."),
                nameof(direction)),
        };

        if (!Path.IsPathFullyQualified(localPath))
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"'{localPath}' is not an absolute Windows path."),
                nameof(localPath));
        }

        var terminal = await _manager.GetOrConnectAsync(session, cancellationToken).ConfigureAwait(false);
        await _sync.WaitForReadyAsync(terminal, cancellationToken: cancellationToken).ConfigureAwait(false);

        var request = new FileTransferRequest(
            parsedDirection,
            localPath,
            hostFile,
            options,
            TimeSpan.FromSeconds(timeoutSeconds ?? DefaultTransferTimeoutSeconds));

        var result = await terminal.TransferFileAsync(request, cancellationToken).ConfigureAwait(false);
        return new TransferResult(
            terminal.SessionName,
            result.BytesTransferred,
            result.Message,
            result.Success,
            result.Success ? null : result.Message ?? "File transfer failed.");
    }

    /// <inheritdoc />
    public async Task<BatchResult> BatchAsync(string steps, bool stopOnError = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steps);

        List<Dictionary<string, JsonElement>>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(steps, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"'steps' is not a valid JSON array of step objects: {ex.Message}", nameof(steps), ex);
        }

        if (parsed is null || parsed.Count == 0)
        {
            throw new ArgumentException("'steps' must be a non-empty JSON array of step objects.", nameof(steps));
        }

        var results = new List<BatchStepResult>(parsed.Count);
        var overallSuccess = true;
        string? firstError = null;

        for (var i = 0; i < parsed.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = parsed[i];
            var tool = GetString(step, "tool") ?? string.Empty;

            try
            {
                var result = await ExecuteStepAsync(tool, step, cancellationToken).ConfigureAwait(false);
                results.Add(new BatchStepResult(i, tool, result.Success, result, result.ErrorMessage));

                if (!result.Success)
                {
                    overallSuccess = false;
                    firstError ??= result.ErrorMessage;
                    if (stopOnError)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                overallSuccess = false;
                firstError ??= string.Create(CultureInfo.InvariantCulture, $"Step {i} ({tool}) failed: {ex.Message}");
                results.Add(new BatchStepResult(i, tool, false, null, ex.Message));
                if (stopOnError)
                {
                    break;
                }
            }
        }

        return new BatchResult(results, results.Count, overallSuccess, firstError);
    }

    private async Task<ToolResult> ExecuteStepAsync(string tool, Dictionary<string, JsonElement> step, CancellationToken cancellationToken)
    {
        var session = GetString(step, "session") ?? string.Empty;

        return tool.ToLowerInvariant() switch
        {
            "list_sessions" => await ListSessionsAsync(GetString(step, "provider"), cancellationToken).ConfigureAwait(false),
            "connect_session" => await ConnectSessionAsync(session, GetString(step, "provider"), cancellationToken).ConfigureAwait(false),
            "disconnect_session" => await DisconnectSessionAsync(session, cancellationToken).ConfigureAwait(false),
            "get_screen" => await GetScreenAsync(session, cancellationToken).ConfigureAwait(false),
            "read_field" => await ReadFieldAsync(session, GetInt(step, "field_index"), GetInt(step, "row"), GetInt(step, "column"), cancellationToken).ConfigureAwait(false),
            "write_field" => await WriteFieldAsync(session, GetString(step, "text") ?? string.Empty, GetInt(step, "field_index"), GetInt(step, "row"), GetInt(step, "column"), cancellationToken).ConfigureAwait(false),
            "get_text" => await GetTextAsync(session, GetInt(step, "row") ?? 1, GetInt(step, "column") ?? 1, GetInt(step, "length") ?? 0, cancellationToken).ConfigureAwait(false),
            "set_text" => await SetTextAsync(session, GetInt(step, "row") ?? 1, GetInt(step, "column") ?? 1, GetString(step, "text") ?? string.Empty, cancellationToken).ConfigureAwait(false),
            "send_keys" => await SendKeysAsync(session, GetString(step, "keys") ?? string.Empty, GetBool(step, "wait_for_ready") ?? true, GetInt(step, "timeout_seconds"), cancellationToken).ConfigureAwait(false),
            "wait_for_ready" => await WaitForReadyAsync(session, GetInt(step, "timeout_seconds"), cancellationToken).ConfigureAwait(false),
            "wait_for_text" => await WaitForTextAsync(session, GetString(step, "text") ?? string.Empty, GetInt(step, "row"), GetInt(step, "column"), GetBool(step, "ignore_case") ?? false, GetInt(step, "timeout_seconds"), cancellationToken).ConfigureAwait(false),
            "search_text" => await SearchTextAsync(session, GetString(step, "text") ?? string.Empty, GetBool(step, "ignore_case") ?? false, cancellationToken).ConfigureAwait(false),
            "get_cursor" => await GetCursorAsync(session, cancellationToken).ConfigureAwait(false),
            "set_cursor" => await SetCursorAsync(session, GetInt(step, "row") ?? 1, GetInt(step, "column") ?? 1, cancellationToken).ConfigureAwait(false),
            "transfer_file" => await TransferFileAsync(session, GetString(step, "direction") ?? string.Empty, GetString(step, "local_path") ?? string.Empty, GetString(step, "host_file") ?? string.Empty, GetString(step, "options"), GetInt(step, "timeout_seconds"), cancellationToken).ConfigureAwait(false),
            "batch" => throw new ArgumentException("Nested batch steps are not supported.", nameof(step)),
            _ => throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Unknown tool '{tool}' in batch step. Every step needs a 'tool' property naming a host terminal tool."),
                nameof(step)),
        };
    }

    private static bool IsTextAt(ScreenModel screen, int row, int column, string text, StringComparison comparison)
    {
        var offset = CoordinateMapper.ToOffset(row, column, screen.Rows, screen.Columns);
        if (offset + text.Length > screen.Buffer.Length)
        {
            return false;
        }

        return screen.Buffer.AsSpan(offset, text.Length).Equals(text.AsSpan(), comparison);
    }

    private static ScreenField ResolveField(ScreenModel screen, int? fieldIndex, int? row, int? column)
    {
        if (fieldIndex.HasValue)
        {
            return screen.Fields.FirstOrDefault(f => f.Index == fieldIndex.Value)
                ?? throw new TerminalException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"No field with index {fieldIndex.Value}. The screen exposes {screen.Fields.Count} fields."));
        }

        if (row.HasValue && column.HasValue)
        {
            return screen.FieldAt(row.Value, column.Value)
                ?? throw new TerminalException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"No field covers row {row.Value}, column {column.Value}."));
        }

        throw new ArgumentException("Specify either 'fieldIndex' or both 'row' and 'column'.", nameof(fieldIndex));
    }

    private TimeSpan ToTimeout(int? timeoutSeconds)
        => timeoutSeconds.HasValue ? TimeSpan.FromSeconds(timeoutSeconds.Value) : _options.ReadyTimeout;

    private static string? GetString(Dictionary<string, JsonElement> step, string key)
        => step.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(Dictionary<string, JsonElement> step, string key)
        => step.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static bool? GetBool(Dictionary<string, JsonElement> step, string key)
        => step.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
