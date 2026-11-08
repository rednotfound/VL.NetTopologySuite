using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// Vector2, VL's own position: the conversion keeps X and Y as they are (no flip), and the
/// precision a Float32 loses on a map coordinate is real and documented, not hidden.
/// </summary>
public class Vector2Tests
{
    [Fact]
    public void Coordinate_from_Vector2_keeps_X_and_Y()
    {
        var c = GeometryNodes.CoordinateFromVector2(new Vector2(3.5f, -2f));
        Assert.Equal(3.5, c.X);
        Assert.Equal(-2, c.Y);
        Assert.True(double.IsNaN(c.Z));
    }

    [Fact]
    public void ToVector2_keeps_X_and_Y_and_does_not_flip()
    {
        // Y stays Y: north on a map is up on a y-up screen and down on Skia's, and which one is
        // the renderer's business, not this package's.
        Assert.Equal(new Vector2(1, 2), GeometryNodes.ToVector2(new Coordinate(1, 2)));
    }

    [Fact]
    public void ToVector2_of_nothing_is_zero()
    {
        Assert.Equal(Vector2.Zero, GeometryNodes.ToVector2(null));
    }

    [Fact]
    public void ToVector2_drops_Z()
    {
        Assert.Equal(new Vector2(1, 2), GeometryNodes.ToVector2(new CoordinateZ(1, 2, 3)));
    }

    [Fact]
    public void A_map_coordinate_through_Vector2_loses_about_a_metre()
    {
        // The measurement the remarks rest on (2026-11-08). A Web Mercator easting near the date
        // line: Float32 holds it to the metre, so the .123 is gone.
        var back = GeometryNodes.CoordinateFromVector2(GeometryNodes.ToVector2(new Coordinate(15_550_000.123, 4_200_000.5)));
        Assert.Equal(15_550_000, back.X);
        Assert.NotEqual(15_550_000.123, back.X);
    }
}
