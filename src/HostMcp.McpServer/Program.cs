using System.Globalization;
using System.Reflection;
using HostMcp.ComInterop;
using HostMcp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostMcp.McpServer;

/// <summary>
/// Entry point of the HostMcp MCP server.
/// </summary>
public static class Program
{
    private const string ServerInstructions = """
        Host terminal automation for IBM 3270 and 5250 sessions (mainframe / IBM i) through a
        locally installed emulator.

        Workflow:
        1. list_sessions   - discover the emulator short names ("A", "B", ...).
        2. connect_session - attach to one; it stays open for later calls.
        3. get_screen      - always read the screen before acting on it. Never assume a layout.
        4. write_field / set_text - fill input fields.
        5. send_keys       - submit with an AID mnemonic, for example "[enter]" or "[pf3]".
        6. get_screen again, or wait_for_text, to confirm the result.

        Synchronization: every mutating tool waits for the OIA keyboard lock to clear before it
        returns, so a send_keys result already reflects the screen the host produced. Only reduce
        that wait when you know the keystroke never reaches the host.

        Coordinates are 1-based (row 1 / column 1 is the top left character), matching EHLLAPI and
        PCOMM conventions.

        Safety: these tools drive a real, authenticated host session. Treat every write as
        production. Never invent host commands, dataset names or credentials, and stop and ask when
        a screen is unfamiliar or a transaction looks destructive.
        """;

    /// <summary>
    /// Runs the MCP server over stdio.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(GetVersion());
            return 0;
        }

        // Terminal automation surfaces plenty of runtime failures. They belong on stderr as a
        // diagnostic, never on stdout, which carries the MCP protocol stream.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[hostmcp] unhandled exception: {e.ExceptionObject}"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[hostmcp] unobserved task exception: {e.Exception}"));
            e.SetObserved();
        };

        // Provider probing happens lazily inside the tools, so a machine without an emulator still
        // starts the server and reports a clear error per tool call.
        var (service, manager) = HostTerminalFactory.Create();
        ToolServices.Configure(service);

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Warning);

            builder.Services
                .AddMcpServer(options => options.ServerInstructions = ServerInstructions)
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            await builder.Build().RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[hostmcp] fatal: {ex.Message}"));
            return 1;
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static string GetVersion()
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

    private static void PrintHelp()
    {
        Console.WriteLine("HostMcp MCP server - IBM 3270/5250 host terminal automation");
        Console.WriteLine();
        Console.WriteLine("Usage: mcp-host [--help] [--version]");
        Console.WriteLine();
        Console.WriteLine("The server speaks the Model Context Protocol over stdio and is normally started");
        Console.WriteLine("by an MCP client, not by hand.");
        Console.WriteLine();
        Console.WriteLine("Environment variables:");
        Console.WriteLine("  HOSTMCP_PROVIDER              auto (default) | pcomm | ehllapi | fake");
        Console.WriteLine("  HOSTMCP_READY_TIMEOUT_SECONDS OIA readiness timeout, default 30");
        Console.WriteLine("  HOSTMCP_POLL_INTERVAL_MS      OIA poll interval, default 100");
        Console.WriteLine("  HOSTMCP_ROWS / HOSTMCP_COLUMNS presentation space size assumed by EHLLAPI (24x80)");
        Console.WriteLine();
        Console.WriteLine("PCOMM automation and pcshll32.dll are frequently 32-bit only. If provider probing");
        Console.WriteLine("fails on a 64-bit build, run the win-x86 build instead. See docs/PLATFORM.md.");
    }
}
