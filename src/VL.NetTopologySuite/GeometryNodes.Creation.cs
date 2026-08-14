using System.Collections.Generic;
using NetTopologySuite.Geometries;
using VL.Core.Import;

namespace VL.NTS;

/// <summary>
/// Creating and inspecting NetTopologySuite geometry. Category <c>NTS.Geometry</c>.
/// </summary>
/// <remarks>
/// <para>The VL category is (namespace minus the "VL" prefix) + type name, so namespace
/// <c>VL.NTS</c> plus <c>[Name("Geometry")]</c> gives <c>NTS.Geometry</c>. <c>[Name]</c> renames
/// the type for VL only — the C# class stays <c>GeometryNodes</c>, because a class literally named
/// <c>Geometry</c> would shadow NetTopologySuite's <c>Geometry</c> in every signature in this
/// assembly.</para>
/// <para>Split across two files, one <c>partial</c> class: creation here, inspection in
/// <c>GeometryNodes.Inspection.cs</c>. One class rather than two so both land in one category
/// without relying on two types being allowed to share one.</para>
/// <para><b>Every node here copies its coordinates.</b> See <see cref="Defaults"/> for the measured
/// reason — NTS coordinate storage is shared and writable, so handing the factory the patch's own
/// <c>Coordinate</c> would leave the finished geometry mutable from the patch.</para>
/// <para><b>Every node here has an optional <c>Factory</c> pin.</b> Left unconnected it uses a
/// shared default with SRID 0 and floating precision, so the common case is
/// <c>Coordinate → Point</c> and not <c>GeometryFactory → CreatePoint</c>. Connected, it is the
/// escape hatch for SRID and precision: those semantics affect correctness and are not hidden.</para>
/// </remarks>
[Name("Geometry")]
public static partial class GeometryNodes
{
    // ── Coordinates ───────────────────────────────────────────────────────────

    /// <summary>
    /// One position: X and Y. The building block every geometry is made of.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// For geographic data X is <b>longitude</b> and Y is <b>latitude</b> — x first, which is the
    /// opposite of how they are usually spoken and the most common source of bugs in this domain.
    /// <para>A <c>Coordinate</c> is mutable, so treat it as a value and do not hold one intending to
    /// edit it later: the geometry nodes copy what they are given, which means editing it afterwards
    /// changes nothing they built.</para>
    /// </remarks>
    public static Coordinate Coordinate(double x = 0, double y = 0) => new Coordinate(x, y);

    /// <summary>
    /// One position with an elevation: X, Y and Z. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// A separate node rather than an optional Z pin because <c>Coordinate</c> and
    /// <c>CoordinateZ</c> are two distinct NetTopologySuite types, not two conveniences over one.
    /// <para>Z survives creation and copying, but <b>most operations ignore it</b>: <c>Area</c> and
    /// <c>Length</c> are computed in 2D, and <c>Buffer</c> and the overlay operations return 2D
    /// results. <c>Write WKT</c> also drops Z unless told otherwise. M ordinates are not exposed in
    /// this version.
    /// </para>
    /// </remarks>
    [Name("CoordinateZ")]
    public static CoordinateZ CoordinateZ(double x = 0, double y = 0, double z = 0)
        => new CoordinateZ(x, y, z);

    /// <summary>
    /// Read a Coordinate's X, Y and Z. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// A <c>Coordinate</c> on a pin is otherwise opaque — a name with no way to see inside it —
    /// and a value a patch author cannot inspect is a value they cannot debug.
    /// <para>Z is <c>NaN</c> for a plain 2D <c>Coordinate</c>, which is NTS's own way of saying
    /// "no Z" rather than a failure.</para>
    /// </remarks>
    public static void Split(Coordinate? coordinate, out double x, out double y, out double z)
    {
        x = coordinate?.X ?? 0;
        y = coordinate?.Y ?? 0;
        z = coordinate?.Z ?? double.NaN;
    }

    // ── The factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// A GeometryFactory that stamps a given SRID onto everything it builds.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>Connect this to any creation node's <c>Factory</c> pin. Leaving that pin unconnected
    /// uses a shared default with <b>SRID 0</b> — NetTopologySuite's own default, meaning
    /// "unset" — so this node is how a patch declares a coordinate reference system.</para>
    /// <para><b>An SRID is a label, not a transformation.</b> Setting it to 4326 does not reproject
    /// anything and does not check that the numbers are longitude and latitude; it records what the
    /// patch author says they already are. Reprojection is a different job and belongs in a
    /// different package.</para>
    /// <para>Mixing SRIDs is also not an error in NTS: intersecting a 4326 geometry with an SRID 0
    /// one returns 4326 without complaint (measured), so a mismatch produces a confident wrong
    /// answer rather than a red node. Set it once, near where the coordinates enter the patch.</para>
    /// <para>Precision is floating — NTS's default. Fixed-precision models are reachable by
    /// constructing a <c>GeometryFactory</c> through VL's raw .NET nodes and plugging it into the
    /// same pin; this node covers the common case rather than every case.</para>
    /// <para>One factory per SRID is created and then reused, so this node is safe to leave sitting
    /// in a patch: it does not allocate on every frame, and it hands out the same instance each
    /// time for a given SRID.</para>
    /// </remarks>
    public static GeometryFactory GeometryFactory([Pin(Name = "SRID")] int srid = 0)
        => Defaults.FactoryFor(srid);

