using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// The /// text below is what vvvv shows on hover - user-facing, short, one sentence per line. The
// reasoning lives here.
//
// The other half of GeometryNodes.Creation.cs: reading what a geometry is and what it measures.
// See that file for the category rule and for why [Name("Geometry")] sits there rather than here -
// a partial class declares its attributes once, and its type-level /// summary once too.
public static partial class GeometryNodes
{
    // ── What is it ────────────────────────────────────────────────────────────

    // The OGC name, so it matches what Write WKT emits and what other GIS tools call it.

    /// <summary>
    /// Tells what kind of geometry this is: Point, LineString, Polygon, MultiPolygon and so on.
    /// </summary>
    /// <remarks>
    /// Useful for sorting a spread of mixed geometries.
    /// </remarks>
    [Name("GeometryType")]
    public static string GeometryType(Geometry? geometry) => geometry?.GeometryType ?? "";

    // An empty geometry is legal and is what the creation nodes return for unconnected input, so
    // this is the check for "nothing arrived yet" rather than an error condition. An empty geometry
    // is also IsValid, and its Area and Length are 0.

    /// <summary>
    /// Tells whether the geometry has no coordinates at all.
    /// </summary>
    /// <remarks>
    /// Creation nodes give an empty geometry for unconnected input, so this checks that nothing has arrived yet.
    /// </remarks>
    [Name("IsEmpty")]
    public static bool IsEmpty(Geometry? geometry) => geometry?.IsEmpty ?? true;

    // 0 means unset, which is what the default factory produces - and what Read WKT produces too, so
    // typed and built geometry agree. That agreement is deliberate: NTS's own reader stamps -1 for
    // "unknown" regardless of the factory it was given, so without a correction a patch would see
    // two different numbers both meaning "nobody said". An SRID=...; prefix in the text still wins.
    //
    // This is a label the patch put there. It is not evidence that the coordinates are in that
    // reference system, and reading it does not transform anything.

    /// <summary>
    /// Returns the SRID the geometry is labelled with.
    /// </summary>
    /// <remarks>
    /// 0 means unset.
    /// The SRID is a label: it does not prove which reference system the coordinates are in.
    /// </remarks>
    [Name("SRID")]
    public static int SRID(Geometry? geometry) => geometry?.SRID ?? 0;

    // ── Validity ──────────────────────────────────────────────────────────────

    // Three outputs rather than one, because NTS hands over the reason and the location for free and
    // a bare false is the least useful thing a validity check could say. A self-crossing polygon
    // reports "Self-intersection" at the coordinate where it crosses; a hole outside its shell says
    // so.
    //
    // Validity is not the same as being constructible. A ring that crosses itself closes perfectly
    // and builds a Polygon without complaint - this is what catches it. It is worth checking before
    // Buffer, Intersection or Union, since those can behave unpredictably on invalid input.
    //
    // An empty geometry is valid, and so is an unconnected pin.

    /// <summary>
    /// Tells whether the geometry is valid, and if not, why and where.
    /// </summary>
    /// <remarks>
    /// A polygon whose ring crosses itself builds without complaint; this is what catches it.
    /// Check before Buffer, Intersection or Union, which can give surprising results on invalid input.
    /// </remarks>
    /// <param name="geometry">The geometry to check.</param>
    /// <param name="reason">What is wrong, for example "Self-intersection". Empty when valid.</param>
    /// <param name="location">Where it goes wrong. Nothing when valid.</param>
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

    // The units follow the coordinates, and nothing warns otherwise. On longitude and latitude the
    // answer is in square degrees, which is not a unit of area anybody wants: a degree of longitude
    // is about 111 km at the equator and 0 km at the poles, so the number is not convertible to m²
    // by a constant. Project to a metric reference system first for real area.
    //
    // Computed in 2D - Z is ignored. Points and lines have zero area.

    /// <summary>
    /// Returns the geometry's area, in the square of its coordinate units.
    /// </summary>
    /// <remarks>
    /// On longitude and latitude the area is in square degrees, not square metres.
    /// Points and lines have no area.
    /// </remarks>
    public static double Area(Geometry? geometry) => geometry?.Area ?? 0;

    // Same units caveat as Area. Computed in 2D, so a sloping line measures its horizontal length.

    /// <summary>
    /// Returns the length of a line, or the perimeter of a polygon, in its coordinate units.
    /// </summary>
    /// <remarks>
    /// On longitude and latitude the length is in degrees, not metres.
    /// A polygon's perimeter includes its holes.
    /// </remarks>
    public static double Length(Geometry? geometry) => geometry?.Length ?? 0;

    // The centroid of a polygon can fall outside it - a crescent is the usual example - so this is
    // not the node for "a point somewhere on this shape". An empty geometry gives an empty Point
    // rather than an error.

    /// <summary>
    /// Returns the geometry's centre of mass, as a point.
    /// </summary>
    /// <remarks>
    /// The centre of a curved shape such as a crescent can lie outside it.
    /// </remarks>
    public static Point Centroid(Geometry? geometry)
        => geometry is null ? Defaults.Factory.CreatePoint() : geometry.Centroid;

    // Four floats rather than NTS's Envelope type, because a patch can connect numbers to anything
    // and cannot open an Envelope at all. NTS's own Geometry.Envelope is a third thing again - a
    // rectangular Polygon - which is reachable through VL's raw .NET nodes.

    /// <summary>
    /// Returns the geometry's bounding box as four numbers.
    /// </summary>
    /// <remarks>
    /// An empty geometry gives all zeros.
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

    // Copies, deliberately. NTS's own Geometry.Coordinates hands out LIVE references into the
    // geometry's storage - measured, and writing to one moves the geometry - which would let a
    // downstream node change a geometry another branch of the patch is still using. These are safe
    // to keep and safe to edit.
    //
    // A closed ring repeats its first coordinate at the end, because that is what the ring actually
    // holds. A polygon returns its shell followed by its holes, with no marker between them.

    /// <summary>
    /// Returns every coordinate in the geometry, in order.
    /// </summary>
    /// <remarks>
    /// A closed ring repeats its first coordinate at the end.
    /// A polygon gives its outline and then its holes with nothing in between; use Geometries to keep the parts apart.
    /// </remarks>
    public static IReadOnlyList<Coordinate> Coordinates(Geometry? geometry)
        => geometry is null ? [] : Defaults.CopyOf(geometry.Coordinates);

    // The counterpart to MultiPoint, MultiPolygon and GeometryCollection. A single geometry returns
    // itself, one item - not empty - so this is safe to apply to anything, including the output of
    // an Intersection that may or may not have split into pieces.

    /// <summary>
    /// Returns the parts of a collection as separate geometries.
    /// </summary>
    /// <remarks>
    /// A single geometry gives itself as the only part, so this works on anything.
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
