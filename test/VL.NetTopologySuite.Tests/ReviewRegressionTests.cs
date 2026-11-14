using System.Linq;
using NetTopologySuite.Geometries;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The whole-package review before 0.0.8-alpha (2026-11-14): every input that made a node throw, lose
/// its SRID or freeze a frame, each confirmed by a probe first. A node runs sixty times a second, so a
/// throw is a red node every frame; these are the inputs a patch reaches easily.
/// </summary>
public class ReviewRegressionTests
{
    private static Geometry W(string wkt) => IONodes.FromWKT(wkt, null, out _);

    private const string Bowtie = "POLYGON ((0 0, 2 2, 2 0, 0 2, 0 0))";   // crosses itself at (1 1)
    private const string Square = "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))";
    private const string Mixed = "GEOMETRYCOLLECTION (POLYGON ((0 0, 2 0, 2 2, 0 0)), POINT (5 5), LINESTRING (0 3, 3 3))";

    // ── 1. Overlays on a self-crossing polygon ──────────────────────────────

    [Fact]
    public void Overlays_of_a_bowtie_give_an_answer_instead_of_side_location_conflict()
    {
        // NTS throws TopologyException for all of these (measured).
        var bow = W(Bowtie);
        var sq = W(Square);
        Assert.Equal(0.5, OperationNodes.Intersection(bow, sq).Area, 9);
        Assert.Equal(2.5, OperationNodes.Union(bow, sq).Area, 9);
        Assert.Equal(1.5, OperationNodes.Difference(bow, sq).Area, 9);
        Assert.Equal(2.0, OperationNodes.SymmetricDifference(bow, sq).Area, 9);
        Assert.Equal(2.5, OperationNodes.UnionSpread(new[] { bow, sq }).Area, 9);
    }

    // ── 2. A mixed GeometryCollection ───────────────────────────────────────

    [Fact]
    public void Union_Difference_and_SymmetricDifference_take_a_mixed_collection()
    {
        // NTS: "Operation does not support GeometryCollection arguments" (measured).
        var gc = W(Mixed);
        var sq = W(Square);

        var union = OperationNodes.Union(gc, sq);
        Assert.Equal(2.5, union.Area, 9);          // the triangle (2) and the square (1) overlap by 0.5
        Assert.True(union.Covers(W("POINT (5 5)")));

        var cut = OperationNodes.Difference(sq, gc);
        Assert.Equal(0.5, cut.Area, 9);            // the square minus the triangle

        var left = OperationNodes.Difference(gc, sq);
        Assert.Equal(1.5, left.Area, 9);           // the triangle minus the square, plus the point and the line
        Assert.True(left.Covers(W("POINT (5 5)")));

        Assert.Equal(2.0, OperationNodes.SymmetricDifference(gc, sq).Area, 9);
    }

    [Fact]
    public void Relate_takes_a_mixed_collection_and_agrees_with_the_classic_answer_elsewhere()
    {
        Assert.Equal(9, OperationNodes.Relate(W(Mixed), W(Square)).Length);
        Assert.Equal("212101212", OperationNodes.Relate(W(Square), W("POLYGON ((0.5 0.5, 2 0.5, 2 2, 0.5 2, 0.5 0.5))")));
    }

    [Fact]
    public void Boundary_of_a_mixed_collection_is_each_parts_boundary()
    {
        var b = GeometryNodes.Boundary(W(Mixed));
        Assert.Equal(4 + 2 * System.Math.Sqrt(2), b.Length, 9);   // the triangle's ring; a line's boundary is its two end points
        Assert.True(b.Covers(W("POINT (0 3)")));   // the line's endpoints
    }

    // ── 3. Creation nodes with half-built input ─────────────────────────────

    [Fact]
    public void A_line_of_one_coordinate_is_empty_not_Invalid_number_of_points()
        => Assert.True(GeometryNodes.LineString(new[] { new Coordinate(1, 1) }).IsEmpty);

    [Fact]
    public void A_ring_of_too_few_coordinates_is_empty()
    {
        Assert.True(GeometryNodes.LinearRing(new[] { new Coordinate(1, 1) }).IsEmpty);
        Assert.True(GeometryNodes.LinearRing(new[] { new Coordinate(1, 1), new Coordinate(2, 2) }).IsEmpty);
        Assert.False(GeometryNodes.LinearRing(new[] { new Coordinate(0, 0), new Coordinate(1, 0), new Coordinate(1, 1) }).IsEmpty);
    }

