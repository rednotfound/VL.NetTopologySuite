using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Category NetTopologySuite.Operation. The point of this category is the chain -
// Polygon -> Buffer -> Intersection -> Geometry - with no conversion anywhere in it. Every node here
// takes Geometry and returns Geometry, which is why they compose in any order and why the result
// can be handed straight to another package.
//
// None of these mutates its input. NTS overlay operations build new geometries, so a patch can fan
// one geometry into several operations without them interfering. That is true of the operations
// even though NTS coordinate STORAGE is writable - see Defaults for where that distinction bites
// and what this package does about it.
//
// Nineteen nodes. The first nine were the smallest surface a chain of operations needs; the other
// ten (SymmetricDifference, ConvexHull, Reverse, IsWithinDistance, Touches, Crosses, Overlaps,
// Covers, CoveredBy, Relate) are tier 1 of the coverage plan in docs/ROADMAP.md, added 2026-11-07
// under the maintainer's rule that a package carrying the library's name owes its users what a
// geometry library is expected to do - every one of them is one line of NTS's Geometry class.
// Nearest Points arrived earlier and differently: a consumer needed to draw the shortest line, not
// merely number it. Disjoint is still deliberately absent: it is Intersects with a Not after it,
// and a node a patch can already build from two nodes is a help patch, not a node.
//
// Tier 2 (2026-11-14) adds Simplify and Union (Spread) here, and Translate, Rotate and Scale in
// OperationNodes.Affine.cs - the same class, split by topic.
//
// No node here returns null or throws (review, 2026-11-14). A missing input gives an empty geometry -
// empty in, empty out, as FromWKT gives for text that does not parse - and the overlays go through
// Overlays, which repairs a self-crossing input and takes a mixed GeometryCollection apart rather
// than letting NTS throw. Until then about half the nodes returned null and the overlays threw.
//
// Geometry-in, geometry-out nodes hand back what NTS builds and do not copy whole geometries. Some
// NTS results share Coordinate objects with their input (ConvexHull's ring is made of the input's
// own coordinate instances); that is safe here because every node that hands coordinates OUT
// (Coordinates, Nearest Points, IsValid) copies them, so no patch can write through. The same
// precedent as Geometries, which returns the collection's own parts.

/// <summary>
/// Spatial operations: geometry in, geometry out.
/// </summary>
[Name("Operation")]
public static partial class OperationNodes
{
    // ── Constructive ──────────────────────────────────────────────────────────

    // The distance is in the geometry's own units, and nothing warns otherwise. On longitude and
    // latitude, 0.001 is a thousandth of a degree - roughly 111 m near the equator, and less the
    // further from it, because a degree of longitude narrows towards the poles. A buffer in real
    // metres means projecting to a metric reference system first.
    //
    // The result is always a Polygon or MultiPolygon, whatever went in: buffering a point gives a
    // disc, buffering a line gives a corridor. A negative distance on a polygon erodes it, and can
    // erode it to nothing - an empty geometry, not an error.
    //
    // Segments is how many straight edges approximate each quarter turn, so a buffered point is a
    // 4 x Segments-sided polygon. 8 is NetTopologySuite's own default and is visibly faceted at
    // close range; raise it for smoother curves at the cost of more coordinates.
    //
    // Buffering an invalid geometry can produce a surprising result rather than an error. IsValid
    // first if the input was patched by hand.

    /// <summary>
    /// Grows a geometry outwards by a distance, or shrinks it with a negative distance.
    /// </summary>
    /// <remarks>
    /// The distance is in the geometry's own units: on longitude and latitude, 0.001 is a thousandth of a degree, not a metre.
    /// The result is always a polygon or multipolygon.
    /// </remarks>
    /// <param name="geometry">The geometry to grow or shrink.</param>
    /// <param name="distance">How far to grow, in coordinate units. Negative shrinks.</param>
    /// <param name="segments">Straight edges per quarter circle. Higher gives smoother curves; at most 25000.</param>
    public static Geometry Buffer(Geometry? geometry, double distance = 0, int segments = 8)
        => geometry is null ? Overlays.Nothing()
            : geometry.Buffer(double.IsFinite(distance) ? distance : 0, Math.Min(segments, MaxSegmentsPerQuarter));

