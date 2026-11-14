using System;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;

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
//   the same points every frame. Rejection sampling inside the bounding box, tested against the area
//   with an IndexedPointInAreaLocator. A seeded System.Random is the same sequence on every .NET
//   version (its seeded algorithm is kept for compatibility), so a patch shows the same points on
//   every machine.

public static partial class OperationNodes
{
    private const int MaxRandomPoints = 100_000;

    /// <summary>
    /// Gives the smallest circle that contains the whole geometry.
    /// </summary>
    /// <remarks>
    /// The circle is a polygon with 32 corners, as the Circle node makes it.
    /// </remarks>
    /// <param name="geometry">The geometry to enclose.</param>
    /// <param name="center">The centre of the circle.</param>
    /// <param name="radius">The radius, in the geometry's own units.</param>
    public static Polygon MinimumBoundingCircle(Geometry? geometry, out Coordinate center, out double radius)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            center = new Coordinate(double.NaN, double.NaN);
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
    public static Geometry? ConcaveHull(Geometry? geometry, double ratio = 0.3, bool allowHoles = false)
    {
        if (geometry is null) return null;
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
        var n = Math.Clamp(count, 0, MaxRandomPoints);
        if (area is not IPolygonal || area.IsEmpty || n == 0 || !(area.Area > 0))
            return WithSrid(factory.CreateMultiPoint(), area);

        var env = area.EnvelopeInternal;
        var locator = new IndexedPointInAreaLocator(area);
        var random = new Random(seed);
        var points = new Point[n];
        var found = 0;

        // A thin diagonal polygon fills a small part of its box, so allow many tries per point; an
        // area too thin for that gives fewer points rather than a frame that never ends.
        var tries = (long)n * 1000;
        while (found < n && tries-- > 0)
        {
            var c = new Coordinate(
                env.MinX + random.NextDouble() * env.Width,
                env.MinY + random.NextDouble() * env.Height);
            if (locator.Locate(c) == Location.Interior)
                points[found++] = factory.CreatePoint(c);
        }

        if (found < n)
            Array.Resize(ref points, found);
        return WithSrid(factory.CreateMultiPoint(points), area);
    }

    private static T WithSrid<T>(T geometry, Geometry? from) where T : Geometry
    {
        if (from is not null) geometry.SRID = from.SRID;
        return geometry;
    }
}
