using NetTopologySuite.Geometries;
using VL.NTS;
using Xunit;

namespace VL.NTS.Tests;

/// <summary>
/// Geometry creation: coordinate order, ring closure, holes, empties, SRID, Z.
/// </summary>
public class CreationTests
{
    // ── Coordinates ───────────────────────────────────────────────────────────

    [Fact]
    public void Coordinate_puts_X_first()
    {
        // Coordinate order is the most common source of bugs in this domain, so it gets a test
        // rather than only a doc comment.
        var c = GeometryNodes.Coordinate(13.4, 52.5);
        Assert.Equal(13.4, c.X);
        Assert.Equal(52.5, c.Y);
    }

    [Fact]
    public void Coordinate_has_no_Z()
    {
        Assert.True(double.IsNaN(GeometryNodes.Coordinate(1, 2).Z));
    }

    [Fact]
    public void CoordinateZ_keeps_its_Z_through_a_Point()
    {
        var point = GeometryNodes.Point(GeometryNodes.CoordinateZ(1, 2, 3));
        Assert.Equal(3, point.Coordinate.Z);
    }

    [Fact]
    public void Split_reads_a_coordinate_back()
    {
        GeometryNodes.Split(GeometryNodes.CoordinateZ(1, 2, 3), out var x, out var y, out var z);
        Assert.Equal(1, x);
        Assert.Equal(2, y);
        Assert.Equal(3, z);
    }

    [Fact]
    public void Split_reports_NaN_for_a_missing_Z()
    {
        GeometryNodes.Split(GeometryNodes.Coordinate(1, 2), out _, out _, out var z);
        Assert.True(double.IsNaN(z));
    }

    [Fact]
    public void Split_survives_an_unconnected_pin()
    {
        GeometryNodes.Split(null, out var x, out var y, out var z);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.True(double.IsNaN(z));
    }

    // ── Ring closure ──────────────────────────────────────────────────────────

