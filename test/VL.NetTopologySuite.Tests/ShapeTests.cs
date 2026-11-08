using System;
using System.Linq;
using NetTopologySuite.Geometries;
using VL.Lib.Mathematics;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// Shapes by position and size: anchors read on a map (y north), angles in cycles counter-clockwise
/// from east, counter-clockwise shells, empty in and empty out.
/// </summary>
public class ShapeTests
{
    private static Coordinate At(double x, double y) => new(x, y);

    private static void AssertBox(Geometry g, double minX, double minY, double maxX, double maxY)
    {
        var e = g.EnvelopeInternal;
        Assert.Equal(minX, e.MinX, 9);
        Assert.Equal(minY, e.MinY, 9);
        Assert.Equal(maxX, e.MaxX, 9);
        Assert.Equal(maxY, e.MaxY, 9);
    }

    // ── Rectangle ─────────────────────────────────────────────────────────────

    [Fact]
    public void Rectangle_is_centred_by_default()
    {
        AssertBox(GeometryNodes.Rectangle(At(10, 20), 4, 2), 8, 19, 12, 21);
    }

    [Fact]
    public void Rectangle_TopLeft_is_the_north_west_corner()
    {
        // The whole point of "coordinates in the map's": Top is north, the LARGEST y.
        // In Skia the same anchor would put the box below the position on screen, i.e. at larger y.
        var r = GeometryNodes.Rectangle(At(0, 0), 4, 2, RectangleAnchor.TopLeft);
        AssertBox(r, 0, -2, 4, 0);
    }

    [Theory]
    [InlineData(RectangleAnchor.TopRight, -4, -2, 0, 0)]
    [InlineData(RectangleAnchor.BottomLeft, 0, 0, 4, 2)]
    [InlineData(RectangleAnchor.BottomRight, -4, 0, 0, 2)]
    [InlineData(RectangleAnchor.TopCenter, -2, -2, 2, 0)]
    [InlineData(RectangleAnchor.BottomCenter, -2, 0, 2, 2)]
    [InlineData(RectangleAnchor.MiddleLeft, 0, -1, 4, 1)]
    [InlineData(RectangleAnchor.MiddleRight, -4, -1, 0, 1)]
    public void Rectangle_every_anchor_on_a_map(RectangleAnchor anchor, double minX, double minY, double maxX, double maxY)
    {
        AssertBox(GeometryNodes.Rectangle(At(0, 0), 4, 2, anchor), minX, minY, maxX, maxY);
    }

    [Fact]
    public void Rectangle_is_four_corners_counter_clockwise_and_valid()
    {
        var r = GeometryNodes.Rectangle(At(0, 0), 4, 2);
        Assert.Equal(5, r.NumPoints);
        Assert.True(r.Shell.IsCCW);
        Assert.True(r.IsValid);
        Assert.Equal(8, r.Area, 9);
    }

    [Fact]
    public void Rectangle_takes_a_negative_size_as_its_size()
    {
        AssertBox(GeometryNodes.Rectangle(At(0, 0), -4, -2), -2, -1, 2, 1);
    }

    [Fact]
    public void Rectangle_of_zero_size_or_no_position_is_empty()
    {
        Assert.True(GeometryNodes.Rectangle(At(0, 0), 0, 2).IsEmpty);
        Assert.True(GeometryNodes.Rectangle(null).IsEmpty);
    }

    [Fact]
    public void Rectangle_keeps_the_factory_SRID()
    {
        Assert.Equal(4326, GeometryNodes.Rectangle(At(0, 0), factory: GeometryNodes.GeometryFactory(4326)).SRID);
    }

    // ── Rectangle (Join Points) ───────────────────────────────────────────────

    [Fact]
    public void RectangleJoinPoints_takes_corners_in_any_order()
    {
        AssertBox(GeometryNodes.RectangleJoinPoints(At(4, 0), At(0, 2)), 0, 0, 4, 2);
        Assert.True(GeometryNodes.RectangleJoinPoints(At(4, 0), At(0, 2)).Shell.IsCCW);
    }

    [Fact]
    public void RectangleJoinPoints_on_one_line_is_empty()
    {
        Assert.True(GeometryNodes.RectangleJoinPoints(At(0, 0), At(0, 5)).IsEmpty);
        Assert.True(GeometryNodes.RectangleJoinPoints(At(0, 0), null).IsEmpty);
    }

    // ── Circle and Ellipse ────────────────────────────────────────────────────

    [Fact]
    public void Circle_has_resolution_corners_on_the_radius()
    {
        var c = GeometryNodes.Circle(At(10, 20), 2, 32);
        Assert.Equal(33, c.NumPoints);
        Assert.All(c.Coordinates, p => Assert.Equal(2, p.Distance(At(10, 20)), 9));
        Assert.True(c.Shell.IsCCW);
        Assert.True(c.IsValid);
    }

    [Fact]
    public void Circle_area_approaches_pi_r_squared()
    {
        Assert.Equal(Math.PI * 4, GeometryNodes.Circle(At(0, 0), 2, 256).Area, 1);
    }

