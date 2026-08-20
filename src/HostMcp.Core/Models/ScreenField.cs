namespace HostMcp.Core.Models;

/// <summary>
/// A single field on a host presentation space, described by 1-based row/column coordinates.
/// </summary>
/// <param name="Index">Zero-based index of the field within the screen's field list.</param>
/// <param name="Row">1-based row of the first character position of the field.</param>
/// <param name="Column">1-based column of the first character position of the field.</param>
/// <param name="Length">Length of the field in character positions.</param>
/// <param name="Attributes">Field attribute flags (protected, numeric, hidden, ...).</param>
/// <param name="Text">Current field content, space padded to <paramref name="Length"/>.</param>
public sealed record ScreenField(
    int Index,
    int Row,
    int Column,
    int Length,
    FieldAttributes Attributes,
    string Text)
{
    /// <summary>Gets a value indicating whether the field is protected (read-only).</summary>
    public bool IsProtected => Attributes.HasFlag(FieldAttributes.Protected);

    /// <summary>Gets a value indicating whether the field accepts numeric input only.</summary>
    public bool IsNumeric => Attributes.HasFlag(FieldAttributes.Numeric);

    /// <summary>Gets a value indicating whether the field is non-display (hidden).</summary>
    public bool IsHidden => Attributes.HasFlag(FieldAttributes.Hidden);

    /// <summary>Gets the field content with trailing padding removed.</summary>
    public string TrimmedText => Text.TrimEnd();
}
