using System.Linq;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// Densify, MakeValid, MinimumBoundingCircle, ConcaveHull, RandomPoints (tier 2, 2026-11-14).
/// </summary>
public class Tier2ShapeTests
{
    private static global::NetTopologySuite.Geometries.Geometry W(string wkt)
        => IONodes.FromWKT(wkt, null, out _);

    // ── Densify ──────────────────────────────────────────────────────────────

    [Fact]
    public void Densify_splits_evenly_never_longer_than_the_spacing()
        => Assert.Equal("LINESTRING (0 0, 2.5 0, 5 0, 7.5 0, 10 0)",
            IONodes.ToWKT(OperationNodes.Densify(W("LINESTRING (0 0, 10 0)"), 3)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Densify_with_no_spacing_changes_nothing_instead_of_throwing(double spacing)
        // NTS throws "Tolerance must be positive" (measured).
        => Assert.Equal("LINESTRING (0 0, 10 0)", IONodes.ToWKT(OperationNodes.Densify(W("LINESTRING (0 0, 10 0)"), spacing)));

    [Fact]
    public void Densify_keeps_its_vertex_count_bounded_however_small_the_spacing()
    {
        var g = OperationNodes.Densify(W("LINESTRING (0 0, 1000000 0)"), 0.000001)!;
        Assert.InRange(g.NumPoints, 2, 100_001);
    }

    [Fact]
    public void Densify_keeps_the_shape_and_the_SRID()
    {
        var square = GeometryNodes.SetSRID(W("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))"), 4326);
        var g = OperationNodes.Densify(square, 1)!;
        Assert.Equal(17, g.NumPoints);
        Assert.True(g.EqualsTopologically(square));
        Assert.Equal(4326, g.SRID);
    }

    // ── MakeValid ────────────────────────────────────────────────────────────

    [Fact]
    public void MakeValid_turns_a_bowtie_into_its_two_triangles()
    {
        var bowtie = W("POLYGON ((0 0, 2 2, 2 0, 0 2, 0 0))");
        Assert.False(bowtie.IsValid);
        var fixedUp = OperationNodes.MakeValid(bowtie)!;
        Assert.True(fixedUp.IsValid);
        Assert.Equal("MultiPolygon", fixedUp.GeometryType);
        Assert.Equal(2.0, fixedUp.Area, 9);
    }

    [Fact]
    public void MakeValid_leaves_a_valid_geometry_as_it_is_and_keeps_the_SRID()
    {
        var g = GeometryNodes.SetSRID(W("POLYGON ((0 0, 1 0, 1 1, 0 0))"), 4326);
        var fixedUp = OperationNodes.MakeValid(g)!;
        Assert.True(fixedUp.EqualsExact(g));
        Assert.Equal(4326, fixedUp.SRID);
    }

    [Fact]
    public void MakeValid_of_nothing_is_empty()
        => Assert.True(OperationNodes.MakeValid(null).IsEmpty);

    // ── MinimumBoundingCircle ───────────────────────────────────────────────

    [Fact]
    public void The_bounding_circle_has_the_exact_centre_and_radius()
    {
        var circle = OperationNodes.MinimumBoundingCircle(W("MULTIPOINT ((0 0), (4 0), (2 3))"), out var c, out var r);
        Assert.Equal(2, c.X, 9);
        Assert.Equal(5.0 / 6, c.Y, 9);
        Assert.Equal(13.0 / 6, r, 9);
        Assert.Equal(33, circle.NumPoints);   // 32 corners, as the Circle node makes it, plus the closing point
    }

    [Fact]
    public void The_bounding_circle_contains_every_point()
    {
        var pts = W("MULTIPOINT ((0 0), (10 1), (3 7), (5 5), (-2 4))");
        var circle = OperationNodes.MinimumBoundingCircle(pts, out var c, out var r);
        foreach (var p in pts.Coordinates)
            Assert.True(p.Distance(c) <= r + 1e-9);
        Assert.True(circle.Area > 0);
    }

    [Fact]
    public void A_single_point_or_nothing_gives_radius_0_and_an_empty_circle()
    {
        Assert.True(OperationNodes.MinimumBoundingCircle(W("POINT (1 1)"), out var c, out var r).IsEmpty);
        Assert.Equal(0, r);
        Assert.Equal(1, c.X);
        Assert.True(OperationNodes.MinimumBoundingCircle(null, out _, out var r2).IsEmpty);
        Assert.Equal(0, r2);
    }

    [Fact]
    public void The_centre_is_not_shared_with_anything_inside_NTS()
    {
        var pts = W("MULTIPOINT ((0 0), (4 0), (2 3))");
        OperationNodes.MinimumBoundingCircle(pts, out var c, out _);
        c.X = 99;
        OperationNodes.MinimumBoundingCircle(pts, out var again, out _);
        Assert.Equal(2, again.X, 9);
    }

    // ── ConcaveHull ──────────────────────────────────────────────────────────

    private const string Scatter = "MULTIPOINT ((0 0), (10 0), (10 10), (0 10), (5 5), (1 5), (5 1), (9 5), (5 9), (3 3))";

    [Fact]
    public void Ratio_1_is_the_convex_hull()
    {
        var pts = W(Scatter);
        Assert.True(OperationNodes.ConcaveHull(pts, 1)!.EqualsTopologically(OperationNodes.ConvexHull(pts)));
    }

    [Fact]
    public void A_smaller_ratio_hugs_tighter_and_still_covers_every_point()
    {
        var pts = W(Scatter);
        var loose = OperationNodes.ConcaveHull(pts, 1)!;
        var tight = OperationNodes.ConcaveHull(pts, 0)!;
        Assert.True(tight.Area < loose.Area);
        Assert.True(tight.Covers(pts));
    }

    [Fact]
    public void ConcaveHull_degrades_like_ConvexHull_and_keeps_the_SRID()
    {
        Assert.Equal("LineString", OperationNodes.ConcaveHull(W("MULTIPOINT ((0 0), (1 1))"))!.GeometryType);
        Assert.True(OperationNodes.ConcaveHull(W("MULTIPOINT EMPTY"))!.IsEmpty);
        Assert.True(OperationNodes.ConcaveHull(null).IsEmpty);
        Assert.Equal(4326, OperationNodes.ConcaveHull(GeometryNodes.SetSRID(W(Scatter), 4326))!.SRID);
    }

    // ── RandomPoints ─────────────────────────────────────────────────────────

    private const string Square = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 6 4, 6 6, 4 6, 4 4))";

