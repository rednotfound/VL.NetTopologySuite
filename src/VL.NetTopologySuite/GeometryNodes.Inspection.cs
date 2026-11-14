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

    // The OGC name, so it matches what ToWKT emits and what other GIS tools call it.

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

    // 0 means unset, which is what the default factory produces - and what FromWKT produces too, so
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

    // The other verb (2026-11-11, agreed with the maintainer while designing VL.PROJ): SetSRID says
    // which reference system a geometry's numbers ALREADY are in, and moves nothing. VL.PROJ's
    // Reproject is the one that changes the numbers. PostGIS draws the same line (ST_SetSRID /
    // ST_Transform) and QGIS too ("does not alter the underlying data source"): two nodes that
    // cannot be confused, never one with a mode. It lives here because it is pure NTS.
    //
    // A deep copy made by the target SRID's shared factory, not a mutation of Geometry.SRID: the
    // input is never touched (the copying rule, MutationTests), and the result's FACTORY carries
    // the SRID too, so any NTS operation downstream - which builds its result with the factory -
    // keeps the label.

    /// <summary>
    /// Says which coordinate reference system a geometry's numbers are in. Moves nothing.
    /// </summary>
    /// <remarks>
    /// The SRID is an EPSG code, for example 4326 for longitude and latitude, 28992 for the Dutch grid.
    /// To change the numbers into another system, use Reproject from VL.PROJ.
    /// </remarks>
    /// <param name="geometry">The geometry.</param>
    /// <param name="srid">The EPSG code its coordinates are in. 0 means unset.</param>
    /// <returns>A copy with that SRID and the same coordinates.</returns>
    public static Geometry SetSRID(Geometry? geometry, [Pin(Name = "SRID")] int srid)
        => geometry is null ? Defaults.FactoryFor(srid).CreateGeometryCollection() : Defaults.FactoryFor(srid).CreateGeometry(geometry);

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

    // Simple is a different question from valid: a LineString that crosses itself is perfectly
    // valid (lines have no rule against it) but not simple, and it is the one that trips up buffer
    // and overlay results and the Network node. A polygon's validity already implies simplicity, so
    // this matters for lines and multipoints (a repeated point is not simple). Null and empty are
    // simple. Tier 1, 2026-11-07.

    /// <summary>
    /// Tells whether the geometry has no self-intersections.
    /// </summary>
    /// <remarks>
    /// A line that crosses itself is valid but not simple. Nothing and empty count as simple.
    /// </remarks>
    [Name("IsSimple")]
    public static bool IsSimple(Geometry? geometry) => geometry?.IsSimple ?? true;

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

    // The node for "a point somewhere ON this shape", which Centroid is not: a U-shaped polygon's
    // centroid sits in the gap, its interior point sits in the U. NTS picks a point guaranteed to be
    // in the interior (polygon), on the line (lines), or one of the points (multipoint). The result
    // is a fresh Point with its own Coordinate, even for a Point input - measured in 2.6 against
    // the expectation that it would share the input's instance (it does not; the test asserts the
    // measurement so a change in NTS shows up), so nothing needs copying here, as with Centroid.
    // Tier 1, 2026-11-07.

    /// <summary>
    /// Returns a point that is guaranteed to lie on the geometry.
    /// </summary>
    /// <remarks>
    /// Use this for a label position where Centroid could fall outside the shape, as it does for a U or a crescent.
    /// </remarks>
    public static Point InteriorPoint(Geometry? geometry)
        => geometry is null ? Defaults.Factory.CreatePoint() : geometry.InteriorPoint;

    // Four floats rather than NTS's Envelope type, because a patch can connect numbers to anything
    // and cannot open an Envelope at all. NTS's own Geometry.Envelope is a third thing again - a
    // rectangular Polygon - which is the Envelope node below.

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

    // The same box as Bounds, but as a geometry - so it can go straight into Intersects, Buffer,
    // ToWKT or a map layer without four numbers being reassembled first. NTS degrades it:
    // a point's envelope is a Point, a horizontal or vertical line's is a LineString, anything with
    // area is a rectangular Polygon. Built fresh by NTS, no shared coordinates. Tier 1, 2026-11-07.

    /// <summary>
    /// Returns the geometry's bounding box as a rectangle polygon.
    /// </summary>
    /// <remarks>
    /// The same box Bounds gives as numbers. A point's box is a point and a flat line's is a line.
    /// </remarks>
    public static Geometry Envelope(Geometry? geometry)
        => geometry is null ? Defaults.Factory.CreatePolygon() : geometry.Envelope;

    // ── Taking it apart ───────────────────────────────────────────────────────

    // The count behind Coordinates, without building the spread: cheap enough to show in an IOBox
    // every frame over a large geometry. Counts the ring-closing repeat, as the ring holds it, so a
    // square is 5. Tier 1, 2026-11-07.

    /// <summary>
    /// Returns how many coordinates the geometry holds.
    /// </summary>
    /// <remarks>
    /// A closed ring counts its repeated first coordinate, so a square is 5.
    /// </remarks>
    public static int NumPoints(Geometry? geometry) => geometry?.NumPoints ?? 0;

    // Every vertex as a point, so a drawn IOBox shows where they are: what Densify adds and Simplify
    // removes. NTS has no such operation, but GEOS's extractUniquePoints (Shapely's
    // extract_unique_points) is UniqueCoordinateArrayFilter into a MultiPoint, and so is this: every
    // repeated coordinate is dropped, the ring-closing one included, in first-seen order. Measured on
    // 2.6: a square with a triangular hole, 9 coordinates, gives 7 points. Named as QGIS ("Extract
    // vertices") and ArcGIS ("Feature Vertices To Points") name it; not "Points", which is already
    // the Outlines output for the point parts. The filter hands back the geometry's own Coordinate
    // instances, and CreateMultiPointFromCoords copies them into new points (both measured): a copy
    // here as well was redundant, and a negative test showed it - removing it turned nothing red. The
    // test that the points own their coordinates stays, to catch NTS changing that. Tier 2,
    // 2026-11-14, asked for by the maintainer to make Densify visible.

    /// <summary>
    /// Returns every vertex of the geometry as a point.
    /// </summary>
    /// <remarks>
    /// Each place appears once: a ring's repeated first point, and any point given twice, are dropped.
    /// Points that differ only in Z count as one place.
    /// </remarks>
    public static MultiPoint Vertices(Geometry? geometry)
    {
        if (geometry is null)
            return Defaults.Factory.CreateMultiPoint();

        var unique = global::NetTopologySuite.Utilities.UniqueCoordinateArrayFilter.FilterCoordinates(geometry.Coordinates);
        var points = geometry.Factory.CreateMultiPointFromCoords(unique);
        points.SRID = geometry.SRID;
        return points;
    }

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

    // What drawing needs, sorted the way every GIS draws: rings to fill (a polygon's shell and each
    // hole separately - Coordinates concatenates them, and a polyline through that concatenation
    // draws a stray segment from the shell into the hole, seen in the first GeometryViewer probe,
    // 2026-11-07, which is why this node exists), lines to stroke, points to mark. Three outputs
    // rather than one mixed list because a filled ring, a stroked line and a marker are three
    // different drawings, and a consumer that gets them mixed has to tell them apart again by
    // type. Collections are flattened. Copies, like Coordinates. Empty parts contribute nothing.
    // The editor's own viewer (VL.NetTopologySuite.HDE.vl) is the first consumer.

    /// <summary>
    /// Takes a geometry apart into the rings, lines and points a drawing needs.
    /// </summary>
    /// <remarks>
    /// Rings are every polygon's outer ring and then its holes, Lines every linestring, Points every point, collections flattened.
    /// One polyline per spread, ready for Skia or Stride.
    /// </remarks>
    /// <param name="geometry">The geometry to take apart.</param>
    /// <param name="rings">Each polygon ring as its own spread of coordinates, closed.</param>
    /// <param name="lines">Each linestring as its own spread of coordinates.</param>
    /// <param name="points">Every point's coordinate.</param>
    public static void Outlines(
        Geometry? geometry,
        out IReadOnlyList<IReadOnlyList<Coordinate>> rings,
        out IReadOnlyList<IReadOnlyList<Coordinate>> lines,
        out IReadOnlyList<Coordinate> points)
    {
        var r = new List<IReadOnlyList<Coordinate>>();
        var l = new List<IReadOnlyList<Coordinate>>();
        var p = new List<Coordinate>();
        CollectOutlines(geometry, r, l, p);
        rings = r;
        lines = l;
        points = p;
    }

    static void CollectOutlines(
        Geometry? geometry,
        List<IReadOnlyList<Coordinate>> rings,
        List<IReadOnlyList<Coordinate>> lines,
        List<Coordinate> points)
    {
        switch (geometry)
        {
            case null:
                return;
            case Polygon polygon:
                if (polygon.IsEmpty)
                    return;
                rings.Add(Defaults.CopyOf(polygon.ExteriorRing.Coordinates));
                for (var i = 0; i < polygon.NumInteriorRings; i++)
                    rings.Add(Defaults.CopyOf(polygon.GetInteriorRingN(i).Coordinates));
                return;
            case Point point:
                if (!point.IsEmpty)
                    points.Add(Defaults.CopyOf(point.Coordinate));
                return;
            case GeometryCollection collection:
                for (var i = 0; i < collection.NumGeometries; i++)
                    CollectOutlines(collection.GetGeometryN(i), rings, lines, points);
                return;
            default:
                if (!geometry.IsEmpty)
                    lines.Add(Defaults.CopyOf(geometry.Coordinates));
                return;
        }
    }

    // One dimension down: a polygon's boundary is its rings as lines (the outline to draw or to
    // measure, holes included), a line's boundary is its two end points, a closed ring's and a
    // point's are empty. The usual way to get a polygon's outline as something a line operation
    // can take. For a polygon NTS returns the polygon's own ring objects, as Geometries returns a
    // collection's own parts - the precedent in this file. Tier 1, 2026-11-07.

    /// <summary>
    /// Returns the geometry's outline, one dimension down.
    /// </summary>
    /// <remarks>
    /// A polygon gives its rings as lines, holes included. A line gives its two end points. A point gives nothing.
    /// </remarks>
    public static Geometry Boundary(Geometry? geometry)
    {
        if (geometry is null)
            return Defaults.Factory.CreateGeometryCollection();

        // NTS throws "does not support GeometryCollection arguments" on a mixed collection (review,
        // 2026-11-14): OGC leaves its boundary undefined. Here it is each part's boundary, together.
        if (geometry.OgcGeometryType != OgcGeometryType.GeometryCollection)
            return geometry.Boundary;

        var parts = new Geometry[geometry.NumGeometries];
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Boundary(geometry.GetGeometryN(i));
        var boundary = geometry.Factory.BuildGeometry(parts);
        boundary.SRID = geometry.SRID;
        return boundary;
    }
}
