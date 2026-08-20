using System.Globalization;
using System.Text;
using HostMcp.Core.Models;
using HostMcp.Core.Terminal;

namespace HostMcp.Core.Providers;

/// <summary>
/// An in-memory terminal session used by tests, by <c>--dry-run</c> style exploration and as a
/// safe default when no emulator is installed.
/// </summary>
/// <remarks>
/// The fake models the essential 3270 semantics that automation depends on: a fixed size
/// presentation space, a field list with protected and hidden fields, a cursor, and an OIA that
/// locks for a configurable number of polls after every AID key. That makes it possible to test
/// the synchronization logic deterministically and without a mainframe.
/// </remarks>
public sealed class FakeTerminalSession : ITerminalSession
{
    private readonly char[] _buffer;
    private readonly List<ScreenField> _fields = [];
    private readonly Queue<Action<FakeTerminalSession>> _hostResponses = new();
    private int _inhibitPollsRemaining;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="FakeTerminalSession"/> class.</summary>
    /// <param name="sessionName">Emulator short session name.</param>
    /// <param name="rows">Rows of the presentation space.</param>
    /// <param name="columns">Columns of the presentation space.</param>
    public FakeTerminalSession(string sessionName = "A", int rows = 24, int columns = 80)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        SessionName = sessionName;
        Rows = rows;
        Columns = columns;
        _buffer = new char[rows * columns];
        Array.Fill(_buffer, ' ');
        Cursor = new CursorPosition(1, 1);
        IsConnected = true;
    }

    /// <inheritdoc />
    public string SessionName { get; }

    /// <inheritdoc />
    public string ProviderName => FakeTerminalProvider.ProviderName;

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public int Rows { get; }

    /// <inheritdoc />
    public int Columns { get; }

    /// <summary>Gets the current cursor position.</summary>
    public CursorPosition Cursor { get; private set; }

    /// <summary>
    /// Gets or sets the number of OIA polls the keyboard stays inhibited after an AID key.
    /// Zero means the host answers instantly.
    /// </summary>
    public int InhibitPollsPerAid { get; set; }

    /// <summary>Gets the keystroke sequences that were sent, in order (for assertions).</summary>
    public IList<string> SentKeys { get; } = [];

    /// <summary>Writes text directly into the presentation space, bypassing field protection.</summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="text">Text to write.</param>
    public void Poke(int row, int column, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var offset = CoordinateMapper.ToOffset(row, column, Rows, Columns);
        for (var i = 0; i < text.Length && offset + i < _buffer.Length; i++)
        {
            _buffer[offset + i] = text[i];
        }
    }

    /// <summary>Defines a field on the fake screen.</summary>
    /// <param name="row">1-based row of the field start.</param>
    /// <param name="column">1-based column of the field start.</param>
    /// <param name="length">Field length in characters.</param>
    /// <param name="attributes">Field attributes.</param>
    /// <param name="text">Initial field content.</param>
    /// <returns>The created field.</returns>
    public ScreenField AddField(
        int row,
        int column,
        int length,
        FieldAttributes attributes = FieldAttributes.None,
        string text = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        CoordinateMapper.ToOffset(row, column, Rows, Columns);

        Poke(row, column, text.PadRight(length).Substring(0, length));
        var field = new ScreenField(_fields.Count, row, column, length, attributes, ReadRaw(row, column, length));
        _fields.Add(field);
        return field;
    }

    /// <summary>
    /// Queues a host reaction that runs the next time an AID key is sent, allowing tests to
    /// simulate screen transitions.
    /// </summary>
    /// <param name="response">Action applied to the session when the next AID key is sent.</param>
    public void EnqueueHostResponse(Action<FakeTerminalSession> response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _hostResponses.Enqueue(response);
    }

    /// <summary>Clears the presentation space and the field list.</summary>
    public void Clear()
    {
        Array.Fill(_buffer, ' ');
        _fields.Clear();
        Cursor = new CursorPosition(1, 1);
    }

    /// <inheritdoc />
    public Task<ScreenModel> GetScreenAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();

        var fields = _fields
            .Select(f => f with { Text = ReadRaw(f.Row, f.Column, f.Length) })
            .ToList();

        var model = new ScreenModel(Rows, Columns, new string(_buffer), Cursor, fields, PeekOia());
        return Task.FromResult(model);
    }

    /// <inheritdoc />
    public Task<OiaStatus> GetOiaAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();

        var oia = PeekOia();
        if (_inhibitPollsRemaining > 0)
        {
            _inhibitPollsRemaining--;
        }

        return Task.FromResult(oia);
    }

    /// <inheritdoc />
    public Task<string> GetTextAsync(int row, int column, int length, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadRaw(row, column, length));
    }

    /// <inheritdoc />
    public Task SetTextAsync(int row, int column, string text, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var field = FindField(row, column);
        if (field is { IsProtected: true })
        {
            throw new TerminalException(string.Create(
                CultureInfo.InvariantCulture,
                $"Cannot write to protected field at row {field.Row}, column {field.Column}."));
        }

        Poke(row, column, text);
        Cursor = new CursorPosition(row, Math.Min(Columns, column + text.Length));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendKeysAsync(IReadOnlyList<Keystroke> keystrokes, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentNullException.ThrowIfNull(keystrokes);
        cancellationToken.ThrowIfCancellationRequested();

        SentKeys.Add(MnemonicParser.ToCanonicalString(keystrokes));

        foreach (var key in keystrokes)
        {
            if (key.Kind == KeystrokeKind.Text)
            {
                TypeText(key.Value);
                continue;
            }

            ApplyMnemonic(key.Value);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CursorPosition> GetCursorAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Cursor);
    }

    /// <inheritdoc />
    public Task SetCursorAsync(int row, int column, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();
        CoordinateMapper.ToOffset(row, column, Rows, Columns);
        Cursor = new CursorPosition(row, column);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<FileTransferResult> TransferFileAsync(FileTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileTransferResult(
            false,
            0,
            "IND$FILE transfer is not implemented by the fake provider."));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsConnected = false;
    }

    private OiaStatus PeekOia() => _inhibitPollsRemaining > 0
        ? new OiaStatus(true, StatusText: "X SYSTEM")
        : new OiaStatus(false, StatusText: "READY");

    private void ApplyMnemonic(string mnemonic)
    {
        switch (mnemonic)
        {
            case "home":
                Cursor = new CursorPosition(1, 1);
                return;
            case "tab":
                Cursor = NextUnprotectedFieldStart();
                return;
            case "clear":
                Clear();
                break;
            case "left":
                Cursor = new CursorPosition(Cursor.Row, Math.Max(1, Cursor.Column - 1));
                return;
            case "right":
                Cursor = new CursorPosition(Cursor.Row, Math.Min(Columns, Cursor.Column + 1));
                return;
            case "up":
                Cursor = new CursorPosition(Math.Max(1, Cursor.Row - 1), Cursor.Column);
                return;
            case "down":
                Cursor = new CursorPosition(Math.Min(Rows, Cursor.Row + 1), Cursor.Column);
                return;
            default:
                break;
        }

        if (!MnemonicParser.IsAidKey(mnemonic))
        {
            return;
        }

        _inhibitPollsRemaining = InhibitPollsPerAid;
        if (_hostResponses.Count > 0)
        {
            _hostResponses.Dequeue()(this);
        }
    }

    private void TypeText(string text)
    {
        var field = FindField(Cursor.Row, Cursor.Column);
        if (field is { IsProtected: true })
        {
            throw new TerminalException(string.Create(
                CultureInfo.InvariantCulture,
                $"Cannot type into protected field at row {Cursor.Row}, column {Cursor.Column}."));
        }

        Poke(Cursor.Row, Cursor.Column, text);
        Cursor = new CursorPosition(Cursor.Row, Math.Min(Columns, Cursor.Column + text.Length));
    }

    private CursorPosition NextUnprotectedFieldStart()
    {
        var current = CoordinateMapper.ToOffset(Cursor.Row, Cursor.Column, Rows, Columns);
        var candidates = _fields
            .Where(f => !f.IsProtected)
            .Select(f => (Field: f, Offset: CoordinateMapper.ToOffset(f.Row, f.Column, Rows, Columns)))
            .OrderBy(x => x.Offset)
            .ToList();

        if (candidates.Count == 0)
        {
            return Cursor;
        }

        var next = candidates.FirstOrDefault(x => x.Offset > current);
        var target = next.Field ?? candidates[0].Field;
        return new CursorPosition(target.Row, target.Column);
    }

    private ScreenField? FindField(int row, int column)
    {
        var offset = CoordinateMapper.ToOffset(row, column, Rows, Columns);
        foreach (var field in _fields)
        {
            var start = CoordinateMapper.ToOffset(field.Row, field.Column, Rows, Columns);
            if (offset >= start && offset < start + field.Length)
            {
                return field;
            }
        }

        return null;
    }

    private string ReadRaw(int row, int column, int length)
    {
        var offset = CoordinateMapper.ToOffset(row, column, Rows, Columns);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var take = Math.Min(length, _buffer.Length - offset);
        return new string(_buffer, offset, take).PadRight(length);
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsConnected)
        {
            throw new TerminalException(string.Create(
                CultureInfo.InvariantCulture,
                $"Session '{SessionName}' is not connected."));
        }
    }
}

