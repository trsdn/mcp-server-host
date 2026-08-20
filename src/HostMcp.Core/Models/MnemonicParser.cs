using System.Globalization;
using System.Text;

namespace HostMcp.Core.Models;

/// <summary>
/// The kind of a parsed keystroke token.
/// </summary>
public enum KeystrokeKind
{
    /// <summary>Literal text to be typed into the presentation space.</summary>
    Text,

    /// <summary>An AID or control key mnemonic such as [enter] or [pf3].</summary>
    Mnemonic,
}

/// <summary>
/// A single token in a keystroke sequence: either literal text or a control mnemonic.
/// </summary>
/// <param name="Kind">Whether the token is literal text or a mnemonic.</param>
/// <param name="Value">
/// For <see cref="KeystrokeKind.Text"/> the literal characters, for
/// <see cref="KeystrokeKind.Mnemonic"/> the normalized mnemonic name without brackets (for example "enter", "pf3").
/// </param>
public sealed record Keystroke(KeystrokeKind Kind, string Value)
{
    /// <summary>Creates a literal text keystroke.</summary>
    /// <param name="text">The literal characters.</param>
    /// <returns>A text keystroke.</returns>
    public static Keystroke Text(string text) => new(KeystrokeKind.Text, text);

    /// <summary>Creates a mnemonic keystroke.</summary>
    /// <param name="name">Normalized mnemonic name without brackets.</param>
    /// <returns>A mnemonic keystroke.</returns>
    public static Keystroke Mnemonic(string name) => new(KeystrokeKind.Mnemonic, name);

    /// <summary>Gets a value indicating whether this mnemonic submits the screen to the host (an AID key).</summary>
    public bool IsAidKey => Kind == KeystrokeKind.Mnemonic && MnemonicParser.IsAidKey(Value);
}

/// <summary>
/// Parses EHLLAPI/PCOMM style keystroke strings into a token stream.
/// </summary>
/// <remarks>
/// Supported syntax: literal text mixed with bracketed mnemonics, for example
/// <c>USER01[tab]SECRET[enter]</c>. Mnemonic names are case-insensitive. A literal
/// opening bracket is written as <c>[[</c>.
/// </remarks>
public static class MnemonicParser
{
    /// <summary>
    /// Mnemonics recognized by the parser, mapped to their canonical name.
    /// Aliases (for example "pf3" and "f3") resolve to the same canonical mnemonic.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    /// <summary>Mnemonics that transmit the screen to the host and therefore lock the keyboard.</summary>
    private static readonly HashSet<string> AidKeys = BuildAidKeys();

