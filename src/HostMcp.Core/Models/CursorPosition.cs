namespace HostMcp.Core.Models;

/// <summary>
/// A 1-based cursor position on the presentation space.
/// </summary>
/// <param name="Row">1-based row.</param>
/// <param name="Column">1-based column.</param>
public sealed record CursorPosition(int Row, int Column);