    // ── Single geometries ─────────────────────────────────────────────────────

    /// <summary>
    /// A Point at one Coordinate. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// An unconnected <c>Coordinate</c> gives an empty Point, which is a legal geometry rather than
    /// an error — NTS is empty-in, empty-out throughout.
    /// </remarks>
    public static Point Point(Coordinate? coordinate = null, GeometryFactory? factory = null)
        => coordinate is null
            ? Defaults.Or(factory).CreatePoint()
            : Defaults.Or(factory).CreatePoint(Defaults.CopyOf(coordinate));

    /// <summary>
    /// A LineString through an ordered sequence of Coordinates. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// Order is the line's direction. Fewer than two coordinates gives an empty LineString; NTS
    /// rejects exactly one.
    /// </remarks>
    public static LineString LineString(
        IEnumerable<Coordinate>? coordinates = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateLineString(Defaults.CopyOf(coordinates));

    /// <summary>
    /// A closed ring through an ordered sequence of Coordinates — the part a Polygon is made of.
    /// <b>Closed automatically</b> if the last Coordinate does not repeat the first.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>The auto-closing is deliberate and is the whole reason this node exists rather than
    /// the raw factory method. NTS throws <c>points must form a closed linestring</c> on an open
    /// ring, and four corners of a rectangle — not five — is the natural thing to patch. The
    /// closing coordinate is added as a copy of the first, so the ring holds no shared reference.</para>
    /// <para>A ring being closed does not make the polygon built from it <b>valid</b>: a ring that
    /// crosses itself closes perfectly and produces an invalid polygon. Use <c>IsValid</c>.</para>
    /// <para>Three or more distinct coordinates are needed for a usable ring. An empty input gives
    /// an empty ring; too few coordinates is an error NTS reports, because silently inventing a
    /// ring would be worse than saying so.</para>
    /// </remarks>
    public static LinearRing LinearRing(
        IEnumerable<Coordinate>? coordinates = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateLinearRing(Defaults.CopyAndClose(coordinates));

    /// <summary>
    /// A Polygon: an outer ring, and optionally rings punched out of it. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para><b>Takes rings, not coordinates.</b> The common path is
    /// <c>Coordinates → LinearRing → Polygon</c>, one node longer than going straight from
    /// coordinates but with two payoffs: <c>Shell</c> and <c>Holes</c> are the same type, so the
    /// pins are learnable from each other, and there is one place where ring closing happens rather
    /// than two nodes that both do it slightly differently.</para>
    /// <para>Holes must lie inside the shell and must not overlap each other or cross it. Nothing
    /// here checks that — <c>IsValid</c> does, and will say <c>Hole lies outside shell</c> with the
    /// coordinate where it goes wrong.</para>
    /// <para>An unconnected <c>Shell</c> gives an empty Polygon.</para>
    /// </remarks>
    public static Polygon Polygon(
        LinearRing? shell = null,
        IEnumerable<LinearRing>? holes = null,
        GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        if (shell is null)
            return f.CreatePolygon();

        var holeArray = holes is null ? null : ToArray(holes);
        return holeArray is null || holeArray.Length == 0
            ? f.CreatePolygon(shell)
            : f.CreatePolygon(shell, holeArray);
    }

    // ── Collections ───────────────────────────────────────────────────────────

    /// <summary>Several Points as one geometry. Uses NetTopologySuite.</summary>
    public static MultiPoint MultiPoint(
        IEnumerable<Point>? points = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateMultiPoint(points is null ? [] : ToArray(points));

    /// <summary>Several LineStrings as one geometry. Uses NetTopologySuite.</summary>
    public static MultiLineString MultiLineString(
        IEnumerable<LineString>? lineStrings = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateMultiLineString(lineStrings is null ? [] : ToArray(lineStrings));

    /// <summary>
    /// Several Polygons as one geometry. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// The polygons must not overlap for the result to be valid — a MultiPolygon is a set of
    /// distinct areas, not a way to add them up. <c>Union</c> is what merges overlapping polygons.
    /// </remarks>
    public static MultiPolygon MultiPolygon(
        IEnumerable<Polygon>? polygons = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateMultiPolygon(polygons is null ? [] : ToArray(polygons));

    /// <summary>
    /// Any mix of geometries as one geometry. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// The general case, and the loosest: unlike the Multi* types this accepts points, lines and
    /// polygons together. Several operations refuse to work on a mixed collection, so prefer a
    /// <c>MultiPoint</c>, <c>MultiLineString</c> or <c>MultiPolygon</c> when everything is the same
    /// kind.
    /// </remarks>
    public static GeometryCollection GeometryCollection(
        IEnumerable<Geometry>? geometries = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateGeometryCollection(geometries is null ? [] : ToArray(geometries));

    /// <summary>
    /// Materialise a spread into an array without copying twice when it already is one.
    /// </summary>
    /// <remarks>
    /// Geometries need no defensive copy the way coordinates do: they are handed on by reference
    /// and none of their own members can be reassigned. Only their coordinate storage is writable,
    /// and that was already copied when they were created.
    /// </remarks>
    private static T[] ToArray<T>(IEnumerable<T> items)
        => items as T[] ?? [.. items];
}
