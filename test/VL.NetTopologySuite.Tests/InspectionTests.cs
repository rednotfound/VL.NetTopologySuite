using VL.NetTopologySuite;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// Inspection against known values, plus what every node does with an unconnected pin.
/// </summary>
public class InspectionTests
{
    [Fact]
    public void Unit_square_measures_what_a_unit_square_measures()
    {
        var square = MutationTests.UnitSquare();

        Assert.Equal(1.0, GeometryNodes.Area(square), 12);
        Assert.Equal(4.0, GeometryNodes.Length(square), 12);
        Assert.Equal(0.5, GeometryNodes.Centroid(square).X, 12);
        Assert.Equal(0.5, GeometryNodes.Centroid(square).Y, 12);
    }

    [Fact]
    public void A_polygons_length_is_its_perimeter_including_holes()
    {
        var shell = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(4, 0),
            GeometryNodes.Coordinate(4, 4),
            GeometryNodes.Coordinate(0, 4),
        ]);
        var hole = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(2, 1),
            GeometryNodes.Coordinate(2, 2),
            GeometryNodes.Coordinate(1, 2),
        ]);

        // 16 around the outside, 4 around the hole. Documented on the node; asserted here.
        Assert.Equal(20.0, GeometryNodes.Length(GeometryNodes.Polygon(shell, [hole])), 12);
    }

    [Fact]
    public void GeometryType_uses_the_OGC_names()
    {
        Assert.Equal("Point", GeometryNodes.GeometryType(GeometryNodes.Point(GeometryNodes.Coordinate(0, 0))));
        Assert.Equal("Polygon", GeometryNodes.GeometryType(MutationTests.UnitSquare()));
        Assert.Equal("LineString", GeometryNodes.GeometryType(
            GeometryNodes.LineString([GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(1, 1)])));
        Assert.Equal("LinearRing", GeometryNodes.GeometryType(GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
        ])));
    }

    [Fact]
    public void Bounds_are_the_bounding_box()
    {
        GeometryNodes.Bounds(MutationTests.UnitSquare(), out var minX, out var minY, out var maxX, out var maxY);

        Assert.Equal(0, minX);
        Assert.Equal(0, minY);
        Assert.Equal(1, maxX);
        Assert.Equal(1, maxY);
    }

    [Fact]
    public void Bounds_of_an_empty_geometry_are_zeros_not_an_exception()
    {
        GeometryNodes.Bounds(GeometryNodes.Polygon(), out var minX, out var minY, out var maxX, out var maxY);

        Assert.Equal(0, minX);
        Assert.Equal(0, minY);
        Assert.Equal(0, maxX);
        Assert.Equal(0, maxY);
    }

    [Fact]
    public void Coordinates_of_a_ring_repeat_the_first_at_the_end()
    {
        // Because that is what the ring actually holds. Documented on the node, asserted here so a
        // future "tidy-up" that drops it fails loudly.
        var ring = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
        ]);

        var coordinates = GeometryNodes.Coordinates(ring);
        Assert.Equal(4, coordinates.Count);
        Assert.Equal(coordinates[0], coordinates[^1]);
    }

    // ── Validity ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_unit_square_is_valid_and_says_nothing_more()
    {
        Assert.True(GeometryNodes.IsValid(MutationTests.UnitSquare(), out var reason, out var location));
        Assert.Equal("", reason);
        Assert.Null(location);
    }

    [Fact]
    public void A_self_crossing_ring_builds_fine_and_is_invalid()
    {
        // The point of the test: closure is checked at construction, validity is not. A bow-tie
        // closes perfectly.
        var bowtie = GeometryNodes.Polygon(GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(2, 2),
            GeometryNodes.Coordinate(2, 0),
            GeometryNodes.Coordinate(0, 2),
        ]));

        Assert.False(GeometryNodes.IsValid(bowtie, out var reason, out var location));
        Assert.Equal("Self-intersection", reason);
        Assert.NotNull(location);
        Assert.Equal(1, location!.X, 12);
        Assert.Equal(1, location.Y, 12);
    }

    [Fact]
    public void A_hole_outside_its_shell_is_invalid_and_says_so()
    {
        var shell = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(0, 1),
        ]);
        var hole = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(10, 10),
            GeometryNodes.Coordinate(11, 10),
            GeometryNodes.Coordinate(11, 11),
            GeometryNodes.Coordinate(10, 11),
        ]);

        Assert.False(GeometryNodes.IsValid(GeometryNodes.Polygon(shell, [hole]), out var reason, out _));
        Assert.Contains("Hole", reason);
    }

    [Fact]
    public void An_empty_geometry_is_valid_and_so_is_nothing_at_all()
    {
        // Both surprising enough to pin down: a patch checking IsValid on an unconnected pin gets
        // true, so IsValid is not a substitute for IsEmpty.
        Assert.True(GeometryNodes.IsValid(GeometryNodes.Polygon(), out _, out _));
        Assert.True(GeometryNodes.IsValid(null, out _, out _));
    }

    // ── Unconnected pins ──────────────────────────────────────────────────────

    [Fact]
    public void Every_inspection_node_survives_an_unconnected_pin()
    {
        Assert.Equal("", GeometryNodes.GeometryType(null));
        Assert.True(GeometryNodes.IsEmpty(null));
        Assert.Equal(0, GeometryNodes.SRID(null));
        Assert.Equal(0, GeometryNodes.Area(null));
        Assert.Equal(0, GeometryNodes.Length(null));
        Assert.True(GeometryNodes.Centroid(null).IsEmpty);
        Assert.Empty(GeometryNodes.Coordinates(null));
        Assert.Empty(GeometryNodes.Geometries(null));
    }

    [Fact]
    public void IsEmpty_is_true_for_a_geometry_with_no_coordinates()
    {
        Assert.True(GeometryNodes.IsEmpty(GeometryNodes.Point()));
        Assert.False(GeometryNodes.IsEmpty(MutationTests.UnitSquare()));
    }

    // ── Tier 1: the rest of Geometry's own surface (2026-11-07) ───────────────

    [Fact]
    public void Envelope_is_the_bounding_box_as_a_geometry_and_degrades_for_flat_input()
    {
        var triangle = GeometryNodes.Polygon(GeometryNodes.LinearRing(
            [GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(2, 0), GeometryNodes.Coordinate(1, 1)]));

        var box = GeometryNodes.Envelope(triangle);
        Assert.Equal("Polygon", GeometryNodes.GeometryType(box));
        Assert.Equal(2.0, GeometryNodes.Area(box), 12);
        GeometryNodes.Bounds(triangle, out var minX, out var minY, out var maxX, out var maxY);
        GeometryNodes.Bounds(box, out var bMinX, out var bMinY, out var bMaxX, out var bMaxY);
        Assert.Equal((minX, minY, maxX, maxY), (bMinX, bMinY, bMaxX, bMaxY));

        Assert.Equal("Point", GeometryNodes.GeometryType(GeometryNodes.Envelope(GeometryNodes.Point(GeometryNodes.Coordinate(3, 4)))));
        var flat = GeometryNodes.LineString([GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(5, 0)]);
        Assert.Equal("LineString", GeometryNodes.GeometryType(GeometryNodes.Envelope(flat)));
        Assert.True(GeometryNodes.IsEmpty(GeometryNodes.Envelope(null)));
    }

    [Fact]
    public void InteriorPoint_lies_on_the_shape_where_the_centroid_may_not()
    {
        // A U: the centroid falls in the gap, the interior point does not.
        var u = GeometryNodes.Polygon(GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(3, 0), GeometryNodes.Coordinate(3, 3),
            GeometryNodes.Coordinate(2, 3), GeometryNodes.Coordinate(2, 1), GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(1, 3), GeometryNodes.Coordinate(0, 3),
        ]));

        Assert.False(OperationNodes.Covers(u, GeometryNodes.Centroid(u)));
        Assert.True(OperationNodes.Covers(u, GeometryNodes.InteriorPoint(u)));
        Assert.True(GeometryNodes.IsEmpty(GeometryNodes.InteriorPoint(null)));
        Assert.True(GeometryNodes.IsEmpty(GeometryNodes.InteriorPoint(GeometryNodes.Point())));
    }

    [Fact]
    public void InteriorPoint_of_a_point_does_not_share_the_input_coordinate()
    {
        // Measured 2026-11-07 against the expectation that NTS would reuse the input's Coordinate
        // instance for a Point: it does not, so the node copies nothing. This is what tells us if
        // that changes - then the node needs Defaults.CopyOf like IsValid's location.
        var point = GeometryNodes.Point(GeometryNodes.Coordinate(1, 2));
        Assert.NotSame(point.Coordinate, point.InteriorPoint.Coordinate);

        var ours = GeometryNodes.InteriorPoint(point);
        Assert.NotSame(point.Coordinate, ours.Coordinate);
        Assert.Equal(1, ours.X);
        Assert.Equal(2, ours.Y);
        ours.Coordinate.X = 99;
        Assert.Equal(1, point.X);
    }

    [Fact]
    public void Boundary_is_one_dimension_down()
    {
        var square = MutationTests.UnitSquare();
        var outline = GeometryNodes.Boundary(square);
        Assert.Equal(4.0, GeometryNodes.Length(outline), 12);
        Assert.Equal(0.0, GeometryNodes.Area(outline), 12);

        var line = GeometryNodes.LineString([GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(2, 0)]);
        var ends = GeometryNodes.Boundary(line);
        Assert.Equal("MultiPoint", GeometryNodes.GeometryType(ends));
        Assert.Equal(2, GeometryNodes.NumPoints(ends));

        Assert.True(GeometryNodes.IsEmpty(GeometryNodes.Boundary(GeometryNodes.Point(GeometryNodes.Coordinate(1, 1)))));
        Assert.True(GeometryNodes.IsEmpty(GeometryNodes.Boundary(null)));
    }

    [Fact]
    public void Outlines_gives_each_ring_and_line_separately_and_copies()
    {
        var shell = GeometryNodes.LinearRing(
            [GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(4, 0), GeometryNodes.Coordinate(4, 3), GeometryNodes.Coordinate(0, 3)]);
        var hole = GeometryNodes.LinearRing(
            [GeometryNodes.Coordinate(1, 1), GeometryNodes.Coordinate(2, 1), GeometryNodes.Coordinate(2, 2), GeometryNodes.Coordinate(1, 2)]);
        var withHole = GeometryNodes.Polygon(shell, [hole]);

        GeometryNodes.Outlines(withHole, out var rings, out var lines, out var points);
        Assert.Equal(2, rings.Count);
        Assert.Equal(5, rings[0].Count);   // shell, closed
        Assert.Equal(5, rings[1].Count);   // hole, closed
        Assert.Equal(4, rings[0][1].X);
        Assert.Equal(1, rings[1][0].X);
        Assert.Empty(lines);
        Assert.Empty(points);

        var two = GeometryNodes.MultiPolygon([MutationTests.UnitSquare(), withHole]);
        GeometryNodes.Outlines(two, out rings, out _, out _);
        Assert.Equal(3, rings.Count);

        var line = GeometryNodes.LineString([GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(1, 1), GeometryNodes.Coordinate(2, 0)]);
        GeometryNodes.Outlines(line, out rings, out lines, out points);
        Assert.Empty(rings);
        Assert.Single(lines);
        Assert.Equal(3, lines[0].Count);
        Assert.Empty(points);

        var point = GeometryNodes.Point(GeometryNodes.Coordinate(7, 8));
        GeometryNodes.Outlines(point, out rings, out lines, out points);
        Assert.Empty(rings);
        Assert.Empty(lines);
        Assert.Single(points);
        Assert.Equal(7, points[0].X);

        // A mixed collection sorts its parts into the three outputs.
        var mixed = GeometryNodes.GeometryCollection([withHole, line, point, GeometryNodes.MultiPoint([point, point])]);
        GeometryNodes.Outlines(mixed, out rings, out lines, out points);
        Assert.Equal(2, rings.Count);
        Assert.Single(lines);
        Assert.Equal(3, points.Count);

        GeometryNodes.Outlines(null, out rings, out lines, out points);
        Assert.Empty(rings); Assert.Empty(lines); Assert.Empty(points);
        GeometryNodes.Outlines(GeometryNodes.Point(), out _, out _, out points);
        Assert.Empty(points);

        // Copies: writing to an outline must not move the polygon.
        GeometryNodes.Outlines(withHole, out rings, out _, out _);
        rings[0][0].X = 99;
        Assert.Equal(0, GeometryNodes.Coordinates(withHole)[0].X);
    }

    [Fact]
    public void IsSimple_is_false_for_a_self_crossing_line_that_is_still_valid()
    {
        var bowtie = GeometryNodes.LineString(
        [
            GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(1, 0), GeometryNodes.Coordinate(0, 1),
        ]);

        Assert.True(GeometryNodes.IsValid(bowtie, out _, out _));
        Assert.False(GeometryNodes.IsSimple(bowtie));
        Assert.True(GeometryNodes.IsSimple(MutationTests.UnitSquare()));
        Assert.True(GeometryNodes.IsSimple(null));
        Assert.True(GeometryNodes.IsSimple(GeometryNodes.Point()));
    }

    [Fact]
    public void NumPoints_counts_the_ring_closing_coordinate()
    {
        Assert.Equal(5, GeometryNodes.NumPoints(MutationTests.UnitSquare()));
        Assert.Equal(5, GeometryNodes.Coordinates(MutationTests.UnitSquare()).Count);
        Assert.Equal(1, GeometryNodes.NumPoints(GeometryNodes.Point(GeometryNodes.Coordinate(1, 1))));
        Assert.Equal(0, GeometryNodes.NumPoints(GeometryNodes.Point()));
        Assert.Equal(0, GeometryNodes.NumPoints(null));
    }
}
