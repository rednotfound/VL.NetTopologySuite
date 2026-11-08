using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// Vector2, VL's own position: the way in keeps X and Y as they are (no flip) and loses nothing;
/// the precision a Float32 would lose on a map coordinate is why there is no way back.
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
    public void Coordinate_from_Vector2_loses_nothing()
    {
        // Every Float32 is exactly a double, even one as large as a Web Mercator easting.
        var v = new Vector2(15_550_000f, 0.1f);
        var c = GeometryNodes.CoordinateFromVector2(v);
        Assert.Equal((double)v.X, c.X);
        Assert.Equal((double)v.Y, c.Y);
    }

    [Fact]
    public void A_map_coordinate_as_Float32_is_a_metre_coarse()
    {
        // The measurement behind keeping positions as Coordinate and having no ToVector2
        // (2026-11-08): a Web Mercator easting near the date line, held to the metre, the .123 gone.
        var f = (float)15_550_000.123;
        Assert.Equal(15_550_000f, f);
        Assert.NotEqual(15_550_000.123, (double)f);
    }
}