    // Segments is per quarter turn, so a buffered point has 4 x Segments corners. A million made one
    // Buffer take 566 ms (review, 2026-11-14) - a frozen patch while somebody types into the IOBox.
    // A quarter of Defaults.MaxVertices per quarter turn: 100,000 corners, the bound every node keeps.
    private const int MaxSegmentsPerQuarter = Defaults.MaxVertices / 4;

    // The smallest convex polygon around everything in the geometry - the rubber band. The usual
    // way to get an outline around a spread of points: MultiPoint -> ConvexHull. Degenerate input
    // degrades gracefully: two points give a LineString, one point a Point, empty stays empty.
    // Tier 1, 2026-11-07.

    /// <summary>
    /// Returns the smallest convex polygon that wraps the whole geometry.
    /// </summary>
    /// <remarks>
    /// A rubber band around a spread of points: MultiPoint, then ConvexHull.
    /// Collinear points give a line and a single point gives a point.
    /// </remarks>
    public static Geometry ConvexHull(Geometry? geometry)
        => geometry is null ? Overlays.Nothing() : geometry.ConvexHull();

    // Same geometry, coordinates in the opposite order. A direction matters for a LineString
    // (animation along it, a one-way street) and for a ring's orientation; NTS's result is a new
    // geometry with a copied, reversed sequence - measured in 2.6: the input is untouched.
    // Tier 1, 2026-11-07.

    /// <summary>
    /// Returns the same geometry with its coordinates in the opposite order.
    /// </summary>
    /// <remarks>
    /// A line runs the other way; a polygon's rings flip orientation. The input is not changed.
    /// </remarks>
    public static Geometry Reverse(Geometry? geometry)
        => geometry is null ? Overlays.Nothing() : geometry.Reverse();

    // Empty if they do not overlap - not an error. The result can be a collection even when both
    // inputs were single polygons, because an overlap can fall into several separate pieces.
    //
    // Both inputs should share a coordinate reference system. NTS does not check: mixing an SRID
    // 4326 geometry with an SRID 0 one returns 4326 and no warning (measured).

    /// <summary>
    /// Returns the part where two geometries overlap.
    /// </summary>
    /// <remarks>
    /// Empty when they do not overlap. The overlap can come in several pieces; Geometries takes them apart.
    /// Both inputs should use the same reference system: nothing checks.
    /// </remarks>
    public static Geometry Intersection(Geometry? a, Geometry? b)
        => a is null || b is null ? Overlays.Nothing(a ?? b) : Overlays.Intersection(a, b);

    // Overlapping areas are merged rather than counted twice, which is the difference between this
    // and MultiPolygon. Geometries that do not touch still merge - into a collection with both parts
    // in it.

    /// <summary>
    /// Merges two geometries into one.
    /// </summary>
    /// <remarks>
    /// Overlapping areas become one area; geometries that do not touch become one collection.
    /// </remarks>
    public static Geometry Union(Geometry? a, Geometry? b)
        => a is null ? b ?? Overlays.Nothing() : b is null ? a : Overlays.Union(a, b);

    // Tier 2, 2026-11-14. Many geometries merged at once - the parcels of a district into its
    // outline, a spread of buffers into one area. NTS's UnaryUnionOp does it in one pass (cascaded,
    // through an STRtree), where folding Union over the spread would redo the whole merged area
    // for every item. The name follows VL's convention for the spread version of a two-input node.
    //
    // Nulls in the spread are skipped. An empty spread gives an empty GeometryCollection, never null:
    // empty in, empty out, as everywhere in this package. Mixed SRIDs do not throw - the result
    // takes the first geometry's factory, so SetSRID first if the spread came from different places.

