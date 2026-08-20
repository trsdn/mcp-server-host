using System.Text.Json;
using System.Text.Json.Serialization;
using HostMcp.Core.Terminal;

namespace HostMcp.Core.Services;

/// <summary>
/// Static accessor used by generated MCP tool classes to resolve the service implementation.
/// </summary>
/// <remarks>
/// The MCP SDK discovers tools as static methods, so the generated tool wrappers need a static
/// entry point. The host process configures this once at startup.
/// </remarks>
public static class ToolServices
{
    private static IHostTerminalService? _hostTerminal;

    /// <summary>Gets the configured host terminal service.</summary>
    /// <exception cref="InvalidOperationException">The host process did not configure the service.</exception>
    public static IHostTerminalService HostTerminal =>
        _hostTerminal ?? throw new InvalidOperationException(
            "ToolServices has not been configured. Call ToolServices.Configure(...) during host startup.");

    /// <summary>Gets a value indicating whether the service has been configured.</summary>
    public static bool IsConfigured => _hostTerminal is not null;

    /// <summary>Configures the service instance used by generated tools.</summary>
    /// <param name="service">Service implementation.</param>
    public static void Configure(IHostTerminalService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _hostTerminal = service;
    }

    /// <summary>Clears the configured service (used by tests).</summary>
    public static void Reset() => _hostTerminal = null;
}

/// <summary>
/// Executes a tool operation and renders its result, or a structured error, as JSON.
/// </summary>
/// <remarks>
/// Terminal automation fails for many mundane reasons: the emulator is not installed, the session
/// is disconnected, the host is slow. None of those should terminate the MCP server, so every
/// generated tool routes through here and returns a JSON payload with <c>success:false</c> instead
/// of throwing across the protocol boundary.
/// </remarks>
public static class ToolInvoker
{
    /// <summary>JSON options tuned for LLM token efficiency.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Runs a tool operation and serializes its result.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="toolName">Tool name used in error messages.</param>
    /// <param name="operation">Operation to execute.</param>
    /// <returns>The JSON payload returned to the MCP client.</returns>
    public static async Task<string> InvokeAsync<T>(string toolName, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            var result = await operation().ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            return Error(toolName, "The operation was cancelled.", "cancelled");
        }
        catch (NoProviderAvailableException ex)
        {
            return Error(toolName, ex.Message, "no_provider");
        }
        catch (TerminalTimeoutException ex)
        {
            return Error(toolName, ex.Message, "timeout");
        }
        catch (TerminalException ex)
        {
            return Error(toolName, ex.Message, "terminal");
        }
        catch (FormatException ex)
        {
            return Error(toolName, ex.Message, "bad_mnemonic");
        }
        catch (ArgumentException ex)
        {
            return Error(toolName, ex.Message, "bad_argument");
        }
        catch (Exception ex)
        {
            return Error(toolName, ex.Message, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Serializes a structured tool error.
    /// </summary>
    /// <param name="toolName">Tool that failed.</param>
    /// <param name="message">Error message.</param>
    /// <param name="kind">Machine readable error category.</param>
    /// <returns>The JSON error payload.</returns>
    public static string Error(string toolName, string message, string kind)
        => JsonSerializer.Serialize(
            new ToolErrorPayload(false, $"{toolName} failed: {message}", kind, true),
            JsonOptions);

    /// <summary>Structured error payload returned to MCP clients.</summary>
    /// <param name="Success">Always false.</param>
    /// <param name="ErrorMessage">Human readable message.</param>
    /// <param name="ErrorKind">Machine readable category.</param>
    /// <param name="IsError">Always true, for clients that key off this field.</param>
    public sealed record ToolErrorPayload(bool Success, string ErrorMessage, string ErrorKind, bool IsError);
}
