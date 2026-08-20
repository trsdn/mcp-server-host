using HostMcp.ComInterop.Ehllapi;
using HostMcp.ComInterop.Pcomm;
using HostMcp.Core.Providers;
using HostMcp.Core.Terminal;

namespace HostMcp.ComInterop;

/// <summary>
/// Builds the ordered provider list used for auto-detection.
/// </summary>
/// <remarks>
/// Construction is side effect free: no COM object is created and no native library is loaded
/// until a provider is probed. This is what keeps the MCP server startable on a machine with no
/// emulator installed.
/// </remarks>
public static class TerminalProviderFactory
{
    /// <summary>
    /// Creates the default provider set: PCOMM automation, EHLLAPI, and optionally the
    /// in-memory fake used for offline exploration and tests.
    /// </summary>
    /// <param name="includeFake">
    /// When true, registers <see cref="FakeTerminalProvider"/> as a last-resort provider. It is
    /// only ever selected by auto-detection when no real emulator is present.
    /// </param>
    /// <param name="rows">Presentation space rows assumed by the EHLLAPI provider.</param>
    /// <param name="columns">Presentation space columns assumed by the EHLLAPI provider.</param>
    /// <returns>The providers in auto-detection order.</returns>
    public static IReadOnlyList<ITerminalProvider> CreateDefault(
        bool includeFake = false,
        int rows = 24,
        int columns = 80)
    {
        var providers = new List<ITerminalProvider>
        {
            new PcommTerminalProvider(),
            new EhllapiTerminalProvider(rows, columns),
        };

        if (includeFake)
        {
            providers.Add(new FakeTerminalProvider());
        }

        return providers;
    }
}
