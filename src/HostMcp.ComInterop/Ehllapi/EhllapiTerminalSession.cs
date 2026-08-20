using System.Globalization;
using HostMcp.Core.Models;
using HostMcp.Core.Terminal;

namespace HostMcp.ComInterop.Ehllapi;

/// <summary>
/// A terminal session driven through the EHLLAPI entry point of <c>pcshll32.dll</c>.
/// </summary>
/// <remarks>
/// This is the fallback provider. EHLLAPI is process-wide: only one presentation space is
/// connected at a time, so every operation re-connects the session before it runs. That is slower
/// than PCOMM automation but works with any emulator that ships a compatible <c>hllapi</c> export.
/// </remarks>
public sealed class EhllapiTerminalSession : ITerminalSession
{
    private static readonly SemaphoreSlim ApiGate = new(1, 1);

    private readonly int _bufferSize;
    private bool _disposed;

    internal EhllapiTerminalSession(string sessionName, int rows, int columns)
    {
        SessionName = sessionName;
        Rows = rows;
        Columns = columns;
        _bufferSize = rows * columns;
    }

    /// <inheritdoc />
    public string SessionName { get; }

    /// <inheritdoc />
    public string ProviderName => EhllapiTerminalProvider.ProviderName;

    /// <inheritdoc />
    public bool IsConnected => !_disposed;

    /// <inheritdoc />
    public int Rows { get; }

    /// <inheritdoc />
    public int Columns { get; }

    /// <inheritdoc />
    public async Task<ScreenModel> GetScreenAsync(CancellationToken cancellationToken = default)
    {
        var buffer = await ReadPresentationSpaceAsync(cancellationToken).ConfigureAwait(false);
        var cursor = await GetCursorAsync(cancellationToken).ConfigureAwait(false);
        var oia = await GetOiaAsync(cancellationToken).ConfigureAwait(false);

        // EHLLAPI's "Copy PS to String" returns characters only; field attributes require the
        // extended "Find Field"/"Query Field Attribute" calls which are not part of the minimal
        // fallback surface. Callers get coordinate based access and an empty field list.
        return new ScreenModel(Rows, Columns, buffer, cursor, [], oia);
    }

