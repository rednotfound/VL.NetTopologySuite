using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Category NetTopologySuite.IO. WKT - Well-Known Text - is here because it belongs to
// NetTopologySuite's own IO namespace and because it is what makes the rest of the package usable:
// it is how a geometry gets into a patch without wiring twenty coordinate nodes, how a help patch
// shows a shape in one IOBox, and how a result gets checked against a value from another tool.
//
// WKB and GeoJSON are deliberately not here. WKB is a reasonable next step. GeoJSON is not: it
// lives in a separate NuGet package (NetTopologySuite.IO.GeoJSON), it brings a feature model that
// is not this package's business, and taking it on is the first step towards the generic geospatial
// file-format package this one is explicitly not trying to be. It has its own package,
// VL.NetTopologySuite.GeoJSON, whose Read GeoJSON and Write GeoJSON join this category.
//
// [Name] is used on the methods here rather than relying on how VL splits a PascalCase name,
// because ReadWKT would otherwise be shown as something like "Read W K T". NameAttribute is legal
// on a method - its AttributeUsage is All, read out of VL.Core's metadata - and the GUI confirms
// the label (see CLAUDE.md).

/// <summary>
/// Reads and writes geometry as Well-Known Text.
/// </summary>
[Name("IO")]
public static class IONodes
{
    // Examples: POINT (13.4 52.5), LINESTRING (0 0, 1 1, 2 0), POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))
    // - note the doubled brackets, which a polygon needs because its outer ring is one of a list.
    //
    // Failure is a pin, not an exception. Half-typed text is the normal state of an IOBox somebody
    // is editing, and a node that throws sixty times a second while they type is not a diagnostic.
    // Success is false and Result is an empty geometry until the text parses.
    //
    // The SRID comes from the Factory pin - 0 when it is unconnected, matching every creation node.
    // An SRID=4326;POINT (...) prefix in the text overrides it. This takes a deliberate correction:
    // left alone, NTS's reader stamps -1 on everything regardless of the factory it was built from
    // (measured), so a patch would see two different numbers for "unset" depending on whether a
    // geometry was typed or built.
    //
    // An unclosed polygon ring is accepted and closed (WKTReader.FixStructure), the same way the
    // LinearRing node closes one, so POLYGON ((0 0, 1 0, 1 1, 0 1)) parses rather than failing on a
    // technicality.
    //
    // Z is read when present (POINT Z (1 2 3)). Reading the same unchanged string every frame does
    // re-parse it every frame - cheap for a handful of coordinates, worth latching with an S+H or a
    // Cache region for anything large.

    /// <summary>
    /// Parses a geometry from Well-Known Text, such as POINT (13.4 52.5).
    /// </summary>
    /// <remarks>
    /// A polygon needs doubled brackets: POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0)). An unclosed ring is closed for you.
    /// The text is parsed every frame, so put a large one in a Cache region.
    /// </remarks>
    /// <param name="wkt">The text to parse.</param>
    /// <param name="factory">Sets the SRID, 0 when unconnected. An SRID=4326; prefix in the text wins.</param>
    /// <param name="success">False until the text parses; the result is then an empty geometry.</param>
    [Name("Read WKT")]
    public static Geometry ReadWKT(
        [Pin(Name = "WKT")] string? wkt,
        GeometryFactory? factory,
        out bool success)
    {
        var f = Defaults.Or(factory);

        if (string.IsNullOrWhiteSpace(wkt))
        {
            // Nothing typed yet is not a failure worth flagging - it is the starting state.
            success = false;
            return f.CreatePoint();
        }

        try
        {
            var geometry = Defaults.ReaderFor(f).Read(wkt);
            success = geometry is not null;
            return geometry ?? f.CreatePoint();
        }
        catch
        {
            // Deliberately catching everything. NTS throws ParseException for malformed text but
            // also ArgumentException for text that parses and then describes something illegal -
            // an unclosed ring, most often - and from a patch author's side those are one problem.
            success = false;
            return f.CreatePoint();
        }
    }

    // The round trip through Read WKT preserves coordinates and geometry type. It does NOT preserve
    // SRID, and there is no pin to make it: WKTWriter in NTS 2.6 has no way to emit one (checked
    // against the type's own members, not assumed), and plain WKT has nowhere to put it. A geometry
    // written and read back carries whatever SRID the reader's Factory says - so if the SRID
    // matters, it travels beside the text, not inside it.
    //
    // Z is dropped unless Include Z is on. NTS's writer defaults to two dimensions and discards Z
    // silently (measured) - a Point built from a CoordinateZ writes as POINT (1 2), with the
    // elevation simply gone. That is a real way to lose data on the way out of a patch, so it is a
    // pin rather than a default nobody looks at. With it on, the output is POINT Z(1 2 3), which NTS
    // reads back and most other tools do too.

    /// <summary>
    /// Writes a geometry as Well-Known Text.
    /// </summary>
    /// <remarks>
    /// The SRID is not written, because WKT has no place for it.
    /// </remarks>
    /// <param name="geometry">The geometry to write.</param>
    /// <param name="includeZ">Writes the elevation too. Off, Z is dropped.</param>
    [Name("Write WKT")]
    public static string WriteWKT(Geometry? geometry, bool includeZ = false)
    {
        if (geometry is null)
            return "";

        // Built per call rather than shared: the output dimension IS the writer's configuration, so
        // one shared instance would have to be mutated per call - and a shared mutable writer is
        // exactly the kind of surprise a patch cannot see. A WKTWriter allocates nothing but itself.
        return new WKTWriter(includeZ ? 3 : 2).Write(geometry);
    }
}
