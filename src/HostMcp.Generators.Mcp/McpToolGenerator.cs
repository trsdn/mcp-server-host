using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace HostMcp.Generators.Mcp;

/// <summary>
/// Generates MCP tool classes from Core service interfaces annotated with
/// <c>HostMcp.Core.Attributes.McpToolAttribute</c>.
/// </summary>
/// <remarks>
/// One interface method becomes one <c>[McpServerTool]</c> static method. The generated wrapper
/// forwards to the configured service through <c>ToolInvoker.InvokeAsync</c>, which serializes the
/// result and converts exceptions into structured error payloads. This keeps the tool surface
/// single-sourced in <c>HostMcp.Core</c>: the MCP tool list cannot drift from the implementation.
/// </remarks>
[Generator]
public sealed class McpToolGenerator : IIncrementalGenerator
{
    private const string McpToolAttributeName = "HostMcp.Core.Attributes.McpToolAttribute";
    private const string ToolNameAttributeName = "HostMcp.Core.Attributes.ToolNameAttribute";
    private const string DescriptionAttributeName = "System.ComponentModel.DescriptionAttribute";
    private const string CancellationTokenName = "System.Threading.CancellationToken";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            context.CompilationProvider,
            static (spc, compilation) =>
            {
                foreach (var service in DiscoverServices(compilation))
                {
                    var code = GenerateToolClass(service);
                    spc.AddSource(service.ToolClassName + ".g.cs", SourceText.From(code, Encoding.UTF8));
                }
            });
    }

    private static List<ServiceInfo> DiscoverServices(Compilation compilation)
    {
        var result = new List<ServiceInfo>();

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                var info = TryExtract(type);
                if (info != null)
                {
                    result.Add(info);
                }
            }
        }

        return result;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(child))
            {
                yield return type;
            }
        }
    }

    private static ServiceInfo? TryExtract(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Interface || type.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        var attribute = type.GetAttributes().FirstOrDefault(
            a => a.AttributeClass?.ToDisplayString() == McpToolAttributeName);

        if (attribute == null || attribute.ConstructorArguments.Length < 2)
        {
            return null;
        }

        var toolClassName = attribute.ConstructorArguments[0].Value as string;
        var accessorPath = attribute.ConstructorArguments[1].Value as string;
        if (string.IsNullOrEmpty(toolClassName) || string.IsNullOrEmpty(accessorPath))
        {
            return null;
        }

        var methods = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .Select(ToMethodInfo)
            .Where(m => m != null)
            .Select(m => m!)
            .ToList();

        return methods.Count == 0 ? null : new ServiceInfo(toolClassName!, accessorPath!, methods);
    }

    /// <summary>
    /// Fully qualified type names including nullable reference annotations. Without the nullable
    /// modifier the generated signatures would reject the <c>= null</c> defaults declared on the
    /// service interface.
    /// </summary>
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static MethodInfo? ToMethodInfo(IMethodSymbol method)
    {
        var parameters = method.Parameters
            .Select(p => new ParameterInfo(
                p.Name,
                p.Type.ToDisplayString(TypeFormat),
                GetDescription(p),
                RenderDefault(p),
                p.Type.ToDisplayString() == CancellationTokenName))
            .ToList();

        return new MethodInfo(
            method.Name,
            ResolveToolName(method),
            GetDescription(method),
            parameters);
    }

    private static string ResolveToolName(IMethodSymbol method)
    {
        var explicitName = method.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ToolNameAttributeName)
            ?.ConstructorArguments.FirstOrDefault().Value as string;

        if (!string.IsNullOrEmpty(explicitName))
        {
            return explicitName!;
        }

        var name = method.Name;
        if (name.EndsWith("Async", System.StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Async".Length);
        }

        return ToSnakeCase(name);
    }

    private static string ToSnakeCase(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string? GetDescription(ISymbol symbol)
        => symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeName)
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    private static string? RenderDefault(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return null;
        }

        var value = parameter.ExplicitDefaultValue;
        return value switch
        {
            null => "default",
            bool b => b ? "true" : "false",
            string s => "\"" + Escape(s) + "\"",
            char c => "'" + c + "'",
            _ => System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "default",
        };
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string GenerateToolClass(ServiceInfo service)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by HostMcp.Generators.Mcp from a [McpTool] Core service interface.");
        sb.AppendLine("// Do not edit: change the interface in HostMcp.Core instead.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1591");
        sb.AppendLine();
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using ModelContextProtocol.Server;");
        sb.AppendLine();
        sb.AppendLine("namespace HostMcp.McpServer.Tools;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Generated MCP tools. Every method forwards to the configured Core service.</summary>");
        sb.AppendLine("[McpServerToolType]");
        sb.AppendLine("public static partial class " + service.ToolClassName);
        sb.AppendLine("{");

        foreach (var method in service.Methods)
        {
            AppendMethod(sb, service, method);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendMethod(StringBuilder sb, ServiceInfo service, MethodInfo method)
    {
        sb.AppendLine("    /// <summary>MCP tool <c>" + method.ToolName + "</c>.</summary>");
        sb.AppendLine("    /// <returns>A JSON payload with the tool result, or a structured error.</returns>");
        sb.AppendLine("    [McpServerTool(Name = \"" + method.ToolName + "\")]");
        if (!string.IsNullOrEmpty(method.Description))
        {
            sb.AppendLine("    [Description(\"" + Escape(method.Description!) + "\")]");
        }

        sb.AppendLine("    public static global::System.Threading.Tasks.Task<string> " + StripAsync(method.Name) + "(");

        var declarations = new List<string>();
        foreach (var p in method.Parameters)
        {
            var description = string.IsNullOrEmpty(p.Description)
                ? string.Empty
                : "[Description(\"" + Escape(p.Description!) + "\")] ";

            var defaultValue = p.DefaultValue == null ? string.Empty : " = " + p.DefaultValue;
            declarations.Add("        " + description + p.Type + " " + p.Name + defaultValue);
        }

        sb.AppendLine(string.Join("," + System.Environment.NewLine, declarations));
        sb.AppendLine("    )");

        var arguments = string.Join(", ", method.Parameters.Select(p => p.Name));
        sb.AppendLine("        => global::HostMcp.Core.Services.ToolInvoker.InvokeAsync(");
        sb.AppendLine("            \"" + method.ToolName + "\",");
        sb.AppendLine("            () => global::" + service.AccessorPath + "." + method.Name + "(" + arguments + "));");
        sb.AppendLine();
    }

    private static string StripAsync(string name)
        => name.EndsWith("Async", System.StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Async".Length)
            : name;

    private sealed class ServiceInfo
    {
        public ServiceInfo(string toolClassName, string accessorPath, List<MethodInfo> methods)
        {
            ToolClassName = toolClassName;
            AccessorPath = accessorPath;
            Methods = methods;
        }

        public string ToolClassName { get; }

        public string AccessorPath { get; }

        public List<MethodInfo> Methods { get; }
    }

    private sealed class MethodInfo
    {
        public MethodInfo(string name, string toolName, string? description, List<ParameterInfo> parameters)
        {
            Name = name;
            ToolName = toolName;
            Description = description;
            Parameters = parameters;
        }

        public string Name { get; }

        public string ToolName { get; }

        public string? Description { get; }

        public List<ParameterInfo> Parameters { get; }
    }

    private sealed class ParameterInfo
    {
        public ParameterInfo(string name, string type, string? description, string? defaultValue, bool isCancellationToken)
        {
            Name = name;
            Type = type;
            Description = description;
            DefaultValue = defaultValue;
            IsCancellationToken = isCancellationToken;
        }

        public string Name { get; }

        public string Type { get; }

        public string? Description { get; }

        public string? DefaultValue { get; }

        public bool IsCancellationToken { get; }
    }
}
