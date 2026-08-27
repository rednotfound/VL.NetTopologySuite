using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using VL.NTS;
using Xunit;

namespace VL.NTS.Tests;

/// <summary>
/// The package's first stateful node, and the contract that makes it one: an index is built once,
/// retained, queried many times, and rebuilt only when the SET OF GEOMETRY REFERENCES changes.
/// Written before the node, per the repository rule.
/// </summary>
public class IndexTests
{
    static Geometry Pt(double x, double y) => GeometryNodes.Point(GeometryNodes.Coordinate(x, y));

    static Geometry Box(double minX, double minY, double maxX, double maxY)
        => GeometryNodes.Polygon(GeometryNodes.LinearRing(new[]
        {
            GeometryNodes.Coordinate(minX, minY), GeometryNodes.Coordinate(maxX, minY),
            GeometryNodes.Coordinate(maxX, maxY), GeometryNodes.Coordinate(minX, maxY),
        }));

    /// <summary>A 10 x 10 grid of points at integer coordinates 0..9.</summary>
    static List<Geometry> Grid()
        => Enumerable.Range(0, 10).SelectMany(x => Enumerable.Range(0, 10).Select(y => Pt(x, y))).ToList();

    // ---- lifecycle -------------------------------------------------------------------------

    [Fact]
    public void Null_collection_builds_nothing()
    {
        var node = new SpatialIndexNode();

        var index = node.Update(null, out var count, out var built);

        Assert.Null(index);
        Assert.Equal(0, count);
        Assert.Equal(0, built);
    }

    [Fact]
    public void Empty_collection_builds_a_real_empty_index()
    {
        var node = new SpatialIndexNode();

        var index = node.Update(new List<Geometry>(), out var count, out var built);

        Assert.NotNull(index);
        Assert.Equal(0, count);
        Assert.Equal(1, built);
        Assert.Empty(IndexNodes.Query(index, Box(-1, -1, 1, 1), out var candidates));
        Assert.Equal(0, candidates);
    }

    [Fact]
    public void Same_collection_reference_never_rebuilds()
    {
        var node = new SpatialIndexNode();
        var grid = Grid();

        var first = node.Update(grid, out _, out _);
        for (var frame = 0; frame < 100; frame++)
            node.Update(grid, out _, out _);
        var last = node.Update(grid, out var count, out var built);

        Assert.Same(first, last);
        Assert.Equal(1, built);
        Assert.Equal(100, count);
    }

    [Fact]
    public void A_new_wrapper_over_the_same_geometries_does_not_rebuild()
    {
        // The case VL produces: a static node upstream hands out a fresh Spread every frame, but
        // if the geometries inside are the same objects, nothing about the index is stale.
        var node = new SpatialIndexNode();
        var grid = Grid();

        var first = node.Update(grid, out _, out _);
        var second = node.Update(new List<Geometry>(grid), out _, out var built);

        Assert.Same(first, second);
        Assert.Equal(1, built);
    }

    [Fact]
    public void A_different_collection_rebuilds_exactly_once()
    {
        var node = new SpatialIndexNode();

        var first = node.Update(Grid(), out _, out _);
        var second = node.Update(Grid(), out _, out var built);   // new Point objects
        node.Update(Grid(), out _, out var builtAgain);            // and again

        Assert.NotSame(first, second);
        Assert.Equal(2, built);
        Assert.Equal(3, builtAgain);
    }

    [Fact]
    public void Adding_to_the_same_mutable_list_is_detected()
    {
        // Reference identity of the wrapper alone would miss this; comparing the elements does not.
        var node = new SpatialIndexNode();
        var list = Grid();

        node.Update(list, out _, out _);
        list.Add(Pt(50, 50));
        node.Update(list, out var count, out var built);

        Assert.Equal(2, built);
        Assert.Equal(101, count);
    }

    [Fact]
    public void Replacing_one_element_is_detected()
    {
        var node = new SpatialIndexNode();
        var list = Grid();

        node.Update(list, out _, out _);
        list[42] = Pt(42, 42);
        node.Update(list, out _, out var built);

        Assert.Equal(2, built);
    }

    [Fact]
    public void Mutating_an_indexed_geometry_in_place_is_NOT_detected_which_is_the_documented_limit()
    {
        // Only possible with a raw NTS handle - this package's own creation nodes copy on the way
        // in, so a patch cannot reach this state through them (MutationTests). Recorded here so
        // the contract is a test and not a sentence.
        var raw = new GeometryFactory().CreatePoint(new Coordinate(5, 5));
        var node = new SpatialIndexNode();
        var index = node.Update(new List<Geometry> { raw }, out _, out _);

        raw.Coordinate.X = 500;                                     // the geometry moves
        node.Update(new List<Geometry> { raw }, out _, out var built);

        Assert.Equal(1, built);                                     // and the index does not know
        Assert.Single(IndexNodes.Query(index, Box(4, 4, 6, 6), out _));   // still answers for x = 5
    }