    /// <summary>
    /// Merges a whole spread of geometries into one.
    /// </summary>
    /// <remarks>
    /// Faster than chaining Union, and the same result: overlapping areas become one area.
    /// An empty spread gives an empty geometry.
    /// </remarks>
    /// <param name="geometries">The geometries to merge. Missing ones are skipped.</param>
    /// <returns>One geometry covering all of them.</returns>
    [Name("Union (Spread)")]
    public static Geometry UnionSpread(IEnumerable<Geometry?>? geometries)
    {
        // The pin was "Input" until the review of 2026-11-14; every other spread of geometries in the
        // package is "Geometries" (GeometryCollection, SpatialIndex). Renamed before it was published.
        var parts = geometries?.Where(g => g is not null).Cast<Geometry>().ToList();
        if (parts is null || parts.Count == 0)
            return Overlays.Nothing();
        return Overlays.UnaryUnion(parts);
    }

    // Tier 2, 2026-11-14. Fewer vertices, same shape within a tolerance - a coastline cut down for
    // drawing, a GPS track thinned out. One node with a switch, as Shapely's
    // simplify(tolerance, preserve_topology=True) has, rather than PostGIS's two functions: one
    // name to find, and the safe behaviour by default.
    //
    // Preserve Topology on: NTS's TopologyPreservingSimplifier. Rings never cross each other or
    // themselves and no ring vanishes, so a polygon with holes stays valid with all its holes.
    // Off: DouglasPeuckerSimplifier, faster and stronger - small rings and holes can disappear, and
    // a polygon may come back as fewer parts.
    //
    // The tolerance is a distance in the geometry's own units, as Buffer's is: no vertex moves
    // further than that from the original line. A negative tolerance is taken as 0 (NTS throws on
    // it), and 0 leaves the shape as it is.

    /// <summary>
    /// Removes vertices that a tolerance says are not needed.
    /// </summary>
    /// <remarks>
    /// The tolerance is in the geometry's own units, as for Buffer.
    /// With Preserve Topology off it is faster, but holes and small parts can disappear.
    /// </remarks>
    /// <param name="geometry">The geometry to simplify.</param>
    /// <param name="tolerance">How far the result may stray from the original, in coordinate units.</param>
    /// <param name="preserveTopology">Keep every ring and hole, and keep rings from crossing.</param>
    public static Geometry Simplify(Geometry? geometry, double tolerance = 0, bool preserveTopology = true)
    {
        if (geometry is null) return Overlays.Nothing();
        var t = double.IsFinite(tolerance) ? Math.Max(0, tolerance) : 0;
        return preserveTopology
            ? global::NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(geometry, t)
            : global::NetTopologySuite.Simplify.DouglasPeuckerSimplifier.Simplify(geometry, t);
    }

    /// <summary>
    /// Returns what is left of A once B is cut out of it.
    /// </summary>
    /// <remarks>
    /// The order of A and B matters. Empty when B covers A completely.
    /// </remarks>
    public static Geometry Difference(Geometry? a, Geometry? b)
        => a is null ? Overlays.Nothing(b) : b is null ? a : Overlays.Difference(a, b);

    // The fourth overlay, and the least used, which is why it came last (tier 1, 2026-11-07). The
    // parts of A and B that are not shared: Union minus Intersection. Symmetric, unlike Difference.
    // NTS 2.6 names it SymmetricDifference too (JTS's symDifference is the abbreviation; checked
    // against the type, which has no SymDifference). Nulls follow Union: a missing side leaves the
    // other side whole, since nothing was shared.

    /// <summary>
    /// Returns the parts of A and B that they do not share.
    /// </summary>
    /// <remarks>
    /// Union minus Intersection. The order of A and B does not matter.
    /// </remarks>
    public static Geometry SymmetricDifference(Geometry? a, Geometry? b)
        => a is null ? b ?? Overlays.Nothing() : b is null ? a : Overlays.SymmetricDifference(a, b);

