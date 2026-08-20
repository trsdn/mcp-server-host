using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using HostMcp.ComInterop;
using HostMcp.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace HostMcp.CLI;

/// <summary>
/// Entry point of the HostMcp command line tool.
/// </summary>
public static class Program
{
    /// <summary>Runs the CLI.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("hostmcp");
            config.AddCommand<ProvidersCommand>("providers")
                .WithDescription("Probes the terminal providers and reports which one auto-detection would pick.");
            config.AddCommand<SessionsCommand>("sessions")
                .WithDescription("Lists the terminal sessions the detected emulator exposes.");
            config.AddCommand<ScreenCommand>("screen")
                .WithDescription("Reads and prints the current screen of a session.");
            config.AddCommand<SendCommand>("send")
                .WithDescription("Sends a keystroke sequence and prints the resulting screen.");
        });

        return app.Run(args);
    }
}

/// <summary>Settings shared by all commands.</summary>
public class HostSettings : CommandSettings
{
    /// <summary>Gets or sets the provider override.</summary>
    [CommandOption("-p|--provider <PROVIDER>")]
    [Description("Provider override: auto (default), pcomm, ehllapi or fake.")]
    public string? Provider { get; set; }

    /// <summary>Gets or sets a value indicating whether raw JSON is printed instead of a table.</summary>
    [CommandOption("--json")]
    [Description("Print the raw tool JSON result.")]
    public bool Json { get; set; }
}

/// <summary>Settings for commands that address a session.</summary>
public class SessionSettings : HostSettings
{
    /// <summary>Gets or sets the emulator short session name.</summary>
    [CommandArgument(0, "<SESSION>")]
    [Description("Emulator short session name, for example 'A'.")]
    public string Session { get; set; } = "A";
}

/// <summary>Shared helpers for CLI commands.</summary>
public static class CommandSupport
{
    /// <summary>Creates the terminal service using the CLI provider override.</summary>
    /// <param name="settings">Command settings.</param>
    /// <returns>The service and its session manager.</returns>
    public static (IHostTerminalService Service, TerminalSessionManager Manager) Create(HostSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = HostTerminalFactory.CreateOptions();
        if (!string.IsNullOrWhiteSpace(settings.Provider))
        {
            options.Provider = settings.Provider;
        }

        return HostTerminalFactory.Create(options);
    }

    /// <summary>Prints a result as JSON.</summary>
    /// <param name="value">Value to print.</param>
    public static void WriteJson(object value)
        => Console.WriteLine(JsonSerializer.Serialize(value, ToolInvoker.JsonOptions));

    /// <summary>Prints an error message and returns the failure exit code.</summary>
    /// <param name="message">Message to print.</param>
    /// <returns>Exit code 1.</returns>
    public static int Fail(string? message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{message ?? "unknown error"}[/]");
        return 1;
    }
}

/// <summary>Probes the registered providers.</summary>
public sealed class ProvidersCommand : AsyncCommand<HostSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, HostSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var (_, manager) = CommandSupport.Create(settings);
        using (manager)
        {
            var table = new Table().AddColumn("Provider").AddColumn("Priority").AddColumn("Available").AddColumn("Detail");

            foreach (var provider in manager.Providers)
            {
                var availability = await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
                table.AddRow(
                    provider.Name,
                    provider.Priority.ToString(CultureInfo.InvariantCulture),
                    availability.IsAvailable ? "yes" : "no",
                    Markup.Escape(availability.Reason ?? provider.Description));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}

/// <summary>Lists sessions.</summary>
public sealed class SessionsCommand : AsyncCommand<HostSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, HostSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var (service, manager) = CommandSupport.Create(settings);
        using (manager)
        {
            var result = await service.ListSessionsAsync(settings.Provider, cancellationToken).ConfigureAwait(false);

            if (settings.Json)
            {
                CommandSupport.WriteJson(result);
                return result.Success ? 0 : 1;
            }

            if (!result.Success)
            {
                return CommandSupport.Fail(result.ErrorMessage);
            }

            var table = new Table().AddColumn("Session").AddColumn("Provider").AddColumn("Connected").AddColumn("Detail");
            foreach (var session in result.Sessions)
            {
                table.AddRow(
                    session.Name,
                    session.ProviderName,
                    session.Connected ? "yes" : "no",
                    Markup.Escape(session.Description ?? string.Empty));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}

/// <summary>Prints the current screen.</summary>
public sealed class ScreenCommand : AsyncCommand<SessionSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, SessionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var (service, manager) = CommandSupport.Create(settings);
        using (manager)
        {
            var result = await service.GetScreenAsync(settings.Session, cancellationToken).ConfigureAwait(false);
            return Render(result, settings.Json);
        }
    }

    internal static int Render(ScreenResult result, bool json)
    {
        if (json)
        {
            CommandSupport.WriteJson(result);
            return result.Success ? 0 : 1;
        }

        if (!result.Success)
        {
            return CommandSupport.Fail(result.ErrorMessage);
        }

        AnsiConsole.Write(new Panel(Markup.Escape(result.Text))
            .Header(string.Create(
                CultureInfo.InvariantCulture,
                $"session {result.Session} - {result.Rows}x{result.Columns} - cursor {result.CursorRow},{result.CursorColumn} - {(result.Oia.InputInhibited ? "INHIBITED" : "READY")}")));

        return 0;
    }
}

/// <summary>Sends keystrokes.</summary>
public sealed class SendCommand : AsyncCommand<SendCommand.Settings>
{
    /// <summary>Settings for the send command.</summary>
    public sealed class Settings : SessionSettings
    {
        /// <summary>Gets or sets the keystroke sequence.</summary>
        [CommandArgument(1, "<KEYS>")]
        [Description("Keystroke sequence, for example 'USER01[tab]SECRET[enter]'.")]
        public string Keys { get; set; } = string.Empty;

        /// <summary>Gets or sets the readiness wait timeout in seconds.</summary>
        [CommandOption("-t|--timeout <SECONDS>")]
        [Description("Readiness timeout in seconds.")]
        public int? TimeoutSeconds { get; set; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var (service, manager) = CommandSupport.Create(settings);
        using (manager)
        {
            var result = await service
                .SendKeysAsync(settings.Session, settings.Keys, waitForReady: true, settings.TimeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            return ScreenCommand.Render(result, settings.Json);
        }
    }
}