    [Fact]
    public void Circle_of_zero_or_negative_radius_is_empty()
    {
        Assert.True(GeometryNodes.Circle(At(0, 0), 0).IsEmpty);
        Assert.True(GeometryNodes.Circle(At(0, 0), -1).IsEmpty);
        Assert.True(GeometryNodes.Circle(null).IsEmpty);
    }

    [Fact]
    public void Ellipse_spans_width_east_west_and_height_south_north()
    {
        var e = GeometryNodes.Ellipse(At(0, 0), 4, 2, 64);
        AssertBox(e, -2, -1, 2, 1);
        Assert.True(e.Shell.IsCCW);
    }

    // ── RegularPolygon ────────────────────────────────────────────────────────

    [Fact]
    public void RegularPolygon_first_corner_points_east()
    {
        var hex = GeometryNodes.RegularPolygon(At(0, 0), 1, 6);
        Assert.Equal(7, hex.NumPoints);
        Assert.Equal(1, hex.Coordinates[0].X, 9);
        Assert.Equal(0, hex.Coordinates[0].Y, 9);
        Assert.True(hex.Shell.IsCCW);
        Assert.True(hex.IsValid);
    }

    [Fact]
    public void RegularPolygon_rotation_is_in_cycles_counter_clockwise()
    {
        // A quarter turn moves the first corner from east to north - north, because y is north.
        var square = GeometryNodes.RegularPolygon(At(0, 0), 1, 4, 0.25);
        Assert.Equal(0, square.Coordinates[0].X, 9);
        Assert.Equal(1, square.Coordinates[0].Y, 9);
    }

    [Fact]
    public void RegularPolygon_takes_at_least_three_sides()
    {
        Assert.Equal(4, GeometryNodes.RegularPolygon(At(0, 0), 1, 1).NumPoints);
    }

    // ── Arc and Sector ────────────────────────────────────────────────────────

    [Fact]
    public void Arc_runs_counter_clockwise_from_east()
    {
        var arc = GeometryNodes.Arc(At(0, 0), 1, 0, 0.25);
        Assert.Equal(1, arc.StartPoint.X, 9);
        Assert.Equal(0, arc.StartPoint.Y, 9);
        Assert.Equal(0, arc.EndPoint.X, 9);
        Assert.Equal(1, arc.EndPoint.Y, 9);
        Assert.All(arc.Coordinates, p => Assert.Equal(1, p.Distance(At(0, 0)), 9));
    }

    [Fact]
    public void Arc_with_a_negative_sweep_runs_clockwise_from_start()
    {
        var arc = GeometryNodes.Arc(At(0, 0), 1, 0, -0.25);
        Assert.Equal(1, arc.StartPoint.X, 9);
        Assert.Equal(0, arc.StartPoint.Y, 9);
        Assert.Equal(0, arc.EndPoint.X, 9);
        Assert.Equal(-1, arc.EndPoint.Y, 9);
    }

    [Fact]
    public void Arc_resolution_is_per_full_turn()
    {
        Assert.Equal(9, GeometryNodes.Arc(At(0, 0), 1, 0, 0.25, 32).NumPoints);
    }

    [Fact]
    public void Arc_of_zero_sweep_is_empty()
    {
        Assert.True(GeometryNodes.Arc(At(0, 0), 1, 0, 0).IsEmpty);
    }

    [Fact]
    public void Sector_is_a_counter_clockwise_slice_from_the_centre()
    {
        var s = GeometryNodes.Sector(At(0, 0), 1, 0, 0.25, 256);
        Assert.True(s.Shell.IsCCW);
        Assert.True(s.IsValid);
        Assert.Contains(s.Coordinates, p => p.Equals2D(At(0, 0)));
        Assert.Equal(Math.PI / 4, s.Area, 2);
        AssertBox(s, 0, 0, 1, 1);
    }

    [Fact]
    public void Sector_with_a_negative_sweep_is_the_slice_below_and_still_counter_clockwise()
    {
        var s = GeometryNodes.Sector(At(0, 0), 1, 0, -0.25, 64);
        Assert.True(s.Shell.IsCCW);
        AssertBox(s, 0, -1, 1, 0);
    }

    // ── Copying ───────────────────────────────────────────────────────────────

    [Fact]
    public void Shapes_do_not_hold_the_position_they_were_given()
    {
        // The package's one guarantee (Defaults, MutationTests): editing the input afterwards
        // changes nothing that was built from it.
        var p = At(0, 0);
        var shapes = new Geometry[]
        {
            GeometryNodes.Rectangle(p), GeometryNodes.Circle(p), GeometryNodes.Ellipse(p),
            GeometryNodes.RegularPolygon(p), GeometryNodes.Arc(p), GeometryNodes.Sector(p),
        };
        var before = shapes.Select(s => s.EnvelopeInternal.Copy()).ToArray();
        p.X = 100;
        Assert.Equal(before, shapes.Select(s => s.EnvelopeInternal));
        Assert.DoesNotContain(shapes.SelectMany(s => s.Coordinates), c => ReferenceEquals(c, p));
    }
}
