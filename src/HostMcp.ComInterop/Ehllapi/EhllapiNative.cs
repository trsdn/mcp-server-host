using System.Runtime.InteropServices;
using System.Text;

namespace HostMcp.ComInterop.Ehllapi;

/// <summary>
/// EHLLAPI function numbers used by this provider.
/// </summary>
public enum EhllapiFunction
{
    /// <summary>Connect Presentation Space.</summary>
    ConnectPresentationSpace = 1,

    /// <summary>Disconnect Presentation Space.</summary>
    DisconnectPresentationSpace = 2,

    /// <summary>Send Key.</summary>
    SendKey = 3,

    /// <summary>Wait (blocks until the keyboard is unlocked or the emulator timeout expires).</summary>
    Wait = 4,

    /// <summary>Copy Presentation Space.</summary>
    CopyPresentationSpace = 5,

    /// <summary>Search Presentation Space.</summary>
    SearchPresentationSpace = 6,

    /// <summary>Query Cursor Location.</summary>
    QueryCursorLocation = 7,

    /// <summary>Copy Presentation Space to String.</summary>
    CopyPresentationSpaceToString = 8,

    /// <summary>Copy String to Presentation Space.</summary>
    CopyStringToPresentationSpace = 15,

    /// <summary>Set Cursor.</summary>
    SetCursor = 40,
}

/// <summary>
/// P/Invoke surface for the EHLLAPI entry point exported by IBM Personal Communications.
/// </summary>
/// <remarks>
/// <para>
/// EHLLAPI exposes a single entry point, <c>hllapi</c>, that takes four by-reference parameters:
/// the function number, a data buffer, a length, and a position/return-code slot. Which of those
/// carries input and which carries output depends on the function, which is why every call is
/// wrapped in a named method in <c>EhllapiTerminalSession</c> rather than called directly.
/// </para>
/// <para>
/// <c>pcshll32.dll</c> is a 32-bit library on most PCOMM installations. A 64-bit process cannot
/// load it, so the EHLLAPI provider reports itself unavailable there instead of crashing.
/// See docs/PLATFORM.md.
/// </para>
/// </remarks>
internal static partial class EhllapiNative
{
    /// <summary>Name of the IBM Personal Communications EHLLAPI library.</summary>
    internal const string LibraryName = "pcshll32.dll";

    [LibraryImport(LibraryName, EntryPoint = "hllapi", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    internal static partial void Hllapi(
        ref int function,
        [Out] byte[] data,
        ref int length,
        ref int returnCode);

    /// <summary>
    /// Invokes EHLLAPI with a byte buffer, returning the (possibly updated) length and return code.
    /// </summary>
    /// <param name="function">EHLLAPI function number.</param>
    /// <param name="data">Data buffer, sized by the caller.</param>
    /// <param name="length">Length parameter on input; updated by some functions on output.</param>
    /// <param name="returnCode">Position parameter on input for some functions; the return code on output.</param>
    internal static void Invoke(EhllapiFunction function, byte[] data, ref int length, ref int returnCode)
    {
        var func = (int)function;
        Hllapi(ref func, data, ref length, ref returnCode);
    }

    /// <summary>Encodes a string into a fixed size EHLLAPI byte buffer.</summary>
    /// <param name="value">Value to encode.</param>
    /// <param name="size">Buffer size; defaults to the encoded length.</param>
    /// <returns>The buffer.</returns>
    internal static byte[] ToBuffer(string value, int? size = null)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var buffer = new byte[size ?? bytes.Length];
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, buffer.Length));
        return buffer;
    }

    /// <summary>Decodes an EHLLAPI byte buffer into a string.</summary>
    /// <param name="buffer">Buffer to decode.</param>
    /// <param name="length">Number of bytes to decode.</param>
    /// <returns>The decoded string with NUL characters replaced by spaces.</returns>
    internal static string FromBuffer(byte[] buffer, int length)
        => Encoding.ASCII.GetString(buffer, 0, Math.Min(length, buffer.Length)).Replace('\0', ' ');
}