    [Fact]
    public void RandomPoints_gives_the_count_all_inside_and_none_in_the_hole()
    {
        var area = W(Square);
        var pts = OperationNodes.RandomPoints(area, 500, 1);
        Assert.Equal(500, pts.NumGeometries);
        Assert.All(pts.Geometries, p => Assert.True(area.Contains(p)));
    }

    [Fact]
    public void The_same_seed_gives_the_same_points_every_frame()
    {
        // NTS's RandomPointsBuilder has no seed (measured): its points would jump every frame.
        var area = W(Square);
        Assert.True(OperationNodes.RandomPoints(area, 50, 7).EqualsExact(OperationNodes.RandomPoints(area, 50, 7)));
        Assert.False(OperationNodes.RandomPoints(area, 50, 7).EqualsExact(OperationNodes.RandomPoints(area, 50, 8)));
    }

    [Fact]
    public void A_seed_gives_the_same_points_on_every_machine()
    {
        // System.Random(seed) keeps its seeded sequence across .NET versions, and the triangle sampling
        // is deterministic; the first point is pinned as a literal so a change in either shows up
        // here rather than as a help patch that looks different.
        var first = (global::NetTopologySuite.Geometries.Point)OperationNodes.RandomPoints(W("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))"), 1, 0).GetGeometryN(0);
        Assert.Equal(0.7680226893946634, first.X, 12);
        Assert.Equal(0.5853480489856322, first.Y, 12);
    }

    [Fact]
    public void A_thin_polygon_gets_every_point_quickly()
    {
        // Rejection in the bounding box took 6.3 s here and gave 61 of 100,000 points (review 2026-11-14).
        var thin = W("POLYGON ((0 0, 1000 1000, 1000 1000.001, 0 0))");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pts = OperationNodes.RandomPoints(thin, 100_000, 0);
        Assert.Equal(100_000, pts.NumGeometries);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("POINT (1 1)")]
    [InlineData("LINESTRING (0 0, 1 1)")]
    [InlineData("POLYGON EMPTY")]
    public void Only_an_area_has_an_inside(string wkt)
        => Assert.True(OperationNodes.RandomPoints(W(wkt), 10).IsEmpty);

    [Fact]
    public void The_count_is_bounded_and_a_negative_count_gives_none()
    {
        Assert.True(OperationNodes.RandomPoints(W(Square), -5).IsEmpty);
        Assert.True(OperationNodes.RandomPoints(null, 5).IsEmpty);
    }

    [Fact]
    public void RandomPoints_keep_the_area_SRID()
        => Assert.Equal(4326, OperationNodes.RandomPoints(GeometryNodes.SetSRID(W(Square), 4326), 3).SRID);
}

/// <summary>
/// Vertices (tier 2, 2026-11-14): every vertex once, as a MultiPoint - Shapely's extract_unique_points.
/// </summary>
public class VerticesTests
{
    private static global::NetTopologySuite.Geometries.Geometry W(string wkt)
        => IONodes.FromWKT(wkt, null, out _);

    [Fact]
    public void A_polygon_gives_each_corner_once_holes_included()
        // 9 coordinates: the two ring-closing repeats are dropped (measured with NTS's filter).
        => Assert.Equal("MULTIPOINT ((0 0), (4 0), (4 4), (0 4), (1 1), (2 1), (2 2))",
            IONodes.ToWKT(GeometryNodes.Vertices(W("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 1))"))));

    [Fact]
    public void A_line_gives_its_vertices_in_order()
        => Assert.Equal("MULTIPOINT ((0 0), (4 0), (4 3))", IONodes.ToWKT(GeometryNodes.Vertices(W("LINESTRING (0 0, 4 0, 4 3)"))));

    [Fact]
    public void Densify_shows_up_as_more_vertices()
        => Assert.Equal(8, GeometryNodes.Vertices(OperationNodes.Densify(W("LINESTRING (0 0, 4 0, 4 3)"), 1)).NumGeometries);

    [Fact]
    public void A_point_given_twice_appears_once()
        => Assert.Equal(2, GeometryNodes.Vertices(W("MULTIPOINT ((1 1), (2 2), (1 1))")).NumGeometries);

    [Fact]
    public void Empty_and_nothing_give_an_empty_multipoint()
    {
        Assert.True(GeometryNodes.Vertices(W("POLYGON EMPTY")).IsEmpty);
        Assert.True(GeometryNodes.Vertices(null).IsEmpty);
    }

    [Fact]
    public void The_points_own_their_coordinates_and_keep_the_SRID_and_Z()
    {
        var line = GeometryNodes.SetSRID(W("LINESTRING Z (0 0 5, 4 0 6)"), 4326);
        var v = GeometryNodes.Vertices(line);
        Assert.Equal(4326, v.SRID);
        Assert.Equal(5, v.GetGeometryN(0).Coordinate.Z);
        v.GetGeometryN(0).Coordinate.X = 99;
        Assert.Equal("LINESTRING (0 0, 4 0)", IONodes.ToWKT(line));
    }
}