/// <summary>
/// Provider that serves <see cref="FakeTerminalSession"/> instances. Always available, which makes
/// CI and unit tests independent of any installed emulator.
/// </summary>
public sealed class FakeTerminalProvider : ITerminalProvider
{
    /// <summary>The stable provider name.</summary>
    public const string ProviderName = "fake";

    private readonly Dictionary<string, FakeTerminalSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="FakeTerminalProvider"/> class.</summary>
    /// <param name="sessionNames">Session short names to expose. Defaults to a single session "A".</param>
    public FakeTerminalProvider(params string[] sessionNames)
    {
        var names = sessionNames.Length > 0 ? sessionNames : ["A"];
        foreach (var name in names)
        {
            _sessions[name] = new FakeTerminalSession(name);
        }
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Description => "In-memory terminal used for tests and offline exploration. No host connection.";

    /// <summary>
    /// Gets the auto-detection priority. The fake provider is probed last so a real emulator
    /// always wins auto-detection.
    /// </summary>
    public int Priority => int.MaxValue;

    /// <summary>Gets the fake sessions by name so tests can pre-populate screens.</summary>
    public IReadOnlyDictionary<string, FakeTerminalSession> Sessions => _sessions;

    /// <inheritdoc />
    public Task<ProviderAvailability> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ProviderAvailability.Available);