    /// <summary>
    /// Gets the canonical names of all supported mnemonics, in a stable sorted order.
    /// </summary>
    /// <returns>The supported mnemonic names, without brackets.</returns>
    public static IReadOnlyList<string> SupportedMnemonics { get; } =
        [.. Aliases.Values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal)];

    /// <summary>
    /// Parses a keystroke string into an ordered token stream.
    /// </summary>
    /// <param name="input">Keystroke string, for example <c>USER01[tab]SECRET[enter]</c>.</param>
    /// <returns>The parsed tokens, in order.</returns>
    /// <exception cref="FormatException">The input contains an unterminated or unknown mnemonic.</exception>
    public static IReadOnlyList<Keystroke> Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tokens = new List<Keystroke>();
        var literal = new StringBuilder();

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c != '[')
            {
                literal.Append(c);
                continue;
            }

            // "[[" is an escaped literal bracket.
            if (i + 1 < input.Length && input[i + 1] == '[')
            {
                literal.Append('[');
                i++;
                continue;
            }

            var close = input.IndexOf(']', i + 1);
            if (close < 0)
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Unterminated mnemonic starting at index {i} in '{input}'."));
            }

            var raw = input.Substring(i + 1, close - i - 1);
            var name = Normalize(raw);

            if (literal.Length > 0)
            {
                tokens.Add(Keystroke.Text(literal.ToString()));
                literal.Clear();
            }

            tokens.Add(Keystroke.Mnemonic(name));
            i = close;
        }

        if (literal.Length > 0)
        {
            tokens.Add(Keystroke.Text(literal.ToString()));
        }

        return tokens;
    }

    /// <summary>
    /// Normalizes a raw mnemonic name to its canonical form.
    /// </summary>
    /// <param name="raw">Raw mnemonic name without brackets, for example "F3" or "PF3".</param>
    /// <returns>The canonical mnemonic name, for example "pf3".</returns>
    /// <exception cref="FormatException">The mnemonic is not recognized.</exception>
    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var key = raw.Trim().ToLowerInvariant();
        if (Aliases.TryGetValue(key, out var canonical))
        {
            return canonical;
        }

        throw new FormatException(
            string.Create(CultureInfo.InvariantCulture, $"Unknown mnemonic '[{raw}]'. Supported: {string.Join(", ", SupportedMnemonics)}."));
    }

    /// <summary>
    /// Determines whether a canonical mnemonic transmits the screen to the host.
    /// </summary>
    /// <param name="canonicalName">Canonical mnemonic name, for example "enter".</param>
    /// <returns>True when the mnemonic is an AID key.</returns>
    public static bool IsAidKey(string canonicalName)
    {
        ArgumentNullException.ThrowIfNull(canonicalName);
        return AidKeys.Contains(canonicalName);
    }

    /// <summary>
    /// Determines whether a keystroke sequence contains at least one AID key.
    /// </summary>
    /// <param name="keystrokes">Parsed keystrokes.</param>
    /// <returns>True when the sequence submits to the host.</returns>
    public static bool ContainsAidKey(IEnumerable<Keystroke> keystrokes)
    {
        ArgumentNullException.ThrowIfNull(keystrokes);
        return keystrokes.Any(k => k.IsAidKey);
    }

    /// <summary>
    /// Converts a token stream back to the canonical bracket notation.
    /// </summary>
    /// <param name="keystrokes">Parsed keystrokes.</param>
    /// <returns>The canonical keystroke string.</returns>
    public static string ToCanonicalString(IEnumerable<Keystroke> keystrokes)
    {
        ArgumentNullException.ThrowIfNull(keystrokes);

        var sb = new StringBuilder();
        foreach (var k in keystrokes)
        {
            if (k.Kind == KeystrokeKind.Mnemonic)
            {
                sb.Append('[').Append(k.Value).Append(']');
            }
            else
            {
                sb.Append(k.Value.Replace("[", "[[", StringComparison.Ordinal));
            }
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> BuildAliases()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string canonical, params string[] aliases)
        {
            map[canonical] = canonical;
            foreach (var alias in aliases)
            {
                map[alias] = canonical;
            }
        }

        Add("enter", "newline");
        Add("clear");
        Add("tab", "ft");
        Add("backtab", "bt");
        Add("home");
        Add("end", "eof");
        Add("eraseeof", "erase_eof");
        Add("eraseinput", "erase_input");
        Add("delete", "del");
        Add("backspace", "bs");
        Add("insert", "ins");
        Add("reset");
        Add("attn");
        Add("sysreq");
        Add("up");
        Add("down");
        Add("left");
        Add("right");
        Add("test");

        for (var i = 1; i <= 24; i++)
        {
            var pf = string.Create(CultureInfo.InvariantCulture, $"pf{i}");
            Add(pf, string.Create(CultureInfo.InvariantCulture, $"f{i}"), string.Create(CultureInfo.InvariantCulture, $"pf{i:00}"));
        }

        for (var i = 1; i <= 3; i++)
        {
            Add(string.Create(CultureInfo.InvariantCulture, $"pa{i}"));
        }

        return map;
    }

    private static HashSet<string> BuildAidKeys()
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { "enter", "clear", "attn", "sysreq" };
        for (var i = 1; i <= 24; i++)
        {
            set.Add(string.Create(CultureInfo.InvariantCulture, $"pf{i}"));
        }

        for (var i = 1; i <= 3; i++)
        {
            set.Add(string.Create(CultureInfo.InvariantCulture, $"pa{i}"));
        }

        return set;
    }
}
