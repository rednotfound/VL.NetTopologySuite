using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using VL.Core.Import;

namespace VL.NTS;

/// <summary>
/// Reading what a geometry is and what it measures. Category <c>NTS.Geometry</c>.
/// </summary>
/// <remarks>
/// The other half of <c>GeometryNodes.Creation.cs</c>. See that file for the category rule and for
/// why <c>[Name("Geometry")]</c> sits there rather than here — a partial class declares its
/// attributes once.
/// </remarks>
public static partial class GeometryNodes
{
    // ── What is it ────────────────────────────────────────────────────────────

    /// <summary>
    /// The geometry's type name: Point, LineString, Polygon, MultiPolygon and so on.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// The OGC name, so it matches what <c>Write WKT</c> emits and what other GIS tools call it.
    /// Useful for routing a spread of mixed geometries.
    /// </remarks>
    [Name("GeometryType")]
    public static string GeometryType(Geometry? geometry) => geometry?.GeometryType ?? "";

    /// <summary>
    /// Is this geometry empty — no coordinates at all? Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// An empty geometry is legal and is what the creation nodes return for unconnected input, so
    /// this is the check for "nothing arrived yet" rather than an error condition. Note that an
    /// empty geometry is also <c>IsValid</c>, and its <c>Area</c> and <c>Length</c> are 0.
    /// </remarks>
    [Name("IsEmpty")]
    public static bool IsEmpty(Geometry? geometry) => geometry?.IsEmpty ?? true;

    /// <summary>
    /// The SRID this geometry is labelled with. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>0 means unset, which is what the default factory produces — and what <c>Read WKT</c>
    /// produces too, so typed and built geometry agree. That agreement is deliberate: NTS's own
    /// reader stamps <b>-1</b> for "unknown" regardless of the factory it was given, so without a
    /// correction a patch would see two different numbers both meaning "nobody said". An
    /// <c>SRID=...;</c> prefix in the text still wins.</para>
    /// <para>This is a label the patch put there. It is not evidence that the coordinates are in
    /// that reference system, and reading it does not transform anything.</para>
    /// </remarks>
    [Name("SRID")]
    public static int SRID(Geometry? geometry) => geometry?.SRID ?? 0;

    // ── Validity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Is this geometry topologically valid, and if not, why and where?
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>Three outputs rather than one, because NTS hands over the reason and the location for
    /// free and a bare <c>false</c> is the least useful thing a validity check could say. A
    /// self-crossing polygon reports <c>Self-intersection</c> at the coordinate where it crosses; a
    /// hole outside its shell says so.</para>
    /// <para>Validity is not the same as being constructible. A ring that crosses itself closes
    /// perfectly and builds a Polygon without complaint — this is what catches it. It is worth
    /// checking before <c>Buffer</c>, <c>Intersection</c> or <c>Union</c>, since those can behave
    /// unpredictably on invalid input.</para>
    /// <para>An empty geometry is valid, and so is an unconnected pin.</para>
    /// </remarks>
    [Name("IsValid")]
    public static bool IsValid(Geometry? geometry, out string reason, out Coordinate? location)
    {
        if (geometry is null)
        {
            reason = "";
            location = null;
            return true;
        }

        // IsValidOp rather than Geometry.IsValid: the property computes the same thing and then
        // throws the reason away, and the reason is most of the value of asking.
        var error = new IsValidOp(geometry).ValidationError;
        if (error is null)
        {
            reason = "";
            location = null;
            return true;
        }

        reason = error.Message;
        // Copied for the same reason every other coordinate crossing this boundary is: the caller
        // must not be able to write into NTS's own storage.
        location = error.Coordinate is null ? null : Defaults.CopyOf(error.Coordinate);
        return false;
    }

    // ── Measurements ──────────────────────────────────────────────────────────