    [Fact]
    public void A_missing_item_in_a_spread_is_skipped()
    {
        // An operation whose input is not connected yet gives a missing part; NTS throws on it.
        Assert.Equal(2, GeometryNodes.LineString(new Coordinate[] { new(0, 0), null!, new(1, 1) }).NumPoints);
        Assert.Equal(1, GeometryNodes.GeometryCollection(new Geometry[] { W(Square), null! }).NumGeometries);
        Assert.Equal(1, GeometryNodes.MultiPolygon(new Polygon[] { (Polygon)W(Square), null! }).NumGeometries);
        var shell = GeometryNodes.LinearRing(new[] { new Coordinate(0, 0), new Coordinate(4, 0), new Coordinate(4, 4), new Coordinate(0, 4) });
        Assert.Equal(16, GeometryNodes.Polygon(shell, new LinearRing[] { null! }).Area, 9);
    }

    // ── 5. ShortestPath keeps the network's SRID ────────────────────────────

    [Fact]
    public void A_route_over_lon_lat_streets_is_in_lon_lat()
    {
        var f = GeometryNodes.GeometryFactory(4326);
        var streets = new[]
        {
            f.CreateLineString(new[] { new Coordinate(0, 0), new Coordinate(1, 0) }),
            f.CreateLineString(new[] { new Coordinate(1, 0), new Coordinate(1, 1) }),
        };
        var network = new NetworkNode().Update(streets, out _, out _, out _);
        var path = NetworkNodes.ShortestPath(network, f.CreatePoint(new Coordinate(0, 0)), f.CreatePoint(new Coordinate(1, 1)),
            out _, out var found, out _, out _);
        Assert.True(found);
        Assert.Equal(4326, path.SRID);
    }

    // ── 7. Vertex counts are bounded ────────────────────────────────────────

    [Fact]
    public void A_huge_Resolution_or_Segments_is_bounded()
    {
        Assert.InRange(GeometryNodes.Circle(new Coordinate(0, 0), 1, 10_000_000).NumPoints, 3, 100_001);
        Assert.InRange(GeometryNodes.RegularPolygon(new Coordinate(0, 0), 1, 10_000_000).NumPoints, 3, 100_001);
        Assert.InRange(OperationNodes.Buffer(W("POINT (0 0)"), 1, 1_000_000).NumPoints, 3, 100_001);
    }

    [Fact]
    public void Buffer_takes_NaN_as_no_distance()
        => Assert.False(OperationNodes.Buffer(W(Square), double.NaN).IsEmpty);

    // ── 8. "No answer" is NaN or nothing, never a plausible number ──────────

    [Fact]
    public void Distance_to_an_empty_geometry_is_NaN_not_touching()
        => Assert.True(double.IsNaN(OperationNodes.Distance(W(Square), W("POINT EMPTY"))));

    [Fact]
    public void The_bounding_circle_of_nothing_has_no_centre()
    {
        OperationNodes.MinimumBoundingCircle(W("POINT EMPTY"), out var c, out _);
        Assert.Null(c);
    }

    // ── 6. No operation returns null ────────────────────────────────────────

    [Fact]
    public void No_geometry_node_returns_null_for_a_missing_input()
    {
        Geometry[] results =
        [
            OperationNodes.Buffer(null), OperationNodes.ConvexHull(null), OperationNodes.Reverse(null),
            OperationNodes.Intersection(null, null), OperationNodes.Union(null, null),
            OperationNodes.Difference(null, null), OperationNodes.SymmetricDifference(null, null),
            OperationNodes.Simplify(null), OperationNodes.Translate(null), OperationNodes.Rotate(null),
            OperationNodes.Scale(null), OperationNodes.Densify(null), OperationNodes.MakeValid(null),
            OperationNodes.ConcaveHull(null), OperationNodes.Substring(null), GeometryNodes.SetSRID(null, 0),
        ];
        Assert.All(results, r => Assert.True(r is not null && r.IsEmpty));
    }
}
