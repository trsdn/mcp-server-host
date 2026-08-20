using System.Runtime.InteropServices;

namespace HostMcp.ComInterop;

/// <summary>
/// Probes whether a native library can be loaded into the current process without throwing.
/// </summary>
internal static class NativeLibraryProbe
{
    /// <summary>
    /// Tries to load a native library and immediately frees it again.
    /// </summary>
    /// <param name="libraryName">Library file name.</param>
    /// <param name="reason">Failure reason when the library cannot be loaded.</param>
    /// <returns>True when the library loaded successfully.</returns>
    internal static bool CanLoad(string libraryName, out string reason)
    {
        try
        {
            if (!NativeLibrary.TryLoad(libraryName, out var handle))
            {
                reason = "library not found on the search path";
                return false;
            }

            NativeLibrary.Free(handle);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }
}
