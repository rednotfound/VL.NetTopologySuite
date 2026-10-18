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
// Nine nodes, not the twenty NTS could support. What is missing is missing on purpose:
// SymmetricDifference, Touches, Crosses, Overlaps, Covers, ConvexHull and Simplify are all one
// method each - they arrive when something needs them rather than because they exist. Nearest
// Points arrived exactly that way: a consumer needed to draw the shortest line, not merely number
// it. Disjoint is deliberately absent: it is Intersects with a Not after it, and a node a patch can
// already build from two nodes is a help patch, not a node.

/// <summary>
/// Spatial operations: geometry in, geometry out.
/// </summary>
[Name("Operation")]
public static class OperationNodes
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
    /// <param name="segments">Straight edges per quarter circle. Higher gives smoother curves.</param>
    public static Geometry? Buffer(Geometry? geometry, double distance = 0, int segments = 8)
        => geometry?.Buffer(distance, segments);

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
    public static Geometry? Intersection(Geometry? a, Geometry? b)
        => a is null || b is null ? null : a.Intersection(b);

    // Overlapping areas are merged rather than counted twice, which is the difference between this
    // and MultiPolygon. Geometries that do not touch still merge - into a collection with both parts
    // in it.

    /// <summary>
    /// Merges two geometries into one.
    /// </summary>
    /// <remarks>
    /// Overlapping areas become one area; geometries that do not touch become one collection.
    /// </remarks>
    public static Geometry? Union(Geometry? a, Geometry? b)
        => a is null ? b : b is null ? a : a.Union(b);

    /// <summary>
    /// Returns what is left of A once B is cut out of it.
    /// </summary>
    /// <remarks>
    /// The order of A and B matters. Empty when B covers A completely.
    /// </remarks>
    public static Geometry? Difference(Geometry? a, Geometry? b)
        => a is null ? null : b is null ? a : a.Difference(b);

    // ── Measurement between two ───────────────────────────────────────────────

    // Straight-line distance in 2D - on longitude and latitude that is a diagonal across a degree
    // grid rather than a distance over the ground, and it is not in metres. Z is ignored.

    /// <summary>
    /// Returns the shortest distance between two geometries, in their coordinate units.
    /// </summary>
    /// <remarks>
    /// 0 when they touch or overlap.
    /// On longitude and latitude the distance is in degrees, not metres.
    /// </remarks>
    public static double Distance(Geometry? a, Geometry? b)
        => a is null || b is null ? 0 : a.Distance(b);

    // The pair that Distance measures between - drawing a line between the two outputs draws the
    // distance itself, which is what a picture of "how far" actually is. Same units caveat as
    // Distance: the coordinates come back in the geometries' own space.
    //
    // When the geometries touch or overlap the two points coincide - some shared point, not a
    // meaningful "nearest" one, because at distance 0 the question has no unique answer. When either
    // input is missing or empty both outputs are missing rather than an error, matching how Read WKT
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
    // not contained by it. Covers is the version without that exception, and is reachable through
    // VL's raw .NET nodes.

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
}
