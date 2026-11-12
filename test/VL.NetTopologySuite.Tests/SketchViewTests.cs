using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// GeometryEditor's window view: y north in the geometry, y down on screen, a fitted shape fills
/// 80% of the window, and zoom keeps the point under the cursor still.
/// </summary>
public class SketchViewTests
{
    private static readonly Vector2 Window = new(800, 600);

    [Fact]
    public void North_is_up_on_screen()
    {
        var v = new SketchView(0, 0, 100);
        Assert.True(v.ToPixels(new Coordinate(0, 1), Window).Y < v.ToPixels(new Coordinate(0, 0), Window).Y);
    }

    [Fact]
    public void Pixels_and_geometry_round_trip()
    {
        var v = new SketchView(3, -2, 37.5);
        var c = v.ToGeometry(new Vector2(123, 456), Window);
        var p = v.ToPixels(c, Window);
        Assert.Equal(123, p.X, 3);
        Assert.Equal(456, p.Y, 3);
    }

    [Fact]
    public void A_fitted_shape_fills_80_percent_of_the_limiting_side()
    {
        var v = SketchView.Fit(new Envelope(0, 10, 0, 10), Window);
        Assert.Equal(600 * SketchView.Fill / 10, v.PixelsPerUnit, 6);
        Assert.Equal(5, v.CentreX, 9);
        Assert.Equal(5, v.CentreY, 9);
    }

    [Fact]
    public void A_single_point_and_nothing_both_get_a_usable_scale()
    {
        Assert.True(SketchView.Fit(new Envelope(new Coordinate(4, 4)), Window).PixelsPerUnit > 0);
        Assert.True(SketchView.Fit(null, Window).PixelsPerUnit > 0);
    }

    [Fact]
    public void Zoom_keeps_the_point_under_the_cursor()
    {
        var v = new SketchView(0, 0, 50);
        var cursor = new Vector2(600, 100);
        var before = v.ToGeometry(cursor, Window);
        var after = v.ZoomAt(cursor, Window, 1.7).ToGeometry(cursor, Window);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
    }

    [Fact]
    public void Pan_moves_the_content_with_the_mouse()
    {
        var v = new SketchView(0, 0, 50);
        var c = new Coordinate(1, 1);
        var p0 = v.ToPixels(c, Window);
        var p1 = v.Pan(new Vector2(30, -20)).ToPixels(c, Window);
        Assert.Equal(p0.X + 30, p1.X, 3);
        Assert.Equal(p0.Y - 20, p1.Y, 3);
    }

    [Theory]
    [InlineData(100, 40, 0.5)]
    [InlineData(10, 40, 5)]
    [InlineData(1, 40, 50)]
    public void The_grid_step_is_1_2_or_5_times_a_power_of_ten(double pixelsPerUnit, double minPixels, double expected)
        => Assert.Equal(expected, new SketchView(0, 0, pixelsPerUnit).GridStep(minPixels), 9);
    [Fact]
    public void On_lon_lat_a_square_block_is_drawn_square()
    {
        // 0.01 degrees of longitude at 52 N is 0.00616 degrees' worth of northward distance.
        var lat = 52.0;
        var k = System.Math.Cos(lat * System.Math.PI / 180);
        var block = new Envelope(4.36, 4.36 + 0.01, lat, lat + 0.01 * k);
        var v = SketchView.Fit(block, Window, degrees: true);
        var a = v.ToPixels(new Coordinate(block.MinX, block.MinY), Window);
        var b = v.ToPixels(new Coordinate(block.MaxX, block.MaxY), Window);
        Assert.Equal(System.Math.Abs(b.X - a.X), System.Math.Abs(b.Y - a.Y), 1);
    }

    [Fact]
    public void Metres_are_not_shrunk()
        => Assert.Equal(1, SketchView.Fit(new Envelope(0, 10, 52, 62), Window).XScale);

    [Fact]
    public void Lon_lat_round_trips_and_pans_with_the_shrink()
    {
        var v = SketchView.Fit(new Envelope(4.3, 4.4, 52.0, 52.1), Window, degrees: true);
        Assert.True(v.XScale < 0.7);
        var c = v.ToGeometry(new Vector2(123, 456), Window);
        var p = v.ToPixels(c, Window);
        Assert.Equal(123, p.X, 3);
        Assert.Equal(456, p.Y, 3);
        var panned = v.Pan(new Vector2(50, 0));
        Assert.Equal(p.X + 50, panned.ToPixels(c, Window).X, 3);
    }

    [Fact]
    public void Near_a_pole_the_shrink_stops_at_a_twentieth()
        => Assert.Equal(0.05, SketchView.XScaleAt(90), 9);
}