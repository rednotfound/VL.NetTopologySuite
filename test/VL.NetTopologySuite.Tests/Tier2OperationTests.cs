using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// Tier 2, first batch (2026-11-14): Simplify, Union (Spread), Translate, Rotate, Scale.
/// </summary>
public class Tier2OperationTests
{
    private static Geometry W(string wkt, int srid = 0)
    {
        var g = new WKTReader().Read(wkt);
        g.SRID = srid;
        return g;
    }

    private static Geometry Square(double x, double y, double size, int srid = 0)
        => W($"POLYGON (({x} {y}, {x + size} {y}, {x + size} {y + size}, {x} {y + size}, {x} {y}))", srid);

    // ── Union (Spread) ──────────────────────────────────────────────────────

    [Fact]
    public void Union_of_a_spread_merges_overlapping_squares_into_one_area()
    {
        var result = OperationNodes.UnionSpread(new[] { Square(0, 0, 2), Square(1, 0, 2), Square(2, 0, 2) });
        Assert.Equal("Polygon", result.GeometryType);
        Assert.Equal(8, result.Area, 9);
    }

    [Fact]
    public void Union_of_a_spread_gives_the_same_area_as_chaining_Union()
    {
        var squares = Enumerable.Range(0, 10).Select(i => Square(i * 0.7, (i % 3) * 0.5, 1)).ToArray();
        var chained = squares.Skip(1).Aggregate(squares[0], (a, b) => OperationNodes.Union(a, b)!);
        Assert.Equal(chained.Area, OperationNodes.UnionSpread(squares).Area, 9);
    }

    [Fact]
    public void Union_of_an_empty_or_missing_spread_is_empty_not_null()
    {
        Assert.True(OperationNodes.UnionSpread(null).IsEmpty);
        Assert.True(OperationNodes.UnionSpread(new Geometry?[0]).IsEmpty);
        Assert.True(OperationNodes.UnionSpread(new Geometry?[] { null, null }).IsEmpty);
    }

    [Fact]
    public void Union_of_a_spread_skips_missing_geometries_and_keeps_the_SRID()
    {
        var result = OperationNodes.UnionSpread(new Geometry?[] { null, Square(0, 0, 1, 28992), Square(5, 5, 1, 28992) });
        Assert.Equal(2, result.Area, 9);
        Assert.Equal(28992, result.SRID);
    }

    // ── Simplify ────────────────────────────────────────────────────────────

    private const string Wiggle = "LINESTRING (0 0, 1 0.01, 2 -0.01, 3 0.01, 4 0)";

    [Fact]
    public void Simplify_removes_vertices_within_the_tolerance()
    {
        var result = OperationNodes.Simplify(W(Wiggle), 0.1)!;
        Assert.Equal(2, result.NumPoints);
        Assert.Equal(5, OperationNodes.Simplify(W(Wiggle), 0.001)!.NumPoints);
    }

    [Fact]
    public void Simplify_with_zero_or_negative_tolerance_keeps_the_shape()
    {
        Assert.Equal(5, OperationNodes.Simplify(W(Wiggle), 0)!.NumPoints);
        Assert.Equal(5, OperationNodes.Simplify(W(Wiggle), -1)!.NumPoints);
    }

