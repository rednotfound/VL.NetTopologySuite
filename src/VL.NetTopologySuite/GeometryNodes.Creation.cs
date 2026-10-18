using System.Collections.Generic;
using NetTopologySuite.Geometries;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Category NetTopologySuite.Geometry. The VL category is (namespace minus the "VL" prefix) + type
// name, so namespace VL.NetTopologySuite plus [Name("Geometry")] gives NetTopologySuite.Geometry.
// [Name] renames the type for VL only - the C# class stays GeometryNodes, because a class literally
// named Geometry would shadow NetTopologySuite's Geometry in every signature in this assembly.
//
// Split across two files, one partial class: creation here, inspection in
// GeometryNodes.Inspection.cs. One class rather than two so both land in one category without
// relying on two types being allowed to share one.
//
// Every node here copies its coordinates. See Defaults for the measured reason - NTS coordinate
// storage is shared and writable, so handing the factory the patch's own Coordinate would leave the
// finished geometry mutable from the patch.
//
// Every node here has an optional Factory pin. Left unconnected it uses a shared default with SRID
// 0 and floating precision, so the common case is Coordinate -> Point and not
// GeometryFactory -> CreatePoint. Connected, it is the escape hatch for SRID and precision: those
// semantics affect correctness and are not hidden.

/// <summary>
/// Creates and inspects geometry: points, lines, polygons and their collections.
/// </summary>
[Name("Geometry")]
public static partial class GeometryNodes
{
    // ── Coordinates ───────────────────────────────────────────────────────────

    // X first, which is the opposite of how longitude and latitude are usually spoken and the most
    // common source of bugs in this domain.
    //
    // A Coordinate is mutable, so treat it as a value and do not hold one intending to edit it
    // later: the geometry nodes copy what they are given, which means editing it afterwards changes
    // nothing they built.

    /// <summary>
    /// Makes a position from X and Y.
    /// </summary>
    /// <remarks>
    /// For geographic data X is longitude and Y is latitude: longitude first.
    /// </remarks>
    public static Coordinate Coordinate(double x = 0, double y = 0) => new Coordinate(x, y);

    // A separate node rather than an optional Z pin because Coordinate and CoordinateZ are two
    // distinct NetTopologySuite types, not two conveniences over one.
    //
    // Z survives creation and copying, but most operations ignore it: Area and Length are computed
    // in 2D, and Buffer and the overlay operations return 2D results. Write WKT also drops Z unless
    // told otherwise. M ordinates are not exposed in this version.

    /// <summary>
    /// Makes a position with an elevation from X, Y and Z.
    /// </summary>
    /// <remarks>
    /// Most operations ignore Z: Area, Length, Buffer and the overlay operations work in 2D.
    /// Write WKT keeps Z only with Include Z on.
    /// </remarks>
    [Name("CoordinateZ")]
    public static CoordinateZ CoordinateZ(double x = 0, double y = 0, double z = 0)
        => new CoordinateZ(x, y, z);

    // A Coordinate on a pin is otherwise opaque - a name with no way to see inside it - and a value
    // a patch author cannot inspect is a value they cannot debug. Z is NaN for a plain 2D
    // Coordinate, which is NTS's own way of saying "no Z" rather than a failure.

    /// <summary>
    /// Takes a coordinate apart into X, Y and Z.
    /// </summary>
    /// <param name="coordinate">The coordinate to read.</param>
    /// <param name="x">X, the longitude for geographic data.</param>
    /// <param name="y">Y, the latitude for geographic data.</param>
    /// <param name="z">Z, or NaN when the coordinate has no elevation.</param>
    public static void Split(Coordinate? coordinate, out double x, out double y, out double z)
    {
        x = coordinate?.X ?? 0;
        y = coordinate?.Y ?? 0;
        z = coordinate?.Z ?? double.NaN;
    }

    // ── The factory ───────────────────────────────────────────────────────────

    // Leaving a Factory pin unconnected uses a shared default with SRID 0 - NetTopologySuite's own
    // default, meaning "unset" - so this node is how a patch declares a coordinate reference system.
    //
    // An SRID is a label, not a transformation. Setting it to 4326 does not reproject anything and
    // does not check that the numbers are longitude and latitude; it records what the patch author
    // says they already are. Reprojection is a different job and belongs in a different package.
    //
    // Mixing SRIDs is also not an error in NTS: intersecting a 4326 geometry with an SRID 0 one
    // returns 4326 without complaint (measured), so a mismatch produces a confident wrong answer
    // rather than a red node. Set it once, near where the coordinates enter the patch.
    //
    // Precision is floating - NTS's default. Fixed-precision models are reachable by constructing a
    // GeometryFactory through VL's raw .NET nodes and plugging it into the same pin; this node
    // covers the common case rather than every case.
    //
    // One factory per SRID is created and then reused, so this node is safe to leave sitting in a
    // patch: it does not allocate on every frame, and it hands out the same instance each time for a
    // given SRID.

