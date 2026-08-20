using System.Globalization;
using HostMcp.Core.Models;
using HostMcp.Core.Terminal;

namespace HostMcp.ComInterop.Pcomm;

/// <summary>
/// A live PCOMM session accessed through the <c>autECLSession</c> automation object.
/// </summary>
public sealed class PcommTerminalSession : ITerminalSession
{
    private readonly ComObject _session;
    private readonly ComObject _ps;
    private readonly ComObject _oia;
    private bool _disposed;

    internal PcommTerminalSession(string sessionName, ComObject session)
    {
        SessionName = sessionName;
        _session = session;
        _ps = session.GetObject("autECLPS");
        _oia = session.GetObject("autECLOIA");
        Rows = _ps.GetValue<int>("NumRows");
        Columns = _ps.GetValue<int>("NumCols");
    }

    /// <inheritdoc />
    public string SessionName { get; }

    /// <inheritdoc />
    public string ProviderName => PcommTerminalProvider.ProviderName;

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                return _session.GetValue<bool>("Ready");
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public int Rows { get; }

    /// <inheritdoc />
    public int Columns { get; }

    /// <inheritdoc />
    public Task<ScreenModel> GetScreenAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();

        var text = _ps.CallValue<string>("GetText") ?? string.Empty;
        var cursor = ReadCursor();
        var fields = ReadFields();
        var oia = ReadOia();

        return Task.FromResult(new ScreenModel(Rows, Columns, text, cursor, fields, oia));
    }

    /// <inheritdoc />
    public Task<OiaStatus> GetOiaAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadOia());
    }

    /// <inheritdoc />
    public Task<string> GetTextAsync(int row, int column, int length, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();
        CoordinateMapper.ToOffset(row, column, Rows, Columns);

        var text = _ps.CallValue<string>("GetText", row, column, length) ?? string.Empty;
        return Task.FromResult(text.PadRight(length));
    }

    /// <inheritdoc />
    public Task SetTextAsync(int row, int column, string text, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        CoordinateMapper.ToOffset(row, column, Rows, Columns);

        _ps.Call("SetText", text, row, column);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendKeysAsync(IReadOnlyList<Keystroke> keystrokes, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(keystrokes);
        cancellationToken.ThrowIfCancellationRequested();

        // PCOMM's SendKeys uses the same bracket notation the parser produces, so the canonical
        // string round-trips directly.
        _ps.Call("SendKeys", MnemonicParser.ToCanonicalString(keystrokes));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CursorPosition> GetCursorAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadCursor());
    }

    /// <inheritdoc />
    public Task SetCursorAsync(int row, int column, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();
        CoordinateMapper.ToOffset(row, column, Rows, Columns);

        _ps.Call("SetCursorPos", row, column);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<FileTransferResult> TransferFileAsync(FileTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // PCOMM exposes autECLPS.SendFile / ReceiveFile. Wiring it up needs host-side IND$FILE
        // configuration and per-site transfer options, so it is deliberately left unimplemented
        // rather than half implemented. See docs/TOOLS.md.
        return Task.FromResult(new FileTransferResult(
            false,
            0,
            "IND$FILE transfer is not implemented yet. Planned via autECLPS.SendFile / autECLPS.ReceiveFile. See docs/TOOLS.md."));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _oia.Dispose();
        _ps.Dispose();
        _session.Dispose();
    }

    private CursorPosition ReadCursor()
        => new(_ps.GetValue<int>("CursorPosRow"), _ps.GetValue<int>("CursorPosCol"));

    private OiaStatus ReadOia()
    {
        // autECLOIA.InputInhibited returns 0 when the keyboard is usable and a non-zero
        // reason code otherwise (system wait, operator error, communication check).
        var inhibited = _oia.GetValue<int>("InputInhibited");
        var alarm = TryGetBool("Alarm");
        var commCheck = TryGetBool("CommErrorReminder");

        return new OiaStatus(
            inhibited != 0,
            alarm,
            commCheck,
            inhibited == 0
                ? "READY"
                : string.Create(CultureInfo.InvariantCulture, $"INHIBITED (code {inhibited})"));
    }

    private bool TryGetBool(string property)
    {
        try
        {
            return _oia.GetValue<bool>(property);
        }
        catch (Exception)
        {
            // Optional OIA indicators differ between PCOMM releases; treat absence as "not set".
            return false;
        }
    }

    private List<ScreenField> ReadFields()
    {
        var fields = new List<ScreenField>();

        try
        {
            using var list = _ps.GetObject("autECLFieldList");
            list.Call("Refresh");
            var count = list.GetValue<int>("Count");

            for (var i = 1; i <= count; i++)
            {
                using var field = list.CallObject("Item", i);
                var row = field.GetValue<int>("StartRow");
                var column = field.GetValue<int>("StartCol");
                var length = field.GetValue<int>("Length");

                var attributes = FieldAttributes.None;
                if (SafeBool(field, "Protected"))
                {
                    attributes |= FieldAttributes.Protected;
                }

                if (SafeBool(field, "Numeric"))
                {
                    attributes |= FieldAttributes.Numeric;
                }

                if (!SafeBool(field, "Display", defaultValue: true))
                {
                    attributes |= FieldAttributes.Hidden;
                }

                if (SafeBool(field, "HighIntensity"))
                {
                    attributes |= FieldAttributes.Intensified;
                }

                if (SafeBool(field, "Modified"))
                {
                    attributes |= FieldAttributes.Modified;
                }

                if (SafeBool(field, "PenDetectable"))
                {
                    attributes |= FieldAttributes.PenDetectable;
                }

                var text = field.CallValue<string>("GetText") ?? string.Empty;
                fields.Add(new ScreenField(fields.Count, row, column, length, attributes, text.PadRight(length)));
            }
        }
        catch (Exception)
        {
            // A screen without a formatted field list (for example an unformatted 5250 panel)
            // is legitimate. Callers fall back to coordinate based access.
            return fields;
        }

        return fields;
    }

    private static bool SafeBool(ComObject field, string property, bool defaultValue = false)
    {
        try
        {
            return field.GetValue<bool>(property);
        }
        catch (Exception)
        {
            return defaultValue;
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
