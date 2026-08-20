using System.Globalization;

namespace HostMcp.Core.Models;

/// <summary>
/// Converts between 1-based row/column coordinates (as used by EHLLAPI and PCOMM) and
/// zero-based linear presentation space offsets.
/// </summary>
/// <remarks>
/// EHLLAPI positions are 1-based and linear: position 1 is row 1 / column 1, and position
/// <c>columns + 1</c> is row 2 / column 1. This helper is the single place where that
/// arithmetic lives so providers and tools never open-code it.
/// </remarks>
public static class CoordinateMapper
{
    /// <summary>
    /// Converts a 1-based row/column pair to a zero-based buffer offset.
    /// </summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="rows">Total rows of the presentation space.</param>
    /// <param name="columns">Total columns of the presentation space.</param>
    /// <returns>The zero-based offset into the flat buffer.</returns>
    public static int ToOffset(int row, int column, int rows, int columns)
    {
        Validate(row, column, rows, columns);
        return ((row - 1) * columns) + (column - 1);
    }

    /// <summary>
    /// Converts a zero-based buffer offset to a 1-based row/column pair.
    /// </summary>
    /// <param name="offset">Zero-based offset into the flat buffer.</param>
    /// <param name="rows">Total rows of the presentation space.</param>
    /// <param name="columns">Total columns of the presentation space.</param>
    /// <returns>The 1-based position.</returns>
    public static CursorPosition ToPosition(int offset, int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, rows * columns);

        return new CursorPosition((offset / columns) + 1, (offset % columns) + 1);
    }

    /// <summary>
    /// Converts a 1-based row/column pair to a 1-based linear EHLLAPI presentation space position.
    /// </summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="rows">Total rows of the presentation space.</param>
    /// <param name="columns">Total columns of the presentation space.</param>
    /// <returns>The 1-based linear position.</returns>
    public static int ToLinearPosition(int row, int column, int rows, int columns)
        => ToOffset(row, column, rows, columns) + 1;

    /// <summary>
    /// Converts a 1-based linear EHLLAPI presentation space position to a 1-based row/column pair.
    /// </summary>
    /// <param name="position">1-based linear position.</param>
    /// <param name="rows">Total rows of the presentation space.</param>
    /// <param name="columns">Total columns of the presentation space.</param>
    /// <returns>The 1-based position.</returns>
    public static CursorPosition FromLinearPosition(int position, int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);
        return ToPosition(position - 1, rows, columns);
    }

    private static void Validate(int row, int column, int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        if (row < 1 || row > rows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                string.Create(CultureInfo.InvariantCulture, $"Row must be between 1 and {rows}."));
        }

        if (column < 1 || column > columns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column),
                column,
                string.Create(CultureInfo.InvariantCulture, $"Column must be between 1 and {columns}."));
        }
    }
}