    /// <inheritdoc />
    public Task<OiaStatus> GetOiaAsync(CancellationToken cancellationToken = default)
        => RunAsync(
            () =>
            {
                // "Copy OIA" is not in the minimal function set; the Wait function (4) reports
                // whether the keyboard is currently locked: return code 0 means the keyboard is
                // free, 4 means it is still inhibited, 5 means the keyboard is locked by an error.
                var data = new byte[1];
                var length = 0;
                var rc = 0;
                EhllapiNative.Invoke(EhllapiFunction.Wait, data, ref length, ref rc);

                return rc switch
                {
                    0 => new OiaStatus(false, StatusText: "READY"),
                    4 => new OiaStatus(true, StatusText: "X SYSTEM (timeout waiting for host)"),
                    5 => new OiaStatus(true, CommunicationError: false, StatusText: "X -f (keyboard locked)"),
                    9 => new OiaStatus(true, CommunicationError: true, StatusText: "System error"),
                    _ => new OiaStatus(false, StatusText: string.Create(CultureInfo.InvariantCulture, $"rc={rc}")),
                };
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<string> GetTextAsync(int row, int column, int length, CancellationToken cancellationToken = default)
    {
        var offset = CoordinateMapper.ToOffset(row, column, Rows, Columns);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var buffer = await ReadPresentationSpaceAsync(cancellationToken).ConfigureAwait(false);
        var take = Math.Min(length, buffer.Length - offset);
        return buffer.Substring(offset, Math.Max(0, take)).PadRight(length);
    }

    /// <inheritdoc />
    public Task SetTextAsync(int row, int column, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var position = CoordinateMapper.ToLinearPosition(row, column, Rows, Columns);

        return RunAsync(
            () =>
            {
                var data = EhllapiNative.ToBuffer(text);
                var length = text.Length;
                var rc = position;
                EhllapiNative.Invoke(EhllapiFunction.CopyStringToPresentationSpace, data, ref length, ref rc);
                ThrowOnError(rc, "Copy String to PS");
                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendKeysAsync(IReadOnlyList<Keystroke> keystrokes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keystrokes);
        var encoded = EhllapiMnemonics.Encode(keystrokes);

        return RunAsync(
            () =>
            {
                var data = EhllapiNative.ToBuffer(encoded);
                var length = encoded.Length;
                var rc = 0;
                EhllapiNative.Invoke(EhllapiFunction.SendKey, data, ref length, ref rc);
                ThrowOnError(rc, "Send Key");
                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<CursorPosition> GetCursorAsync(CancellationToken cancellationToken = default)
        => RunAsync(
            () =>
            {
                var data = new byte[1];
                var length = 0;
                var rc = 0;
                EhllapiNative.Invoke(EhllapiFunction.QueryCursorLocation, data, ref length, ref rc);
                ThrowOnError(rc, "Query Cursor Location");
                return CoordinateMapper.FromLinearPosition(Math.Max(1, length), Rows, Columns);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetCursorAsync(int row, int column, CancellationToken cancellationToken = default)
    {
        var position = CoordinateMapper.ToLinearPosition(row, column, Rows, Columns);

        return RunAsync(
            () =>
            {
                var data = new byte[1];
                var length = 0;
                var rc = position;
                EhllapiNative.Invoke(EhllapiFunction.SetCursor, data, ref length, ref rc);
                ThrowOnError(rc, "Set Cursor");
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Searches the presentation space with EHLLAPI function 6.
    /// </summary>
    /// <param name="text">Text to search for.</param>
    /// <param name="startPosition">1-based linear position to start from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 1-based position of the match, or null when not found.</returns>
    public Task<CursorPosition?> SearchAsync(string text, int startPosition = 1, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        return RunAsync<CursorPosition?>(
            () =>
            {
                var data = EhllapiNative.ToBuffer(text);
                var length = text.Length;
                var rc = startPosition;
                EhllapiNative.Invoke(EhllapiFunction.SearchPresentationSpace, data, ref length, ref rc);

                // rc 24 means "string not found".
                return rc == 24 ? null : CoordinateMapper.FromLinearPosition(Math.Max(1, length), Rows, Columns);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<FileTransferResult> TransferFileAsync(FileTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // EHLLAPI file transfer uses functions 90/91 (Send File / Receive File), which need
        // site specific IND$FILE options. Deliberately unimplemented. See docs/TOOLS.md.
        return Task.FromResult(new FileTransferResult(
            false,
            0,
            "IND$FILE transfer is not implemented yet. Planned via EHLLAPI Send File (90) / Receive File (91). See docs/TOOLS.md."));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            ApiGate.Wait(TimeSpan.FromSeconds(5));
            try
            {
                var data = new byte[1];
                var length = 0;
                var rc = 0;
                EhllapiNative.Invoke(EhllapiFunction.DisconnectPresentationSpace, data, ref length, ref rc);
            }
            finally
            {
                ApiGate.Release();
            }
        }
        catch (Exception)
        {
            // Disconnecting a presentation space that the emulator already dropped is not an error.
        }
    }

    private Task<string> ReadPresentationSpaceAsync(CancellationToken cancellationToken)
        => RunAsync(
            () =>
            {
                var data = new byte[_bufferSize];
                var length = _bufferSize;
                var rc = 1;
                EhllapiNative.Invoke(EhllapiFunction.CopyPresentationSpaceToString, data, ref length, ref rc);
                ThrowOnError(rc, "Copy PS to String", allowed: [0, 4, 5]);
                return EhllapiNative.FromBuffer(data, _bufferSize);
            },
            cancellationToken);

    /// <summary>
    /// Runs an EHLLAPI operation on a worker thread, holding the process-wide API lock and
    /// re-connecting the presentation space first.
    /// </summary>
    private Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(
            async () =>
            {
                await ApiGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Connect();
                    return operation();
                }
                finally
                {
                    ApiGate.Release();
                }
            },
            cancellationToken);
    }

    private void Connect()
    {
        var data = EhllapiNative.ToBuffer(SessionName, 1);
        var length = 1;
        var rc = 0;
        EhllapiNative.Invoke(EhllapiFunction.ConnectPresentationSpace, data, ref length, ref rc);

        // rc 4 means "connected, host busy", which is a normal state, not a failure.
        if (rc is not (0 or 4))
        {
            throw new TerminalException(string.Create(
                CultureInfo.InvariantCulture,
                $"EHLLAPI could not connect to presentation space '{SessionName}' (rc={rc}). Check that the emulator session exists and that its short name matches."));
        }
    }

    private static void ThrowOnError(int rc, string operation, int[]? allowed = null)
    {
        var ok = allowed ?? [0];
        if (Array.IndexOf(ok, rc) >= 0)
        {
            return;
        }

        throw new TerminalException(string.Create(
            CultureInfo.InvariantCulture,
            $"EHLLAPI '{operation}' failed with return code {rc}."));
    }
}