    [Fact]
    public void The_index_is_built_explicitly_not_on_first_query()
    {
        var node = new SpatialIndexNode();
        var index = node.Update(Grid(), out _, out _)!;

        // NTS: Build "can only be called once ... after all of the data has been inserted", and an
        // insert into a built tree is refused. If our node had left building to the first Query,
        // this insert would succeed.
        Assert.ThrowsAny<Exception>(() => index.Insert(new Envelope(0, 1, 0, 1), Pt(0.5, 0.5)));
    }

    [Fact]
    public void Nulls_and_empty_geometries_in_the_collection_are_skipped()
    {
        var node = new SpatialIndexNode();
        var items = new List<Geometry?> { Pt(0, 0), null, GeometryNodes.Point(null), Pt(1, 1) };

        var index = node.Update(items!, out var count, out _);

        Assert.Equal(2, count);
        Assert.Equal(2, IndexNodes.Query(index, Box(-1, -1, 2, 2), out _).Count);
    }

    // ---- query semantics ---------------------------------------------------------------------

    [Fact]
    public void Query_outside_every_bound_returns_nothing()
    {
        var index = new SpatialIndexNode().Update(Grid(), out _, out _);

        var found = IndexNodes.Query(index, Box(100, 100, 110, 110), out var candidates);

        Assert.Empty(found);
        Assert.Equal(0, candidates);
    }

    [Fact]
    public void Query_returns_exactly_the_points_whose_bounds_intersect()
    {
        // Points have degenerate envelopes, so for points candidate == result and the count is exact.
        var index = new SpatialIndexNode().Update(Grid(), out _, out _);

        var found = IndexNodes.Query(index, Box(2, 2, 4, 4), out var candidates);

        Assert.Equal(9, candidates);
        Assert.Equal(9, found.Count);
        Assert.All(found, g => Assert.InRange(g.Coordinate.X, 2, 4));
        Assert.All(found, g => Assert.InRange(g.Coordinate.Y, 2, 4));
    }

    [Fact]
    public void A_candidate_is_not_a_result()
    {
        // A diagonal line's ENVELOPE covers the whole square, but the line itself passes nowhere
        // near the square's top-left corner. The index must return it (bounds intersect); the
        // exact predicate must then reject it. That gap is the entire lesson of Tutorial 11.
        var diagonal = GeometryNodes.LineString(new[] { GeometryNodes.Coordinate(0, 0), GeometryNodes.Coordinate(10, 10) });
        var index = new SpatialIndexNode().Update(new List<Geometry> { diagonal }, out _, out _);
        var corner = Box(0, 8, 2, 10);

        var candidates = IndexNodes.Query(index, corner, out var candidateCount);

        Assert.Equal(1, candidateCount);                            // the index says "maybe"
        Assert.DoesNotContain(candidates, g => g.Intersects(corner));  // the geometry says "no"
    }

    [Fact]
    public void Candidates_filtered_by_the_exact_predicate_equal_brute_force()
    {
        var shapes = new List<Geometry>();
        for (var i = 0; i < 50; i++)
        {
            shapes.Add(Box(i, i, i + 3, i + 3));                                            // squares up the diagonal
            shapes.Add(GeometryNodes.LineString(new[] { GeometryNodes.Coordinate(i, 0), GeometryNodes.Coordinate(0, i) }));  // lines across it
        }
        var index = new SpatialIndexNode().Update(shapes, out _, out _);
        var search = Box(10, 10, 20, 20);

        var bruteForce = shapes.Where(g => g.Intersects(search)).ToHashSet();
        var viaIndex = IndexNodes.Query(index, search, out var candidateCount).Where(g => g.Intersects(search)).ToHashSet();

        Assert.True(candidateCount >= viaIndex.Count);              // the broad phase over-approximates ...
        Assert.Equal(bruteForce, viaIndex);                         // ... and never loses anything
        Assert.NotEmpty(bruteForce);
    }

    [Fact]
    public void Query_tolerates_missing_inputs()
    {
        var index = new SpatialIndexNode().Update(Grid(), out _, out _);

        Assert.Empty(IndexNodes.Query(null, Box(0, 0, 1, 1), out var a));
        Assert.Empty(IndexNodes.Query(index, null, out var b));
        Assert.Empty(IndexNodes.Query(index, GeometryNodes.Point(null), out var c));
        Assert.Equal(0, a + b + c);
    }

    [Fact]
    public void The_handle_is_the_raw_NTS_tree()
    {
        // Native types, and the one thing we add: no wrapper class. A patch that wants
        // NearestNeighbour or Remove can reach them through raw .NET nodes on this object.
        var index = new SpatialIndexNode().Update(Grid(), out _, out _);

        Assert.IsType<STRtree<Geometry>>(index);
        Assert.Equal(100, index!.Count);
    }
}
