namespace HostMcp.ComInterop.Pcomm;

/// <summary>
/// ProgIDs of the IBM Personal Communications (PCOMM) automation objects used by this provider.
/// </summary>
/// <remarks>
/// All objects are created by ProgID through late binding, so no PCOMM interop assembly is needed
/// at build time and the solution compiles on machines and CI agents without PCOMM.
/// </remarks>
public static class PcommProgIds
{
    /// <summary>Session root object: binds a connection and exposes PS, OIA and window metrics.</summary>
    public const string Session = "PCOMM.autECLSession";

    /// <summary>Presentation space: screen text, cursor, keystrokes and field list.</summary>
    public const string PresentationSpace = "PCOMM.autECLPS";

    /// <summary>Operator Information Area: the keyboard lock and status indicators.</summary>
    public const string Oia = "PCOMM.autECLOIA";

    /// <summary>Connection list: enumerates the emulator sessions that currently exist.</summary>
    public const string ConnectionList = "PCOMM.autECLConnList";

    /// <summary>Screen descriptor: declarative wait conditions.</summary>
    public const string ScreenDescription = "PCOMM.autECLScreenDesc";

    /// <summary>Field list of a presentation space.</summary>
    public const string FieldList = "PCOMM.autECLFieldList";

    /// <summary>Emulator window metrics (position, size, visibility).</summary>
    public const string WindowMetrics = "PCOMM.autECLWinMetrics";
}
