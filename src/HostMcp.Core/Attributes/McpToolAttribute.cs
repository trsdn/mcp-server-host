namespace HostMcp.Core.Attributes;

/// <summary>
/// Marks a Core service interface as the source of MCP tools.
/// The <c>HostMcp.Generators.Mcp</c> source generator emits one
/// <c>[McpServerToolType]</c> class per annotated interface, with one tool per method.
/// </summary>
/// <remarks>
/// This keeps the tool surface single-sourced in Core: adding a method to the interface adds an
/// MCP tool, and the CLI and tests bind to exactly the same contract.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="McpToolAttribute"/> class.</summary>
    /// <param name="toolClassName">Name of the generated static tool class.</param>
    /// <param name="accessorPath">
    /// Fully qualified static property that resolves the service instance in generated code,
    /// for example <c>HostMcp.Core.Services.ToolServices.HostTerminal</c>.
    /// </param>
    public McpToolAttribute(string toolClassName, string accessorPath)
    {
        ToolClassName = toolClassName;
        AccessorPath = accessorPath;
    }

    /// <summary>Gets the name of the generated static tool class.</summary>
    public string ToolClassName { get; }

    /// <summary>Gets the fully qualified static accessor used to resolve the service instance.</summary>
    public string AccessorPath { get; }
}

/// <summary>
/// Overrides the MCP tool name derived from a service method name.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolNameAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ToolNameAttribute"/> class.</summary>
    /// <param name="name">Explicit snake_case tool name.</param>
    public ToolNameAttribute(string name) => Name = name;

    /// <summary>Gets the explicit snake_case tool name.</summary>
    public string Name { get; }
}