    // A square with a small hole: the topology-preserving simplifier keeps the hole, Douglas-Peucker
    // with a tolerance larger than the hole drops it. That is the whole difference the switch makes.
    private const string WithHole =
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (5 5, 5.2 5, 5.2 5.2, 5 5.2, 5 5))";

    [Fact]
    public void Simplify_preserving_topology_keeps_a_small_hole()
    {
        var result = (Polygon)OperationNodes.Simplify(W(WithHole), 1, preserveTopology: true)!;
        Assert.Equal(1, result.NumInteriorRings);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Simplify_without_topology_may_drop_a_small_hole()
    {
        var result = OperationNodes.Simplify(W(WithHole), 1, preserveTopology: false)!;
        Assert.Equal(0, ((Polygon)result).NumInteriorRings);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Simplify_keeps_the_SRID_and_leaves_the_input_alone()
    {
        var input = W(Wiggle, 4326);
        var before = input.Copy();
        var result = OperationNodes.Simplify(input, 0.1)!;
        Assert.Equal(4326, result.SRID);
        Assert.True(input.EqualsExact(before));
    }

    [Fact]
    public void Simplify_of_nothing_is_empty()
        => Assert.True(OperationNodes.Simplify(null, 1).IsEmpty);

    // ── Translate ───────────────────────────────────────────────────────────

    [Fact]
    public void Translate_moves_east_and_north()
    {
        var result = OperationNodes.Translate(W("POINT (1 2)"), 10, -5)!;
        Assert.Equal(new Coordinate(11, -3), result.Coordinate);
    }

    [Fact]
    public void Translate_copies_so_the_input_and_the_result_are_independent()
    {
        var input = Square(0, 0, 1, 28992);
        var result = OperationNodes.Translate(input, 1, 1)!;
        Assert.Equal(0, input.Coordinates[0].X);
        Assert.Equal(28992, result.SRID);
        Assert.NotSame(input.Coordinates[0], result.Coordinates[0]);
    }

    [Fact]
    public void Transforms_of_nothing_are_empty()
    {
        Assert.True(OperationNodes.Translate(null, 1, 1).IsEmpty);
        Assert.True(OperationNodes.Rotate(null, 0.25).IsEmpty);
        Assert.True(OperationNodes.Scale(null, 2, 2).IsEmpty);
    }

    [Fact]
    public void Transforms_of_an_empty_geometry_are_empty()
    {
        var empty = W("POLYGON EMPTY");
        Assert.True(OperationNodes.Translate(empty, 1, 1)!.IsEmpty);
        Assert.True(OperationNodes.Rotate(empty, 0.25)!.IsEmpty);
        Assert.True(OperationNodes.Scale(empty, 2, 2)!.IsEmpty);
    }

    // ── Rotate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Rotate_a_quarter_cycle_is_counter_clockwise_on_a_north_up_map()
    {
        // (1 0), east of the origin, turned a quarter turn left lands north of it, at (0 1).
        var result = OperationNodes.Rotate(W("POINT (1 0)"), 0.25, new Coordinate(0, 0))!;
        Assert.Equal(0, result.Coordinate.X, 9);
        Assert.Equal(1, result.Coordinate.Y, 9);
    }

    [Fact]
    public void Rotate_without_a_center_turns_in_place()
    {
        // A 4 x 2 box turned a quarter turn around its own middle (2, 1) becomes 2 x 4, same middle.
        var box = W("POLYGON ((0 0, 4 0, 4 2, 0 2, 0 0))");
        var e = OperationNodes.Rotate(box, 0.25)!.EnvelopeInternal;
        Assert.Equal(2, e.Width, 9);
        Assert.Equal(4, e.Height, 9);
        Assert.Equal(2, e.Centre.X, 9);
        Assert.Equal(1, e.Centre.Y, 9);
    }

    [Fact]
    public void Rotate_a_full_cycle_is_where_it_started()
    {
        var box = Square(3, 4, 2);
        Assert.True(OperationNodes.Rotate(box, 1)!.EqualsExact(box, 1e-9));
    }

    // ── Scale ───────────────────────────────────────────────────────────────

    [Fact]
    public void Scale_without_a_center_grows_in_place()
    {
        var e = OperationNodes.Scale(Square(10, 10, 2), 2, 3)!.EnvelopeInternal;
        Assert.Equal(4, e.Width, 9);
        Assert.Equal(6, e.Height, 9);
        Assert.Equal(11, e.Centre.X, 9);
        Assert.Equal(11, e.Centre.Y, 9);
    }

    [Fact]
    public void Scale_around_a_center_keeps_that_point_still()
    {
        var result = OperationNodes.Scale(Square(1, 1, 1), 2, 2, new Coordinate(1, 1))!;
        Assert.Equal(new Envelope(1, 3, 1, 3), result.EnvelopeInternal);
    }

    [Fact]
    public void Scale_by_minus_one_mirrors_and_the_polygon_stays_valid()
    {
        var result = OperationNodes.Scale(Square(0, 0, 2), -1, 1)!;
        Assert.True(result.IsValid);
        Assert.Equal(4, result.Area, 9);
    }
}
