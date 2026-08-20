namespace HostMcp.Core.Terminal;

/// <summary>
/// Raised when a host terminal operation fails in a way the caller can act on
/// (no emulator installed, session not connected, host did not become ready).
/// </summary>
public class TerminalException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TerminalException"/> class.</summary>
    public TerminalException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TerminalException"/> class.</summary>
    /// <param name="message">Error message.</param>
    public TerminalException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TerminalException"/> class.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Underlying exception.</param>
    public TerminalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when the host did not release the keyboard within the configured timeout.
/// </summary>
public sealed class TerminalTimeoutException : TerminalException
{
    /// <summary>Initializes a new instance of the <see cref="TerminalTimeoutException"/> class.</summary>
    public TerminalTimeoutException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TerminalTimeoutException"/> class.</summary>
    /// <param name="message">Error message.</param>
    public TerminalTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TerminalTimeoutException"/> class.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Underlying exception.</param>
    public TerminalTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when no terminal emulator provider is available on this machine.
/// </summary>
public sealed class NoProviderAvailableException : TerminalException
{
    /// <summary>Initializes a new instance of the <see cref="NoProviderAvailableException"/> class.</summary>
    public NoProviderAvailableException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NoProviderAvailableException"/> class.</summary>
    /// <param name="message">Error message.</param>
    public NoProviderAvailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NoProviderAvailableException"/> class.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Underlying exception.</param>
    public NoProviderAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
