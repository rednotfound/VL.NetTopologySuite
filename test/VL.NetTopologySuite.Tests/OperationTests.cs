using VL.NTS;
using Xunit;

namespace VL.NTS.Tests;

/// <summary>
/// Spatial operations against known values, and the geometry-preserving chain the package exists
/// for.
/// </summary>
public class OperationTests
{
    [Fact]
    public void Buffering_a_point_gives_roughly_a_disc()
    {
        var point = GeometryNodes.Point(GeometryNodes.Coordinate(0, 0));

        var disc = OperationNodes.Buffer(point, 1, 64)!;

        // A 64-segment approximation is under a circle's area, and close to it. Asserting a range
        // rather than a number, because the exact value is a property of NTS's approximation and
        // pinning it would make this a test of NTS's arithmetic instead of ours.
        Assert.InRange(GeometryNodes.Area(disc), 3.14, System.Math.PI);
        Assert.Equal("Polygon", GeometryNodes.GeometryType(disc));
    }

    [Fact]
    public void More_segments_means_more_area_and_more_coordinates()
    {
        var point = GeometryNodes.Point(GeometryNodes.Coordinate(0, 0));

        var coarse = OperationNodes.Buffer(point, 1, 4)!;
        var fine = OperationNodes.Buffer(point, 1, 64)!;

        Assert.True(GeometryNodes.Area(fine) > GeometryNodes.Area(coarse));
        Assert.True(GeometryNodes.Coordinates(fine).Count > GeometryNodes.Coordinates(coarse).Count);
    }

    [Fact]
    public void A_negative_buffer_erodes_and_can_erode_to_nothing()
    {
        var square = MutationTests.UnitSquare();

        Assert.Equal(0.25, GeometryNodes.Area(OperationNodes.Buffer(square, -0.25)!), 12);
        // Documented on the node: eroding past the middle gives an empty geometry, not an error.
        Assert.True(GeometryNodes.IsEmpty(OperationNodes.Buffer(square, -10)!));
    }

    [Fact]
    public void Buffering_a_line_gives_a_corridor()
    {
        var line = GeometryNodes.LineString(
            [GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(10, 0)]);

        var corridor = OperationNodes.Buffer(line, 1, 64)!;

        // 10 long by 2 wide, plus a half-disc at each end.
        Assert.InRange(GeometryNodes.Area(corridor), 20 + 3.13, 20 + System.Math.PI);
    }

    // ── Overlay ───────────────────────────────────────────────────────────────

    [Fact]
    public void Intersection_of_two_overlapping_squares_is_the_overlap()
    {
        var a = MutationTests.UnitSquare();
        var b = Square(0.5, 0.5, 1);

        Assert.Equal(0.25, GeometryNodes.Area(OperationNodes.Intersection(a, b)!), 12);
    }

    [Fact]
    public void Intersection_of_disjoint_squares_is_empty_rather_than_an_error()
    {
        Assert.True(GeometryNodes.IsEmpty(
            OperationNodes.Intersection(MutationTests.UnitSquare(), Square(10, 10, 1))!));
    }

    [Fact]
    public void Union_merges_an_overlap_instead_of_counting_it_twice()
    {
        var a = MutationTests.UnitSquare();
        var b = Square(0.5, 0.5, 1);

        // 1 + 1 - 0.25, which is the whole difference between Union and MultiPolygon.
        Assert.Equal(1.75, GeometryNodes.Area(OperationNodes.Union(a, b)!), 12);
    }

    [Fact]
    public void Difference_is_not_symmetric()
    {
        var a = MutationTests.UnitSquare();
        var b = Square(0.5, 0.5, 1);

        Assert.Equal(0.75, GeometryNodes.Area(OperationNodes.Difference(a, b)!), 12);
        Assert.Equal(0.75, GeometryNodes.Area(OperationNodes.Difference(b, a)!), 12);
        // Same area, different shape - so compare the shapes, not the numbers.
        Assert.False(OperationNodes.Difference(a, b)!.EqualsTopologically(OperationNodes.Difference(b, a)!));
    }

    [Fact]
    public void Distance_is_zero_when_geometries_touch()
    {
        var a = MutationTests.UnitSquare();

        Assert.Equal(0, OperationNodes.Distance(a, Square(0.5, 0.5, 1)), 12);
        Assert.Equal(1, OperationNodes.Distance(a, Square(2, 0, 1)), 12);
    }

    [Fact]
    public void Nearest_points_span_the_gap_and_agree_with_Distance()
    {
        var a = MutationTests.UnitSquare();     // [0..1] x [0..1]
        var b = Square(3, 0, 1);                // [3..4] x [0..1]

        OperationNodes.NearestPoints(a, b, out var onA, out var onB);

        // The gap runs from x=1 to x=3 at some shared y; the pair must sit on the facing edges,
        // and the line between them must be exactly what Distance measures.
        Assert.Equal(1, onA!.X, 12);
        Assert.Equal(3, onB!.X, 12);
        Assert.Equal(onA.Y, onB.Y, 12);
        Assert.Equal(OperationNodes.Distance(a, b), onA.Distance(onB), 12);
    }

    [Fact]
    public void Nearest_points_coincide_when_geometries_overlap()
    {
        var a = MutationTests.UnitSquare();

        OperationNodes.NearestPoints(a, Square(0.5, 0.5, 1), out var onA, out var onB);

        Assert.Equal(0, onA!.Distance(onB!), 12);
    }

