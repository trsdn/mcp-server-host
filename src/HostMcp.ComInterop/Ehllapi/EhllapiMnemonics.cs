using System.Globalization;
using System.Text;
using HostMcp.Core.Models;

namespace HostMcp.ComInterop.Ehllapi;

/// <summary>
/// Translates the portable <see cref="Keystroke"/> model into EHLLAPI Send Key (function 3) strings.
/// </summary>
/// <remarks>
/// EHLLAPI encodes control keys as two-character escapes beginning with <c>@</c>, for example
/// <c>@E</c> for Enter and <c>@1</c> for PF1. A literal <c>@</c> in typed text must be doubled.
/// The table is kept here, in one place, so both the EHLLAPI provider and its tests use exactly the
/// same mapping.
/// </remarks>
public static class EhllapiMnemonics
{
    private static readonly Dictionary<string, string> Map = BuildMap();

    /// <summary>
    /// Converts a parsed keystroke sequence into an EHLLAPI Send Key string.
    /// </summary>
    /// <param name="keystrokes">Parsed keystrokes.</param>
    /// <returns>The EHLLAPI keystroke string.</returns>
    /// <exception cref="NotSupportedException">A mnemonic has no EHLLAPI equivalent.</exception>
    public static string Encode(IReadOnlyList<Keystroke> keystrokes)
    {
        ArgumentNullException.ThrowIfNull(keystrokes);

        var sb = new StringBuilder();
        foreach (var key in keystrokes)
        {
            if (key.Kind == KeystrokeKind.Text)
            {
                sb.Append(EscapeText(key.Value));
                continue;
            }

            if (!Map.TryGetValue(key.Value, out var encoded))
            {
                throw new NotSupportedException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Mnemonic '[{key.Value}]' has no EHLLAPI equivalent. Use the PCOMM provider for this key."));
            }

            sb.Append(encoded);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes literal text for EHLLAPI Send Key: <c>@</c> is the escape character and must be doubled.
    /// </summary>
    /// <param name="text">Literal text.</param>
    /// <returns>The escaped text.</returns>
    public static string EscapeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("@", "@@", StringComparison.Ordinal);
    }

    /// <summary>Gets the canonical mnemonics that the EHLLAPI provider can send.</summary>
    public static IReadOnlyCollection<string> Supported => Map.Keys;

    /// <summary>
    /// Tries to resolve the EHLLAPI escape sequence for a canonical mnemonic.
    /// </summary>
    /// <param name="canonicalName">Canonical mnemonic name, for example "pf3".</param>
    /// <param name="encoded">The EHLLAPI escape sequence when supported.</param>
    /// <returns>True when the mnemonic is supported.</returns>
    public static bool TryGet(string canonicalName, out string? encoded)
    {
        ArgumentNullException.ThrowIfNull(canonicalName);
        return Map.TryGetValue(canonicalName, out encoded);
    }

    private static Dictionary<string, string> BuildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enter"] = "@E",
            ["clear"] = "@C",
            ["tab"] = "@T",
            ["backtab"] = "@B",
            ["home"] = "@0",
            ["end"] = "@q",
            ["delete"] = "@D",
            ["backspace"] = "@<",
            ["insert"] = "@I",
            ["eraseeof"] = "@F",
            ["eraseinput"] = "@A@F",
            ["reset"] = "@R",
            ["attn"] = "@A@Q",
            ["sysreq"] = "@A@H",
            ["left"] = "@L",
            ["right"] = "@Z",
            ["up"] = "@U",
            ["down"] = "@V",
            ["test"] = "@A@C",
        };

        // PF1-PF9 map to @1-@9, PF10-PF24 continue with @a-@o.
        for (var i = 1; i <= 9; i++)
        {
            map[string.Create(CultureInfo.InvariantCulture, $"pf{i}")] = string.Create(CultureInfo.InvariantCulture, $"@{i}");
        }

        for (var i = 10; i <= 24; i++)
        {
            var letter = (char)('a' + (i - 10));
            map[string.Create(CultureInfo.InvariantCulture, $"pf{i}")] = string.Create(CultureInfo.InvariantCulture, $"@{letter}");
        }

        map["pa1"] = "@x";
        map["pa2"] = "@y";
        map["pa3"] = "@z";

        return map;
    }
}