    // ── Measurement between two ───────────────────────────────────────────────

    // Straight-line distance in 2D - on longitude and latitude that is a diagonal across a degree
    // grid rather than a distance over the ground, and it is not in metres. Z is ignored.

    /// <summary>
    /// Returns the shortest distance between two geometries, in their coordinate units.
    /// </summary>
    /// <remarks>
    /// 0 when they touch or overlap; NaN when one is missing or empty, since there is nothing to measure to.
    /// On longitude and latitude the distance is in degrees, not metres.
    /// </remarks>
    public static double Distance(Geometry? a, Geometry? b)
        // NTS says 0 for an empty geometry (measured), which reads as "touching". Shapely says NaN.
        => a is null || b is null || a.IsEmpty || b.IsEmpty ? double.NaN : a.Distance(b);

    // The pair that Distance measures between - drawing a line between the two outputs draws the
    // distance itself, which is what a picture of "how far" actually is. Same units caveat as
    // Distance: the coordinates come back in the geometries' own space.
    //
    // When the geometries touch or overlap the two points coincide - some shared point, not a
    // meaningful "nearest" one, because at distance 0 the question has no unique answer. When either
    // input is missing or empty both outputs are missing rather than an error, matching how FromWKT
    // treats a question that cannot be answered yet.
    //
    // The coordinates are copies. NTS can hand back live references into a geometry's own storage,
    // and writing through one would silently move the geometry - a mutation hazard, defended the same
    // way the creation nodes defend it.

    /// <summary>
    /// Returns the point on A and the point on B that are closest to each other.
    /// </summary>
    /// <remarks>
    /// A line between the two points draws the distance that Distance measures.
    /// When the geometries touch, both points are the same shared point.
    /// </remarks>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="onA">The closest point on A. Nothing when either input is missing or empty.</param>
    /// <param name="onB">The closest point on B. Nothing when either input is missing or empty.</param>
    [Name("Nearest Points")]
    public static void NearestPoints(Geometry? a, Geometry? b,
        out Coordinate? onA, out Coordinate? onB)
    {
        if (a is null || b is null || a.IsEmpty || b.IsEmpty)
        {
            onA = null;
            onB = null;
            return;
        }

        var points = global::NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(a, b);
        onA = points[0].Copy();
        onB = points[1].Copy();
    }

    // Distance followed by a comparison, but NTS can answer it without computing the full distance
    // - it stops as soon as any pair of parts is close enough - so on large geometries this is the
    // cheaper question. Same units caveat as Distance. Tier 1, 2026-11-07.

    /// <summary>
    /// Tells whether two geometries come within a distance of each other.
    /// </summary>
    /// <remarks>
    /// The distance is in the geometries' coordinate units: degrees on longitude and latitude, not metres.
    /// Faster than Distance followed by a comparison on large geometries.
    /// </remarks>
    /// <param name="a">The first geometry.</param>
    /// <param name="b">The second geometry.</param>
    /// <param name="distance">How close counts, in coordinate units.</param>
    public static bool IsWithinDistance(Geometry? a, Geometry? b, double distance = 0)
        => a is not null && b is not null && a.IsWithinDistance(b, distance);

    // ── Predicates ────────────────────────────────────────────────────────────

    // The broadest of the predicates and usually the one to reach for first, since it is also the
    // cheapest.

