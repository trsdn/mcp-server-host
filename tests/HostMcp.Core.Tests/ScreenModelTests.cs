using HostMcp.Core.Models;
using HostMcp.Core.Providers;
using Xunit;

namespace HostMcp.Core.Tests;

public class ScreenModelTests
{
    private static ScreenModel Build(params string[] lines)
        => new(
            24,
            80,
            FakeTerminalProvider.BuildBuffer(24, 80, lines),
            new CursorPosition(1, 1),
            [],
            OiaStatus.Ready);

    [Fact]
    public void Constructor_PadsShortBufferToScreenSize()
    {
        var screen = new ScreenModel(2, 10, "ABC", new CursorPosition(1, 1), [], OiaStatus.Ready);

        Assert.Equal(20, screen.Buffer.Length);
        Assert.Equal("ABC       ", screen.Lines[0]);
        Assert.Equal("          ", screen.Lines[1]);
    }

    [Fact]
    public void Constructor_TruncatesOversizedBuffer()
    {
        var screen = new ScreenModel(1, 4, "ABCDEFGH", new CursorPosition(1, 1), [], OiaStatus.Ready);

        Assert.Equal("ABCD", screen.Buffer);
    }

    [Fact]
    public void Lines_SplitBufferIntoFixedWidthRows()
    {
        var screen = Build("SIGN ON", "USERID");

        Assert.Equal(24, screen.Lines.Count);
        Assert.All(screen.Lines, line => Assert.Equal(80, line.Length));
        Assert.StartsWith("SIGN ON", screen.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void PlainText_TrimsTrailingBlanksPerLine()
    {
        var screen = Build("SIGN ON", "USERID");
        var lines = screen.PlainText.Split(Environment.NewLine);

        Assert.Equal("SIGN ON", lines[0]);
        Assert.Equal("USERID", lines[1]);
        Assert.Equal(string.Empty, lines[2]);
    }

    [Fact]
    public void GetText_ReadsAtOneBasedCoordinates()
    {
        var screen = Build("SIGN ON");

        Assert.Equal("SIGN", screen.GetText(1, 1, 4));
        Assert.Equal("ON", screen.GetText(1, 6, 2));
    }

    [Fact]
    public void GetText_BeyondBuffer_Throws()
    {
        var screen = Build("SIGN ON");

        Assert.Throws<ArgumentOutOfRangeException>(() => screen.GetText(24, 79, 10));
    }

    [Fact]
    public void Find_ReturnsOneBasedPositionOfFirstMatch()
    {
        var screen = Build("SIGN ON", "  PASSWORD");

        var position = screen.Find("PASSWORD");

        Assert.NotNull(position);
        Assert.Equal(2, position!.Row);
        Assert.Equal(3, position.Column);
    }

    [Fact]
    public void Find_IsCaseSensitiveByDefault()
    {
        var screen = Build("SIGN ON");

        Assert.Null(screen.Find("sign"));
        Assert.NotNull(screen.Find("sign", ignoreCase: true));
    }

    [Fact]
    public void Find_MissingText_ReturnsNull() => Assert.Null(Build("SIGN ON").Find("LOGOFF"));

    [Fact]
    public void FieldAt_ResolvesTheContainingField()
    {
        var fields = new List<ScreenField>
        {
            new(0, 1, 1, 7, FieldAttributes.Protected, "SIGN ON"),
            new(1, 2, 10, 8, FieldAttributes.None, "        "),
            new(2, 3, 10, 8, FieldAttributes.Hidden, "        "),
        };

        var screen = new ScreenModel(
            24,
            80,
            FakeTerminalProvider.BuildBuffer(24, 80, "SIGN ON"),
            new CursorPosition(1, 1),
            fields,
            OiaStatus.Ready);

        Assert.Equal(0, screen.FieldAt(1, 3)!.Index);
        Assert.Equal(1, screen.FieldAt(2, 17)!.Index);
        Assert.Null(screen.FieldAt(2, 18));
        Assert.True(screen.FieldAt(1, 3)!.IsProtected);
        Assert.True(screen.FieldAt(3, 10)!.IsHidden);
    }

    [Fact]
    public void ScreenField_TrimmedText_RemovesPadding()
    {
        var field = new ScreenField(0, 1, 1, 8, FieldAttributes.None, "AB      ");

        Assert.Equal("AB", field.TrimmedText);
    }

    [Fact]
    public void ScreenField_AttributeFlagsAreIndependent()
    {
        var field = new ScreenField(0, 1, 1, 8, FieldAttributes.Numeric | FieldAttributes.Hidden, "        ");

        Assert.False(field.IsProtected);
        Assert.True(field.IsNumeric);
        Assert.True(field.IsHidden);
    }
}
