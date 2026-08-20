using System.ComponentModel;
using System.Reflection;
using HostMcp.ComInterop;
using HostMcp.Core.Services;
using ModelContextProtocol.Server;
using Xunit;

namespace HostMcp.McpServer.Tests;

/// <summary>
/// Verifies that the source generator produced exactly the documented MCP tool surface. These
/// tests are the guard against the tool list silently drifting from
/// <see cref="IHostTerminalService"/>.
/// </summary>
public class GeneratedToolTests
{
    private static readonly string[] ExpectedTools =
    [
        "list_sessions",
        "connect_session",
        "disconnect_session",
        "get_screen",
        "read_field",
        "write_field",
        "get_text",
        "set_text",
        "send_keys",
        "wait_for_ready",
        "wait_for_text",
        "search_text",
        "get_cursor",
        "set_cursor",
        "transfer_file",
        "batch",
    ];

    private static IReadOnlyList<MethodInfo> ToolMethods()
        => [.. typeof(Program).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)];

    [Fact]
    public void Generator_EmittedAToolTypeForTheService()
    {
        var toolTypes = typeof(Program).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .ToList();

        Assert.Single(toolTypes);
        Assert.Equal("HostTerminalTools", toolTypes[0].Name);
    }

    [Fact]
    public void Generator_EmittedExactlyTheDocumentedTools()
    {
        var names = ToolMethods()
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .ToList();

        Assert.Equal(ExpectedTools.Order(), names.Order());
    }

    [Fact]
    public void ToolCount_MatchesTheServiceInterface()
    {
        var serviceMethods = typeof(IHostTerminalService).GetMethods().Length;

        Assert.Equal(serviceMethods, ToolMethods().Count);
    }

    [Fact]
    public void EveryTool_CarriesADescriptionForTheModel()
    {
        foreach (var method in ToolMethods())
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>();

            Assert.NotNull(description);
            Assert.False(string.IsNullOrWhiteSpace(description!.Description));
        }
    }

    [Fact]
    public void EveryToolParameter_CarriesADescription()
    {
        foreach (var method in ToolMethods())
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    continue;
                }

                var description = parameter.GetCustomAttribute<DescriptionAttribute>();
                Assert.True(
                    description is not null && !string.IsNullOrWhiteSpace(description.Description),
                    $"{method.Name}.{parameter.Name} has no [Description].");
            }
        }
    }

    [Fact]
    public void EveryTool_ReturnsSerializedJson()
    {
        foreach (var method in ToolMethods())
        {
            Assert.Equal(typeof(Task<string>), method.ReturnType);
        }
    }

    [Fact]
    public async Task Tool_ReturnsAStructuredError_WhenNoEmulatorIsAvailable()
    {
        var previous = ToolServices.IsConfigured ? ToolServices.HostTerminal : null;
        try
        {
            var options = HostTerminalFactory.CreateOptions();
            options.Provider = "pcomm";
            var (service, manager) = HostTerminalFactory.Create(options);

            using (manager)
            {
                ToolServices.Configure(service);

                var json = await ToolInvoker.InvokeAsync("list_sessions", () => service.ListSessionsAsync("pcomm"));

                // On a CI agent PCOMM is absent, so the tool must fail gracefully with an
                // explanatory payload instead of throwing across the protocol boundary.
                Assert.Contains("\"success\"", json, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (previous is not null)
            {
                ToolServices.Configure(previous);
            }
            else
            {
                ToolServices.Reset();
            }
        }
    }

    [Fact]
    public void ToolInvoker_RendersErrorsAsSnakeCaseJson()
    {
        var json = ToolInvoker.Error("get_screen", "boom", "terminal");

        Assert.Contains("\"error_message\"", json, StringComparison.Ordinal);
        Assert.Contains("\"error_kind\":\"terminal\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_error\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolInvoker_ConvertsMissingProviderIntoAnErrorPayload()
    {
        var json = await ToolInvoker.InvokeAsync<string>(
            "connect_session",
            () => throw new HostMcp.Core.Terminal.NoProviderAvailableException("no emulator"));

        Assert.Contains("\"error_kind\":\"no_provider\"", json, StringComparison.Ordinal);
        Assert.Contains("no emulator", json, StringComparison.Ordinal);
    }
}
