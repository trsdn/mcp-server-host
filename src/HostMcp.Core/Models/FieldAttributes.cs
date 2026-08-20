namespace HostMcp.Core.Models;

/// <summary>
/// Identifies the field attribute characteristics of a 3270/5250 screen field.
/// </summary>
[Flags]
public enum FieldAttributes
{
    /// <summary>No special attributes: an unprotected, alphanumeric, displayable field.</summary>
    None = 0,

    /// <summary>Field is protected (read-only); the host rejects keystrokes typed into it.</summary>
    Protected = 1,

    /// <summary>Field accepts numeric input only.</summary>
    Numeric = 2,

    /// <summary>Field is non-display (hidden), typically used for password entry.</summary>
    Hidden = 4,

    /// <summary>Field is displayed with high intensity.</summary>
    Intensified = 8,

    /// <summary>Field is pen-detectable / selectable.</summary>
    PenDetectable = 16,

    /// <summary>Field content has been modified since the last host read (MDT bit set).</summary>
    Modified = 32,
}
