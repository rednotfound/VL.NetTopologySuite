using NetTopologySuite.Geometries;
using VL.NTS;
using Xunit;

namespace VL.NTS.Tests;

/// <summary>
/// The regression suite for the one thing this package does that the raw API does not.
/// </summary>
/// <remarks>
/// <para>NTS geometries are widely described as immutable, and coordinate storage is not: the
/// default coordinate sequence factory wraps the caller's array instead of copying it, and
/// <c>Geometry.Coordinates</c> hands out live references. In VL that is worse than in C#, because a
/// value flowing into two branches of a patch is the same reference and nothing marks it read-only.
/// </para>
/// <para>Every test here <b>fails if the defensive copying in <c>Defaults</c> is removed</b>, which
/// is the property that makes them checks rather than decoration — the first block proves the
/// hazard is real against raw NTS, so these are not asserting something that was never possible.
/// </para>
/// </remarks>
public class MutationTests
{
    // ── First, prove the hazard exists at all ─────────────────────────────────

    [Fact]
    public void Raw_NTS_lets_a_caller_move_a_finished_geometry()
    {
        // Not a test of our code. If this ever fails, NTS changed and the copying below may have
        // become unnecessary - which is worth knowing rather than discovering by accident.
        var factory = new GeometryFactory();
        var coordinate = new Coordinate(1, 2);
        var point = factory.CreatePoint(coordinate);

        coordinate.X = 777;

        Assert.Equal(777, point.X);
    }

    [Fact]
    public void Raw_NTS_hands_out_live_coordinates()
    {
        var factory = new GeometryFactory();
        var line = factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        line.Coordinates[0].X = 555;

        Assert.Equal(555, line.Coordinates[0].X);
    }

    // ── Now, that our nodes close it ──────────────────────────────────────────

    [Fact]
    public void Point_cannot_be_moved_through_the_Coordinate_it_was_built_from()
    {
        var coordinate = GeometryNodes.Coordinate(1, 2);
        var point = GeometryNodes.Point(coordinate);

        coordinate.X = 777;

        Assert.Equal(1, point.X);
    }

    [Fact]
    public void LineString_cannot_be_moved_through_its_input_coordinates()
    {
        var coordinates = new[] { GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(1, 1) };
        var line = GeometryNodes.LineString(coordinates);

        coordinates[0].X = 555;

        Assert.Equal(0, line.Coordinates[0].X);
    }

    [Fact]
    public void LinearRing_cannot_be_moved_through_its_input_coordinates()
    {
        var coordinates = new[]
        {
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
        };
        var ring = GeometryNodes.LinearRing(coordinates);

        coordinates[0].X = 555;

        Assert.Equal(0, ring.Coordinates[0].X);
        // The auto-added closing coordinate is a copy too, not a second reference to the first.
        Assert.Equal(0, ring.Coordinates[^1].X);
    }

    [Fact]
    public void Coordinates_reader_hands_out_copies_that_cannot_write_back()
    {
        var polygon = UnitSquare();

        var read = GeometryNodes.Coordinates(polygon);
        read[0].X = 999;

        Assert.Equal(0, GeometryNodes.Coordinates(polygon)[0].X);
    }

    [Fact]
    public void The_ring_closing_coordinate_is_independent_of_the_first()
    {
        // A shared reference here would be invisible until someone edited one end of the ring and
        // watched the other end follow, which is the kind of bug that survives for months.
        var ring = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
        ]);

        Assert.False(ReferenceEquals(ring.Coordinates[0], ring.Coordinates[^1]));
    }

    [Fact]
    public void IsValid_location_is_a_copy_not_a_handle_into_NTS()
    {
        var bowtie = GeometryNodes.Polygon(GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(2, 2),
            GeometryNodes.Coordinate(2, 0),
            GeometryNodes.Coordinate(0, 2),
        ]));

        GeometryNodes.IsValid(bowtie, out _, out var location);
        Assert.NotNull(location);
        location!.X = 12345;

        GeometryNodes.IsValid(bowtie, out _, out var again);
        Assert.Equal(1, again!.X);
    }

    internal static Polygon UnitSquare() => GeometryNodes.Polygon(GeometryNodes.LinearRing(
    [
        GeometryNodes.Coordinate(0, 0),
        GeometryNodes.Coordinate(1, 0),
        GeometryNodes.Coordinate(1, 1),
        GeometryNodes.Coordinate(0, 1),
    ]));
}
