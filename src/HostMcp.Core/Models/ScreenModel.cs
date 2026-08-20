using System.Globalization;
using System.Text;

namespace HostMcp.Core.Models;

/// <summary>
/// Structured snapshot of a host presentation space (screen).
/// </summary>
public sealed class ScreenModel
{
    /// <summary>Initializes a new instance of the <see cref="ScreenModel"/> class.</summary>
    /// <param name="rows">Number of rows in the presentation space (for example 24 or 43).</param>
    /// <param name="columns">Number of columns in the presentation space (for example 80 or 132).</param>
    /// <param name="text">
    /// The complete presentation space as a flat character buffer of length rows * columns.
    /// Shorter buffers are space-padded, longer buffers are truncated.
    /// </param>
    /// <param name="cursor">Current cursor position (1-based).</param>
    /// <param name="fields">Field list, or an empty list when the provider cannot enumerate fields.</param>
    /// <param name="oia">Operator Information Area status.</param>
    public ScreenModel(
        int rows,
        int columns,
        string text,
        CursorPosition cursor,
        IReadOnlyList<ScreenField> fields,
        OiaStatus oia)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(oia);

        Rows = rows;
        Columns = columns;
        Buffer = Normalize(text, rows * columns);
        Cursor = cursor;
        Fields = fields;
        Oia = oia;
    }

    /// <summary>Gets the number of rows.</summary>
    public int Rows { get; }

    /// <summary>Gets the number of columns.</summary>
    public int Columns { get; }

    /// <summary>Gets the flat presentation space buffer of exactly <see cref="Rows"/> * <see cref="Columns"/> characters.</summary>
    public string Buffer { get; }

    /// <summary>Gets the current cursor position (1-based).</summary>
    public CursorPosition Cursor { get; }

    /// <summary>Gets the enumerated fields of the screen.</summary>
    public IReadOnlyList<ScreenField> Fields { get; }

    /// <summary>Gets the Operator Information Area status.</summary>
    public OiaStatus Oia { get; }

    /// <summary>Gets the presentation space split into <see cref="Rows"/> lines of <see cref="Columns"/> characters.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var lines = new string[Rows];
            for (var r = 0; r < Rows; r++)
            {
                lines[r] = Buffer.Substring(r * Columns, Columns);
            }

            return lines;
        }
    }

    /// <summary>Gets the presentation space as newline-separated text with trailing blanks removed per line.</summary>
    public string PlainText
    {
        get
        {
            var sb = new StringBuilder(Buffer.Length + Rows);
            for (var r = 0; r < Rows; r++)
            {
                sb.AppendLine(Buffer.Substring(r * Columns, Columns).TrimEnd());
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Reads text from the presentation space at a 1-based coordinate.
    /// </summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <param name="length">Number of characters to read.</param>
    /// <returns>The requested substring of the presentation space.</returns>
    public string GetText(int row, int column, int length)
    {
        var offset = CoordinateMapper.ToOffset(row, column, Rows, Columns);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (offset + length > Buffer.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Reading {length} characters at row {row}, column {column} exceeds the {Rows}x{Columns} presentation space."));
        }

        return Buffer.Substring(offset, length);
    }

    /// <summary>
    /// Finds the first occurrence of <paramref name="needle"/> in the presentation space.
    /// </summary>
    /// <param name="needle">Text to search for.</param>
    /// <param name="ignoreCase">When true, the comparison is case-insensitive.</param>
    /// <returns>The 1-based position of the match, or null when the text is not present.</returns>
    public CursorPosition? Find(string needle, bool ignoreCase = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(needle);

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var index = Buffer.IndexOf(needle, comparison);
        return index < 0 ? null : CoordinateMapper.ToPosition(index, Rows, Columns);
    }

    /// <summary>
    /// Returns the field that contains the given 1-based coordinate, or null when no field covers it.
    /// </summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>The containing field, or null.</returns>
    public ScreenField? FieldAt(int row, int column)
    {
        var offset = CoordinateMapper.ToOffset(row, column, Rows, Columns);
        foreach (var field in Fields)
        {
            var start = CoordinateMapper.ToOffset(field.Row, field.Column, Rows, Columns);
            if (offset >= start && offset < start + field.Length)
            {
                return field;
            }
        }

        return null;
    }

    private static string Normalize(string text, int size)
    {
        if (text.Length == size)
        {
            return text;
        }

        return text.Length > size ? text[..size] : text.PadRight(size, ' ');
    }
}