    /// <summary>
    /// The geometry's area, in the square of whatever units its coordinates are in.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para><b>The units follow the coordinates, and nothing warns otherwise.</b> On longitude and
    /// latitude the answer is in square degrees, which is not a unit of area anybody wants: a
    /// degree of longitude is about 111 km at the equator and 0 km at the poles, so the number is
    /// not convertible to m² by a constant. Project to a metric reference system first if you need
    /// real area.</para>
    /// <para>Computed in 2D — Z is ignored. Points and lines have zero area.</para>
    /// </remarks>
    public static double Area(Geometry? geometry) => geometry?.Area ?? 0;

    /// <summary>
    /// The geometry's length, or a polygon's perimeter, in whatever units its coordinates are in.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// Same units caveat as <c>Area</c>: on longitude and latitude this is a distance in degrees,
    /// not metres. Computed in 2D, so a sloping line measures its horizontal length. A polygon's
    /// perimeter includes its holes.
    /// </remarks>
    public static double Length(Geometry? geometry) => geometry?.Length ?? 0;

    /// <summary>
    /// The geometry's centre of mass, as a Point. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// The centroid of a polygon can fall <b>outside</b> it — a crescent is the usual example — so
    /// this is not the node for "a point somewhere on this shape". An empty geometry gives an empty
    /// Point rather than an error.
    /// </remarks>
    public static Point Centroid(Geometry? geometry)
        => geometry is null ? Defaults.Factory.CreatePoint() : geometry.Centroid;

    /// <summary>
    /// The geometry's bounding box, as four numbers. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>Four floats rather than NTS's <c>Envelope</c> type, because a patch can connect
    /// numbers to anything and cannot open an <c>Envelope</c> at all. NTS's own
    /// <c>Geometry.Envelope</c> is a third thing again — a rectangular <i>Polygon</i> — which is
    /// reachable through VL's raw .NET nodes if that is what you want.</para>
    /// <para>An empty geometry gives all zeros.</para>
    /// </remarks>
    public static void Bounds(
        Geometry? geometry,
        out double minX, out double minY, out double maxX, out double maxY)
    {
        var envelope = geometry?.EnvelopeInternal;
        if (envelope is null || envelope.IsNull)
        {
            minX = minY = maxX = maxY = 0;
            return;
        }

        minX = envelope.MinX;
        minY = envelope.MinY;
        maxX = envelope.MaxX;
        maxY = envelope.MaxY;
    }

    // ── Taking it apart ───────────────────────────────────────────────────────

    /// <summary>
    /// Every Coordinate in the geometry, in order. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>Copies, deliberately. NTS's own <c>Geometry.Coordinates</c> hands out <b>live</b>
    /// references into the geometry's storage — measured, and writing to one moves the geometry —
    /// which would let a downstream node change a geometry another branch of the patch is still
    /// using. These are safe to keep and safe to edit.</para>
    /// <para>A closed ring repeats its first coordinate at the end, because that is what the ring
    /// actually holds. A polygon returns its shell followed by its holes, with no marker between
    /// them; use <c>Geometries</c> for structure.</para>
    /// </remarks>
    public static IReadOnlyList<Coordinate> Coordinates(Geometry? geometry)
        => geometry is null ? [] : Defaults.CopyOf(geometry.Coordinates);

    /// <summary>
    /// The parts of a collection, as separate geometries. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// The counterpart to <c>MultiPoint</c>, <c>MultiPolygon</c> and <c>GeometryCollection</c>. A
    /// single geometry returns itself, one item — not empty — so this is safe to apply to anything,
    /// including the output of an <c>Intersection</c> that may or may not have split into pieces.
    /// </remarks>
    public static IReadOnlyList<Geometry> Geometries(Geometry? geometry)
    {
        if (geometry is null)
            return [];

        var parts = new List<Geometry>(geometry.NumGeometries);
        for (var i = 0; i < geometry.NumGeometries; i++)
            parts.Add(geometry.GetGeometryN(i));
        return parts;
    }
}