    /// <inheritdoc />
    public Task<IReadOnlyList<TerminalSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TerminalSessionInfo> list = [.. _sessions.Values.Select(s =>
            new TerminalSessionInfo(s.SessionName, ProviderName, s.IsConnected, s.Rows, s.Columns, "Fake session"))];
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<ITerminalSession> ConnectAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        if (!_sessions.TryGetValue(sessionName, out var session))
        {
            throw new TerminalException(string.Create(
                CultureInfo.InvariantCulture,
                $"Fake provider has no session '{sessionName}'. Available: {string.Join(", ", _sessions.Keys)}."));
        }

        return Task.FromResult<ITerminalSession>(session);
    }

    /// <summary>
    /// Renders a fake screen from a list of lines, useful when arranging tests.
    /// </summary>
    /// <param name="sessionName">Session to populate.</param>
    /// <param name="lines">Screen lines, written starting at row 1.</param>
    public void SetScreen(string sessionName, params string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!_sessions.TryGetValue(sessionName, out var session))
        {
            throw new TerminalException(string.Create(CultureInfo.InvariantCulture, $"Fake provider has no session '{sessionName}'."));
        }

        session.Clear();
        for (var i = 0; i < lines.Length && i < session.Rows; i++)
        {
            session.Poke(i + 1, 1, lines[i]);
        }
    }

    /// <summary>Builds a flat presentation space string from lines, for direct <see cref="ScreenModel"/> construction.</summary>
    /// <param name="rows">Rows of the presentation space.</param>
    /// <param name="columns">Columns of the presentation space.</param>
    /// <param name="lines">Screen lines.</param>
    /// <returns>The flat buffer.</returns>
    public static string BuildBuffer(int rows, int columns, params string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sb = new StringBuilder(rows * columns);
        for (var r = 0; r < rows; r++)
        {
            var line = r < lines.Length ? lines[r] : string.Empty;
            sb.Append(line.Length > columns ? line[..columns] : line.PadRight(columns));
        }

        return sb.ToString();
    }
}