    [Fact]
    public void Nearest_points_of_nothing_are_nothing_rather_than_an_error()
    {
        var square = MutationTests.UnitSquare();

        OperationNodes.NearestPoints(square, null, out var onA, out var onB);
        Assert.Null(onA);
        Assert.Null(onB);

        // An empty geometry has no nearest point either - documented on the node.
        var empty = OperationNodes.Intersection(square, Square(10, 10, 1))!;
        OperationNodes.NearestPoints(square, empty, out onA, out onB);
        Assert.Null(onA);
        Assert.Null(onB);
    }

    [Fact]
    public void Nearest_points_are_copies_not_live_references()
    {
        // Writing through the answer must not move the geometry - the mutation hazard the
        // creation nodes defend against, defended here on the way out.
        var a = MutationTests.UnitSquare();
        var b = Square(3, 0, 1);

        OperationNodes.NearestPoints(a, b, out var onA, out _);
        onA!.X = 999;

        OperationNodes.NearestPoints(a, b, out var again, out _);
        Assert.Equal(1, again!.X, 12);
    }

    // ── Predicates ────────────────────────────────────────────────────────────

    [Fact]
    public void Contains_and_Within_are_each_others_mirror()
    {
        var big = Square(0, 0, 10);
        var small = Square(1, 1, 1);

        Assert.True(OperationNodes.Contains(big, small));
        Assert.False(OperationNodes.Contains(small, big));
        Assert.True(OperationNodes.Within(small, big));
        Assert.False(OperationNodes.Within(big, small));
    }

    [Fact]
    public void Intersects_is_true_for_a_shared_edge_and_false_for_a_gap()
    {
        var a = MutationTests.UnitSquare();

        Assert.True(OperationNodes.Intersects(a, Square(1, 0, 1)));   // shares an edge
        Assert.False(OperationNodes.Intersects(a, Square(2, 0, 1)));  // a gap
    }

    [Fact]
    public void Contains_excludes_a_geometry_lying_on_the_boundary()
    {
        // The edge case named in the node's doc comment, pinned down so the comment stays true.
        var square = MutationTests.UnitSquare();
        var edge = GeometryNodes.LineString(
            [GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(1, 0)]);

        Assert.False(OperationNodes.Contains(square, edge));
        Assert.True(square.Covers(edge));
    }

    [Fact]
    public void Every_operation_survives_an_unconnected_pin()
    {
        var square = MutationTests.UnitSquare();

        Assert.Null(OperationNodes.Buffer(null, 1));
        Assert.Null(OperationNodes.Intersection(square, null));
        Assert.Null(OperationNodes.Intersection(null, square));
        // Union with nothing is the thing itself, which is more useful than null.
        Assert.Same(square, OperationNodes.Union(square, null));
        Assert.Same(square, OperationNodes.Union(null, square));
        Assert.Same(square, OperationNodes.Difference(square, null));
        Assert.Null(OperationNodes.Difference(null, square));
        Assert.Equal(0, OperationNodes.Distance(square, null));
        Assert.False(OperationNodes.Intersects(square, null));
        Assert.False(OperationNodes.Contains(square, null));
        Assert.False(OperationNodes.Within(square, null));
    }

    // ── The chain the package exists for ──────────────────────────────────────

    [Fact]
    public void Operations_chain_without_any_conversion()
    {
        // Brief §12: Geometry -> Operation -> Geometry -> Operation -> Geometry, no conversions
        // anywhere. If this ever needs a cast, the API has gone wrong.
        var square = MutationTests.UnitSquare();

        var result = OperationNodes.Difference(
            OperationNodes.Intersection(
                OperationNodes.Buffer(square, 0.5, 32),
                OperationNodes.Buffer(Square(1, 1, 1), 0.5, 32)),
            GeometryNodes.Point(GeometryNodes.Coordinate(1, 1)));

        Assert.NotNull(result);
        Assert.False(GeometryNodes.IsEmpty(result));
    }

    [Fact]
    public void The_MVP_graph_produces_a_native_NTS_geometry()
    {
        // Brief §35, first target: Coordinates -> Polygon -> Buffer -> Geometry, and the output
        // must be a NetTopologySuite.Geometries.Geometry that another package can take.
        var coordinates = new[]
        {
            GeometryNodes.Coordinate(0, 0),
            GeometryNodes.Coordinate(1, 0),
            GeometryNodes.Coordinate(1, 1),
            GeometryNodes.Coordinate(0, 1),
        };

        var polygon = GeometryNodes.Polygon(GeometryNodes.LinearRing(coordinates));
        var buffered = OperationNodes.Buffer(polygon, 0.1, 16);

        Assert.IsAssignableFrom<NetTopologySuite.Geometries.Geometry>(buffered);
        Assert.True(GeometryNodes.Area(buffered) > 1.0);
        Assert.True(GeometryNodes.IsValid(buffered, out _, out _));
    }

    /// <summary>An axis-aligned square, for tests that need a second shape.</summary>
    private static NetTopologySuite.Geometries.Polygon Square(double x, double y, double size)
        => GeometryNodes.Polygon(GeometryNodes.LinearRing(
        [
            GeometryNodes.Coordinate(x, y),
            GeometryNodes.Coordinate(x + size, y),
            GeometryNodes.Coordinate(x + size, y + size),
            GeometryNodes.Coordinate(x, y + size),
        ]));
}
