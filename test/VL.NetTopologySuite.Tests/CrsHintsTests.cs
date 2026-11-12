using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The short SRID table GeometryEditor's window uses: lon/lat codes are degrees, the common grids
/// are metres, and anything else says it does not know rather than guessing.
/// </summary>
public class CrsHintsTests
{
    [Theory]
    [InlineData(4326)] [InlineData(4258)] [InlineData(4490)] [InlineData(6668)] [InlineData(4979)]
    public void Geographic_codes_are_degrees(int srid)
        => Assert.Equal(CrsHints.Units.Degrees, CrsHints.UnitsOf(srid));

    [Theory]
    [InlineData(3857)] [InlineData(28992)] [InlineData(7415)] [InlineData(32631)] [InlineData(32755)] [InlineData(25832)] [InlineData(6677)]
    public void Projected_grids_are_metres(int srid)
        => Assert.Equal(CrsHints.Units.Metres, CrsHints.UnitsOf(srid));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(2263)] [InlineData(123456)]
    public void Anything_else_is_unknown(int srid)
        => Assert.Equal(CrsHints.Units.Unknown, CrsHints.UnitsOf(srid));

    [Fact]
    public void Labels_say_the_units()
    {
        Assert.Equal("SRID unset", CrsHints.Label(0));
        Assert.Equal("EPSG:4326 · degrees", CrsHints.Label(4326));
        Assert.Equal("EPSG:28992 · metres", CrsHints.Label(28992));
        Assert.Equal("EPSG:2263 · units unknown", CrsHints.Label(2263)); // NY state plane is in US feet
    }
}