    [Fact]
    public void LinearRing_closes_an_open_ring()
    {
        var ring = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
        ]);

        Assert.Equal(4, ring.NumPoints);
        Assert.True(ring.IsClosed);
        Assert.Equal(ring.Coordinates[0], ring.Coordinates[^1]);
    }

    [Fact]
    public void LinearRing_leaves_an_already_closed_ring_alone()
    {
        var ring = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(0, 0),
        ]);

        // 4, not 5 - it must not add a second closing coordinate.
        Assert.Equal(4, ring.NumPoints);
    }

    [Fact]
    public void LinearRing_of_nothing_is_empty_rather_than_invented()
    {
        Assert.True(GeometryNodes.LinearRing().IsEmpty);
        Assert.True(GeometryNodes.LinearRing([]).IsEmpty);
    }

    // ── Polygons ──────────────────────────────────────────────────────────────

    [Fact]
    public void Polygon_from_a_ring_has_the_area_of_that_ring()
    {
        Assert.Equal(1.0, MutationTests.UnitSquare().Area, 12);
    }

    [Fact]
    public void Polygon_holes_are_subtracted_from_the_area()
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

        var polygon = GeometryNodes.Polygon(shell, [hole]);

        Assert.Equal(16.0 - 1.0, polygon.Area, 12);
        Assert.Equal(1, polygon.NumInteriorRings);
        Assert.True(polygon.IsValid);
    }

    [Fact]
    public void Polygon_with_an_empty_hole_spread_is_the_same_as_none()
    {
        var shell = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(0, 1),
        ]);

        Assert.Equal(0, GeometryNodes.Polygon(shell, []).NumInteriorRings);
        Assert.Equal(1.0, GeometryNodes.Polygon(shell, []).Area, 12);
    }

    [Fact]
    public void Polygon_without_a_shell_is_empty_rather_than_an_error()
    {
        Assert.True(GeometryNodes.Polygon().IsEmpty);
        Assert.Equal(0, GeometryNodes.Polygon().Area);
    }

    // ── Empty in, empty out ───────────────────────────────────────────────────

    [Fact]
    public void Unconnected_pins_give_empty_geometries_not_exceptions()
    {
        Assert.True(GeometryNodes.Point().IsEmpty);
        Assert.True(GeometryNodes.LineString().IsEmpty);
        Assert.True(GeometryNodes.LinearRing().IsEmpty);
        Assert.True(GeometryNodes.Polygon().IsEmpty);
        Assert.True(GeometryNodes.MultiPoint().IsEmpty);
        Assert.True(GeometryNodes.MultiLineString().IsEmpty);
        Assert.True(GeometryNodes.MultiPolygon().IsEmpty);
        Assert.True(GeometryNodes.GeometryCollection().IsEmpty);
    }

    // ── Collections ───────────────────────────────────────────────────────────

    [Fact]
    public void MultiPoint_holds_its_points()
    {
        var multi = GeometryNodes.MultiPoint(
        [
            GeometryNodes.Point(GeometryNodes.Coordinate(0, 0)),
            GeometryNodes.Point(GeometryNodes.Coordinate(1, 1)),
        ]);

        Assert.Equal(2, multi.NumGeometries);
        Assert.Equal("MultiPoint", multi.GeometryType);
    }

    [Fact]
    public void MultiPolygon_area_is_the_sum_of_its_parts()
    {
        var a = MutationTests.UnitSquare();
        var b = GeometryNodes.Polygon(GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(5, 5),
            GeometryNodes.Coordinate(6, 5),
            GeometryNodes.Coordinate(6, 6),
            GeometryNodes.Coordinate(5, 6),
        ]));

        var multi = GeometryNodes.MultiPolygon([a, b]);

        Assert.Equal(2.0, multi.Area, 12);
        Assert.True(multi.IsValid);
    }

    [Fact]
    public void GeometryCollection_takes_a_mix()
    {
        var collection = GeometryNodes.GeometryCollection(
        [
            GeometryNodes.Point(GeometryNodes.Coordinate(0, 0)),
            GeometryNodes.LineString([GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(1, 1)]),
            MutationTests.UnitSquare(),
        ]);

        Assert.Equal(3, collection.NumGeometries);
    }

    [Fact]
    public void Geometries_takes_a_collection_apart_and_leaves_a_single_geometry_whole()
    {
        var multi = GeometryNodes.MultiPoint(
        [
            GeometryNodes.Point(GeometryNodes.Coordinate(0, 0)),
            GeometryNodes.Point(GeometryNodes.Coordinate(1, 1)),
        ]);

        Assert.Equal(2, GeometryNodes.Geometries(multi).Count);
        // A single geometry returns itself, one item - not empty. This is what makes the node safe
        // to apply to the output of an Intersection that may or may not have split.
        Assert.Single(GeometryNodes.Geometries(MutationTests.UnitSquare()));
        Assert.Empty(GeometryNodes.Geometries(null));
    }

    // ── SRID ──────────────────────────────────────────────────────────────────

    [Fact]
    public void The_default_factory_produces_SRID_zero()
    {
        // The decision recorded in docs/AUDIT.md: 0 means "unset", which is NTS's own default and
        // is honest for a package that has never seen the coordinates.
        Assert.Equal(0, GeometryNodes.Point(GeometryNodes.Coordinate(1, 2)).SRID);
        Assert.Equal(0, MutationTests.UnitSquare().SRID);
    }

    [Fact]
    public void A_factory_stamps_its_SRID_on_everything_built_with_it()
    {
        var factory = GeometryNodes.GeometryFactory(4326);

        Assert.Equal(4326, GeometryNodes.Point(GeometryNodes.Coordinate(1, 2), factory).SRID);
        Assert.Equal(4326, GeometryNodes.LineString(
            [GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(1, 1)], factory).SRID);

        var ring = GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
        ], factory);
        Assert.Equal(4326, ring.SRID);
        Assert.Equal(4326, GeometryNodes.Polygon(ring, null, factory).SRID);
    }

    [Fact]
    public void SRID_survives_an_operation()
    {
        var factory = GeometryNodes.GeometryFactory(4326);
        var square = GeometryNodes.Polygon(GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(0, 1),
        ], factory), null, factory);

        Assert.Equal(4326, OperationNodes.Buffer(square, 0.1)!.SRID);
        Assert.Equal(4326, GeometryNodes.Centroid(square).SRID);
        Assert.Equal(4326, OperationNodes.Union(square, square)!.SRID);
    }

    [Fact]
    public void GeometryFactory_reuses_one_instance_per_SRID()
    {
        // Not a micro-optimisation: a public static method is a node that runs every frame, so a
        // node that allocated per call would allocate sixty times a second forever. Reference
        // stability is also what keeps the WKT reader cache from rebuilding a parser each frame.
        Assert.Same(GeometryNodes.GeometryFactory(4326), GeometryNodes.GeometryFactory(4326));
        Assert.Same(GeometryNodes.GeometryFactory(0), GeometryNodes.GeometryFactory(0));
        Assert.NotSame(GeometryNodes.GeometryFactory(4326), GeometryNodes.GeometryFactory(3857));
    }
}
