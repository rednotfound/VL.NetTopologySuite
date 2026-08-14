using NetTopologySuite.Geometries;
using VL.Core.Import;

namespace VL.NTS;

/// <summary>
/// Spatial operations: geometry in, geometry out. Category <c>NTS.Operation</c>.
/// </summary>
/// <remarks>
/// <para>The point of this category is the chain in the brief's §12 —
/// <c>Polygon → Buffer → Intersection → Geometry</c> — with no conversion anywhere in it. Every
/// node here takes <c>Geometry</c> and returns <c>Geometry</c>, which is why they compose in any
/// order and why the result can be handed straight to another package.</para>
/// <para><b>None of these mutates its input.</b> NTS overlay operations build new geometries, so a
/// patch can fan one geometry into several operations without them interfering. That is true of the
/// operations even though NTS coordinate <i>storage</i> is writable — see <see cref="Defaults"/>
/// for where that distinction bites and what this package does about it.</para>
/// <para>Eight nodes, not the twenty NTS could support. What is missing is missing on purpose:
/// <c>SymmetricDifference</c>, <c>Touches</c>, <c>Crosses</c>, <c>Overlaps</c>, <c>Covers</c>,
/// <c>ConvexHull</c> and <c>Simplify</c> are all one method each and are listed in
/// <c>docs/ROADMAP.md</c> — they arrive when something needs them rather than because they exist.
/// <c>Disjoint</c> is deliberately absent: it is <c>Intersects</c> with a <c>Not</c> after it, and a
/// node a patch can already build from two nodes is a help patch, not a node.</para>
/// </remarks>
[Name("Operation")]
public static class OperationNodes
{
    // ── Constructive ──────────────────────────────────────────────────────────

    /// <summary>
    /// Grow a geometry outwards by a distance — or shrink it, with a negative distance.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para><b>The distance is in the geometry's own units, and nothing warns otherwise.</b> On
    /// longitude and latitude, <c>0.001</c> is a thousandth of a degree — roughly 111 m near the
    /// equator, and less the further from it, because a degree of longitude narrows towards the
    /// poles. A buffer in real metres means projecting to a metric reference system first.</para>
    /// <para>The result is always a Polygon or MultiPolygon, whatever went in: buffering a point
    /// gives a disc, buffering a line gives a corridor. A negative distance on a polygon erodes it,
    /// and can erode it to nothing — an empty geometry, not an error.</para>
    /// <para><c>Segments</c> is how many straight edges approximate each quarter turn, so a buffered
    /// point is a <c>4 × Segments</c>-sided polygon. 8 is NetTopologySuite's own default and is
    /// visibly faceted at close range; raise it for smoother curves at the cost of more
    /// coordinates.</para>
    /// <para>Buffering an invalid geometry can produce a surprising result rather than an error.
    /// <c>IsValid</c> first if the input was patched by hand.</para>
    /// </remarks>
    public static Geometry? Buffer(Geometry? geometry, double distance = 0, int segments = 8)
        => geometry?.Buffer(distance, segments);

    /// <summary>
    /// The part where two geometries overlap. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// Empty if they do not overlap — not an error. The result can be a collection even when both
    /// inputs were single polygons, because an overlap can fall into several separate pieces; use
    /// <c>Geometries</c> to take them apart.
    /// <para>Both inputs should share a coordinate reference system. NTS does not check: mixing an
    /// SRID 4326 geometry with an SRID 0 one returns 4326 and no warning (measured).</para>
    /// </remarks>
    public static Geometry? Intersection(Geometry? a, Geometry? b)
        => a is null || b is null ? null : a.Intersection(b);

    /// <summary>
    /// Both geometries merged into one. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// Overlapping areas are merged rather than counted twice, which is the difference between this
    /// and <c>MultiPolygon</c>. Geometries that do not touch still merge — into a collection with
    /// both parts in it.
    /// </remarks>
    public static Geometry? Union(Geometry? a, Geometry? b)
        => a is null ? b : b is null ? a : a.Union(b);

    /// <summary>
    /// What is left of A once B is taken out of it. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// Order matters — this is not symmetric. Empty if B swallows A entirely.
    /// </remarks>
    public static Geometry? Difference(Geometry? a, Geometry? b)
        => a is null ? null : b is null ? a : a.Difference(b);

    // ── Measurement between two ───────────────────────────────────────────────

    /// <summary>
    /// The shortest distance between two geometries, in their own coordinate units.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// 0 when they touch or overlap. Straight-line distance in 2D — on longitude and latitude that
    /// is a diagonal across a degree grid rather than a distance over the ground, and it is not in
    /// metres. Z is ignored.
    /// </remarks>
    public static double Distance(Geometry? a, Geometry? b)
        => a is null || b is null ? 0 : a.Distance(b);

    // ── Predicates ────────────────────────────────────────────────────────────

    /// <summary>
    /// Do these two geometries touch or overlap at all? Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// True if they share any point whatsoever, including just a boundary. The broadest of the
    /// predicates and usually the one to reach for first, since it is also the cheapest.
    /// </remarks>
    public static bool Intersects(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Intersects(b);

    /// <summary>
    /// Is B entirely inside A? Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// The exact rule has an edge case worth knowing: a geometry lying entirely <i>on</i> A's
    /// boundary is not contained by it. <c>Covers</c> is the version without that exception, and is
    /// reachable through VL's raw .NET nodes.
    /// </remarks>
    public static bool Contains(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Contains(b);

    /// <summary>
    /// Is A entirely inside B? Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <c>Contains</c> with the inputs the other way round. Both exist because a patch reads better
    /// when the geometry being asked about is on the first pin, and swapping two links to change
    /// the question is easy to misread later.
    /// </remarks>
    public static bool Within(Geometry? a, Geometry? b)
        => a is not null && b is not null && a.Within(b);
}
