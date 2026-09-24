using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using VL.Core.Import;

namespace VL.NTS;

/// <summary>
/// Reading and writing geometry as text. Category <c>NTS.IO</c>.
/// </summary>
/// <remarks>
/// <para>WKT — Well-Known Text — is here because it belongs to NetTopologySuite's own IO namespace
/// and because it is what makes the rest of the package usable: it is how a geometry gets into a
/// patch without wiring twenty coordinate nodes, how a help patch shows a shape in one IOBox, and
/// how a result gets checked against a value from another tool.</para>
/// <para><b>WKB and GeoJSON are deliberately not here.</b> WKB is a reasonable next step. GeoJSON is not: it lives in a separate NuGet package
/// (<c>NetTopologySuite.IO.GeoJSON</c>), it brings a feature model that is not this package's
/// business, and taking it on is the first step towards the generic geospatial file-format package
/// this one is explicitly not trying to be. It deserves its own package.</para>
/// <para><c>[Name]</c> is used on the methods here rather than relying on how VL splits a
/// PascalCase name, because <c>ReadWKT</c> would otherwise be shown as something like
/// "Read W K T". Verified that <c>NameAttribute</c> is legal on a method — its
/// <c>AttributeUsage</c> is <c>All</c>, read out of <c>VL.Core</c>'s metadata. <b>Only the GUI
/// proves a node's label</b>, so this is confirmed as far as the compiler goes and no further.</para>
/// </remarks>
[Name("IO")]
public static class IONodes
{
    /// <summary>
    /// Parse a geometry from Well-Known Text. Reports failure instead of throwing.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>Examples: <c>POINT (13.4 52.5)</c>, <c>LINESTRING (0 0, 1 1, 2 0)</c>,
    /// <c>POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))</c> — note the doubled brackets, which a polygon needs
    /// because its outer ring is one of a list. Rings must be closed, repeating the first
    /// coordinate.</para>
    /// <para><b>Failure is a pin, not an exception.</b> Half-typed text is the normal state of an
    /// IOBox somebody is editing, and a node that throws sixty times a second while they type is
    /// not a diagnostic. <c>Success</c> is false and <c>Result</c> is an empty geometry until the
    /// text parses.</para>
    /// <para>The SRID comes from the <c>Factory</c> pin — 0 when it is unconnected, matching every
    /// creation node. An <c>SRID=4326;POINT (...)</c> prefix in the text overrides it. This takes a
    /// deliberate correction: left alone, NTS's reader stamps <b>-1</b> on everything regardless of
    /// the factory it was built from (measured), so a patch would see two different numbers for
    /// "unset" depending on whether a geometry was typed or built.</para>
    /// <para><b>An unclosed polygon ring is accepted and closed</b>, the same way the
    /// <c>LinearRing</c> node closes one, so <c>POLYGON ((0 0, 1 0, 1 1, 0 1))</c> parses rather
    /// than failing on a technicality.</para>
    /// <para>Z is read when present (<c>POINT Z (1 2 3)</c>). Reading the same unchanged string every
    /// frame does re-parse it every frame — cheap for a handful of coordinates, worth latching with
    /// a <c>S+H</c> for anything large.</para>
    /// </remarks>
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

    /// <summary>
    /// Write a geometry as Well-Known Text. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para>The round trip through <c>Read WKT</c> preserves coordinates and geometry type. It does
    /// <b>not</b> preserve SRID, and there is no pin to make it: <c>WKTWriter</c> in NTS 2.6 has no
    /// way to emit one (checked against the type's own members, not assumed), and plain WKT has
    /// nowhere to put it. A geometry written and read back carries whatever SRID the reader's
    /// <c>Factory</c> says — so if the SRID matters, it travels beside the text, not inside it.</para>
    /// <para><b>Z is dropped unless <c>Include Z</c> is on.</b> NTS's writer defaults to two
    /// dimensions and discards Z silently (measured) — a Point built from a <c>CoordinateZ</c>
    /// writes as <c>POINT (1 2)</c>, with the elevation simply gone. That is a real way to lose data
    /// on the way out of a patch, so it is a pin rather than a default nobody looks at. With it on,
    /// the output is <c>POINT Z(1 2 3)</c>, which NTS reads back and most other tools do too.</para>
    /// </remarks>
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