    /// <summary>
    /// Makes a factory that stamps an SRID on every geometry built with it.
    /// </summary>
    /// <remarks>
    /// Connect it to a creation node's Factory pin; unconnected, the SRID is 0, meaning unset.
    /// The SRID is a label only: nothing is reprojected, and geometries with different SRIDs mix without warning.
    /// </remarks>
    /// <param name="srid">The coordinate reference system's number, for example 4326 for longitude and latitude.</param>
    public static GeometryFactory GeometryFactory([Pin(Name = "SRID")] int srid = 0)
        => Defaults.FactoryFor(srid);

    // ── Single geometries ─────────────────────────────────────────────────────

    // An unconnected Coordinate gives an empty Point, which is a legal geometry rather than an error
    // - NTS is empty-in, empty-out throughout.

    /// <summary>
    /// Makes a point at a coordinate.
    /// </summary>
    /// <remarks>
    /// Unconnected gives an empty point, not an error.
    /// </remarks>
    public static Point Point(Coordinate? coordinate = null, GeometryFactory? factory = null)
        => coordinate is null
            ? Defaults.Or(factory).CreatePoint()
            : Defaults.Or(factory).CreatePoint(Defaults.CopyOf(coordinate));

    // Fewer than two coordinates gives an empty LineString; NTS rejects exactly one.

    /// <summary>
    /// Makes a line through a sequence of coordinates.
    /// </summary>
    /// <remarks>
    /// The order of the coordinates is the line's direction.
    /// </remarks>
    public static LineString LineString(
        IEnumerable<Coordinate>? coordinates = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateLineString(Defaults.CopyOf(coordinates));

    // The auto-closing is deliberate and is the whole reason this node exists rather than the raw
    // factory method. NTS throws "points must form a closed linestring" on an open ring, and four
    // corners of a rectangle - not five - is the natural thing to patch. The closing coordinate is
    // added as a copy of the first, so the ring holds no shared reference.
    //
    // A ring being closed does not make the polygon built from it valid: a ring that crosses itself
    // closes perfectly and produces an invalid polygon.
    //
    // Three or more distinct coordinates are needed for a usable ring. An empty input gives an empty
    // ring; too few coordinates is an error NTS reports, because silently inventing a ring would be
    // worse than saying so.

    /// <summary>
    /// Makes a closed ring through a sequence of coordinates, the outline a polygon is made of.
    /// </summary>
    /// <remarks>
    /// The ring is closed for you: four corners make a rectangle.
    /// It needs at least three distinct coordinates; a ring that crosses itself still builds, so check the polygon with IsValid.
    /// </remarks>
    public static LinearRing LinearRing(
        IEnumerable<Coordinate>? coordinates = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateLinearRing(Defaults.CopyAndClose(coordinates));

    // Takes rings, not coordinates. The common path is Coordinates -> LinearRing -> Polygon, one node
    // longer than going straight from coordinates but with two payoffs: Shell and Holes are the same
    // type, so the pins are learnable from each other, and there is one place where ring closing
    // happens rather than two nodes that both do it slightly differently.
    //
    // Holes must lie inside the shell and must not overlap each other or cross it. Nothing here
    // checks that - IsValid does, and will say "Hole lies outside shell" with the coordinate where
    // it goes wrong.

    /// <summary>
    /// Makes a polygon from an outer ring and optional holes.
    /// </summary>
    /// <remarks>
    /// Holes must lie inside the shell without overlapping; IsValid reports it when they do not.
    /// </remarks>
    /// <param name="shell">The outline, from LinearRing. Unconnected gives an empty polygon.</param>
    /// <param name="holes">Rings cut out of the shell.</param>
    /// <param name="factory">Sets the SRID. Unconnected gives SRID 0.</param>
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

    /// <summary>Makes one geometry from several points.</summary>
    public static MultiPoint MultiPoint(
        IEnumerable<Point>? points = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateMultiPoint(points is null ? [] : ToArray(points));

    /// <summary>Makes one geometry from several lines.</summary>
    public static MultiLineString MultiLineString(
        IEnumerable<LineString>? lineStrings = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateMultiLineString(lineStrings is null ? [] : ToArray(lineStrings));

    // A MultiPolygon is a set of distinct areas, not a way to add them up.

    /// <summary>
    /// Makes one geometry from several polygons.
    /// </summary>
    /// <remarks>
    /// The polygons must not overlap for the result to be valid.
    /// Use Union to merge overlapping polygons.
    /// </remarks>
    public static MultiPolygon MultiPolygon(
        IEnumerable<Polygon>? polygons = null,
        GeometryFactory? factory = null)
        => Defaults.Or(factory).CreateMultiPolygon(polygons is null ? [] : ToArray(polygons));

    // The general case, and the loosest: unlike the Multi* types this accepts points, lines and
    // polygons together. Several operations refuse to work on a mixed collection.

    /// <summary>
    /// Makes one geometry from any mix of geometries.
    /// </summary>
    /// <remarks>
    /// Some operations refuse a mixed collection, so prefer MultiPoint, MultiLineString or MultiPolygon when all parts are the same kind.
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