    /// <summary>
    /// Tells whether two geometries touch or overlap at all.
    /// </summary>
    /// <remarks>
    /// True when they share any point, even only a boundary.
    /// </remarks>
    public static bool Intersects(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Intersects(b);

    // The exact rule has an edge case worth knowing: a geometry lying entirely ON A's boundary is
    // not contained by it. Covers is the version without that exception, below.

    /// <summary>
    /// Tells whether B lies entirely inside A.
    /// </summary>
    /// <remarks>
    /// A geometry lying only on A's boundary is not contained by A.
    /// </remarks>
    public static bool Contains(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Contains(b);

    // Contains with the inputs the other way round. Both exist because a patch reads better when the
    // geometry being asked about is on the first pin, and swapping two links to change the question
    // is easy to misread later.

    /// <summary>
    /// Tells whether A lies entirely inside B.
    /// </summary>
    /// <remarks>
    /// Contains with the inputs the other way round.
    /// </remarks>
    public static bool Within(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Within(b);

    // The remaining named predicates, tier 1 (2026-11-07). Each is one DE-9IM pattern with a name
    // the OGC Simple Features spec gave it; Relate below is the pattern itself, for the questions
    // that have no name. They are all false when either input is missing, like the three above.

    // Boundaries meet, interiors do not: two parcels sharing a fence, a road ending at a junction.

    /// <summary>
    /// Tells whether two geometries meet only at their edges.
    /// </summary>
    /// <remarks>
    /// True for a shared boundary with no overlap of interiors, such as two parcels along a fence.
    /// </remarks>
    public static bool Touches(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Touches(b);

    // Defined only between different dimensions (a line and a polygon, two lines, a point and a
    // line): the line goes in and comes out again. Two polygons never cross - they overlap.

    /// <summary>
    /// Tells whether a line passes through a geometry and out the other side.
    /// </summary>
    /// <remarks>
    /// Meant for a line against a polygon, or two lines. Two polygons never cross; use Overlaps.
    /// </remarks>
    public static bool Crosses(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Crosses(b);

    // Same dimension, partial overlap, neither inside the other. Two polygons that share some area
    // but not all of it; two lines that run together for a stretch.

    /// <summary>
    /// Tells whether two geometries of the same kind share some of their area but not all.
    /// </summary>
    /// <remarks>
    /// False when one lies entirely inside the other, and false when they only touch.
    /// </remarks>
    public static bool Overlaps(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Overlaps(b);

    // Contains without the boundary exception: a geometry lying on A's edge IS covered by A. This is
    // the question most people mean when they say "inside", and it is the one with no surprises on
    // edges, so prefer it over Contains unless the exception is wanted.

    /// <summary>
    /// Tells whether no point of B lies outside A.
    /// </summary>
    /// <remarks>
    /// Like Contains, but a geometry lying on A's boundary counts as covered.
    /// </remarks>
    public static bool Covers(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Covers(b);

    // Covers with the inputs the other way round, for the same reason Within exists beside Contains.

    /// <summary>
    /// Tells whether no point of A lies outside B.
    /// </summary>
    /// <remarks>
    /// Covers with the inputs the other way round.
    /// </remarks>
    public static bool CoveredBy(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.CoveredBy(b);

    // The DE-9IM intersection matrix as its nine-character string, for the relationship questions
    // that have no named predicate. Each character says what the intersection of one part of A
    // (interior, boundary, exterior) with one part of B has: F for nothing, 0 for points, 1 for
    // lines, 2 for areas. Two overlapping squares give "212101212". A pattern test against this
    // string with wildcards (NTS's IntersectionMatrix.Matches) is left raw: the five named
    // predicates above cover nearly every question a patch asks, and VL has string nodes for the
    // rest.

    /// <summary>
    /// Returns how two geometries relate as the nine-character DE-9IM string.
    /// </summary>
    /// <remarks>
    /// Interior, boundary and exterior of A against those of B: F for no contact, 0 for points, 1 for lines, 2 for areas.
    /// Two overlapping squares give 212101212. Empty when an input is missing.
    /// </remarks>
    public static string Relate(Geometry? a, Geometry? b)
        // RelateNG (NTS 2.6), not Geometry.Relate: the classic one throws on a GeometryCollection
        // ("does not support GeometryCollection arguments", review 2026-11-14); RelateNG takes it and
        // gives the same matrix elsewhere (measured: 212111212 both ways for two overlapping squares).
        => a is null || b is null ? "" : global::NetTopologySuite.Operation.RelateNG.RelateNG.Relate(a, b).ToString();
}
