using VL.NTS;
using Xunit;

namespace VL.NTS.Tests;

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
}
