using System;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Hull;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Triangulate.Polygon;

namespace VL.NetTopologySuite;

// Tier 2, 2026-11-14: shapes made from a set of points - the circle around them, the outline that
// hugs them, and points scattered inside an area. Measured against NetTopologySuite 2.6.0:
//
// - MinimumBoundingCircle gives the centre and radius exactly (three points (0 0) (4 0) (2 3):
//   centre (2, 0.8333), radius 2.1667) and its own circle as a 33-point buffer. The circle here is
//   built by this package's Circle node instead, so it has the same 32 corners as a Circle the user
//   makes. A single point gives radius 0 and an empty circle (Circle's rule); empty gives empty.
// - ConcaveHull.ConcaveHullByLengthRatio: 1 is the convex hull, 0 hugs the points as tightly as it
//   can (Shapely's ratio, PostGIS's pctconvex). Shapely defaults to 0, which on scattered points is a
//   jagged star; 0.3 is a readable outline and the default here. Two points give a LineString,
//   empty gives an empty polygon, a line's vertices are used as points.
// - RandomPointsBuilder has NO seed: it draws from a new Random every call, so in a patch (sixty
//   calls a second) the points would jump every frame. RandomPoints is therefore written here, with
//   a Seed pin as PostGIS's ST_GeneratePoints(area, n, seed) has: the same area, count and seed give
//   the same points every frame. Points are drawn in the area's triangles (PolygonTriangulator),
//   weighted by area, so every draw lands inside (rejection in the bounding box was the first
//   version and froze a thin polygon for seconds). A seeded System.Random is the same sequence on every .NET
//   version (its seeded algorithm is kept for compatibility), so a patch shows the same points on
//   every machine.

public static partial class OperationNodes
{
    /// <summary>
    /// Gives the smallest circle that contains the whole geometry.
    /// </summary>
    /// <remarks>
    /// The circle is a polygon with 32 corners, as the Circle node makes it.
    /// </remarks>
    /// <param name="geometry">The geometry to enclose.</param>
    /// <param name="center">The centre of the circle; nothing when the geometry is empty.</param>
    /// <param name="radius">The radius, in the geometry's own units.</param>
    public static Polygon MinimumBoundingCircle(Geometry? geometry, out Coordinate? center, out double radius)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            // No centre is null, as Nearest Points gives it (review 2026-11-14; was NaN, NaN).
            center = null;
            radius = 0;
            return (geometry?.Factory ?? Defaults.Factory).CreatePolygon();
        }

        var mbc = new MinimumBoundingCircle(geometry);
        center = mbc.GetCentre().Copy();
        radius = mbc.GetRadius();
        var circle = GeometryNodes.Circle(center, radius, 32, geometry.Factory);
        circle.SRID = geometry.SRID;
        return circle;
    }

    /// <summary>
    /// Gives an outline that hugs the points of a geometry more tightly than ConvexHull.
    /// </summary>
    /// <remarks>
    /// Ratio 1 is the convex hull; towards 0 it follows the points into every gap.
    /// </remarks>
    /// <param name="geometry">The points to wrap, or any geometry whose vertices are used.</param>
    /// <param name="ratio">From 0, tightest, to 1, the convex hull.</param>
    /// <param name="allowHoles">Lets the outline have holes where the points leave a gap inside.</param>
    public static Geometry ConcaveHull(Geometry? geometry, double ratio = 0.3, bool allowHoles = false)
    {
        if (geometry is null) return Overlays.Nothing();
        var r = double.IsNaN(ratio) ? 0.3 : Math.Clamp(ratio, 0, 1);
        var hull = new ConcaveHull(geometry) { MaximumEdgeLengthRatio = r, HolesAllowed = allowHoles }.GetHull();
        hull.SRID = geometry.SRID;
        return hull;
    }

    /// <summary>
    /// Scatters points at random inside an area.
    /// </summary>
    /// <remarks>
    /// The same Seed gives the same points every frame; change it for another scatter.
    /// At most 100000 points. Only polygons have an inside.
    /// </remarks>
    /// <param name="area">The polygon to fill.</param>
    /// <param name="count">How many points.</param>
    /// <param name="seed">Which scatter. The same seed always gives the same points.</param>
    public static MultiPoint RandomPoints(Geometry? area, int count = 100, int seed = 0)
    {
        var factory = area?.Factory ?? Defaults.Factory;
        var n = Math.Clamp(count, 0, Defaults.MaxVertices);
        if (area is not IPolygonal || area.IsEmpty || n == 0 || !(area.Area > 0))
            return WithSrid(factory.CreateMultiPoint(), area);

        // Area-weighted triangles, not rejection in the bounding box. Rejection froze a frame for
        // 6.3 s on a thin diagonal polygon and still gave 61 of 100,000 points (review, 2026-11-14).
        // Triangulated, every draw lands inside: pick a triangle with probability by area, then a
        // uniform point in it. Exactly Count points, in a time that does not depend on the shape.
        var triangles = Triangles(area);
        if (triangles.Count == 0)
            return WithSrid(factory.CreateMultiPoint(), area);

        var cumulative = new double[triangles.Count];
        var total = 0.0;
        for (var i = 0; i < triangles.Count; i++)
        {
            var (a, b, c) = triangles[i];
            total += Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
            cumulative[i] = total;
        }

        var random = new Random(seed);
        var points = new Point[n];
        for (var k = 0; k < n; k++)
        {
            var i = Array.BinarySearch(cumulative, random.NextDouble() * total);
            if (i < 0) i = ~i;
            if (i >= triangles.Count) i = triangles.Count - 1;
            var (a, b, c) = triangles[i];
            var u = random.NextDouble();
            var v = random.NextDouble();
            if (u + v > 1) { u = 1 - u; v = 1 - v; }
            points[k] = factory.CreatePoint(new Coordinate(
                a.X + u * (b.X - a.X) + v * (c.X - a.X),
                a.Y + u * (b.Y - a.Y) + v * (c.Y - a.Y)));
        }

        return WithSrid(factory.CreateMultiPoint(points), area);
    }

    // The triangles of a polygonal area, each as its three corners. A self-crossing polygon cannot be
    // triangulated, so it is repaired first, as the overlays do; if that fails too, no triangles.
    private static List<(Coordinate A, Coordinate B, Coordinate C)> Triangles(Geometry area)
    {
        Geometry tris;
        try { tris = PolygonTriangulator.Triangulate(area.IsValid ? area : GeometryFixer.Fix(area)); }
        catch (Exception e) when (e is TopologyException or ArgumentException or InvalidOperationException) { return []; }

        var list = new List<(Coordinate, Coordinate, Coordinate)>(tris.NumGeometries);
        for (var i = 0; i < tris.NumGeometries; i++)
        {
            var ring = ((Polygon)tris.GetGeometryN(i)).ExteriorRing.Coordinates;
            if (ring.Length >= 3)
                list.Add((ring[0], ring[1], ring[2]));
        }
        return list;
    }

    private static T WithSrid<T>(T geometry, Geometry? from) where T : Geometry
    {
        if (from is not null) geometry.SRID = from.SRID;
        return geometry;
    }
}
