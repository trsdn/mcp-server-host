using HostMcp.Core.Models;
using Xunit;

namespace HostMcp.Core.Tests;

public class CoordinateMapperTests
{
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1, 80, 79)]
    [InlineData(2, 1, 80)]
    [InlineData(24, 80, 1919)]
    public void ToOffset_MapsOneBasedCoordinates(int row, int column, int expected)
        => Assert.Equal(expected, CoordinateMapper.ToOffset(row, column, 24, 80));

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(79, 1, 80)]
    [InlineData(80, 2, 1)]
    [InlineData(1919, 24, 80)]
    public void ToPosition_IsTheInverseOfToOffset(int offset, int row, int column)
    {
        var position = CoordinateMapper.ToPosition(offset, 24, 80);

        Assert.Equal(row, position.Row);
        Assert.Equal(column, position.Column);
        Assert.Equal(offset, CoordinateMapper.ToOffset(position.Row, position.Column, 24, 80));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 80, 80)]
    [InlineData(2, 1, 81)]
    [InlineData(24, 80, 1920)]
    public void ToLinearPosition_IsOneBased(int row, int column, int expected)
        => Assert.Equal(expected, CoordinateMapper.ToLinearPosition(row, column, 24, 80));

    [Fact]
    public void FromLinearPosition_RoundTrips()
    {
        for (var position = 1; position <= 1920; position += 137)
        {
            var mapped = CoordinateMapper.FromLinearPosition(position, 24, 80);
            Assert.Equal(position, CoordinateMapper.ToLinearPosition(mapped.Row, mapped.Column, 24, 80));
        }
    }

    [Fact]
    public void Works_ForWideAndTallScreens()
    {
        Assert.Equal(132, CoordinateMapper.ToOffset(2, 1, 43, 132));
        Assert.Equal(5675, CoordinateMapper.ToOffset(43, 132, 43, 132));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 81)]
    public void ToOffset_RejectsOutOfRangeCoordinates(int row, int column)
        => Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateMapper.ToOffset(row, column, 24, 80));

    [Fact]
    public void FromLinearPosition_RejectsZero()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateMapper.FromLinearPosition(0, 24, 80));
}
