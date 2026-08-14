using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace VL.NTS;

/// <summary>
/// The package's default <see cref="GeometryFactory"/> and the defensive copying every creation
/// node depends on. Internal: none of this is a node.
/// </summary>
/// <remarks>
/// <para><b>Why the copying exists.</b> NetTopologySuite geometries are commonly described as
/// immutable, and that is only half true. Operations are non-mutating — <c>Buffer</c> and
/// <c>Union</c> return new geometries — but coordinate <i>storage is shared and writable</i>.
/// Measured against NTS 2.6.0 on 2026-08-14:</para>
/// <list type="bullet">
/// <item><description><c>Coordinate.X</c> has a public setter.</description></item>
/// <item><description><c>factory.CreatePoint(c)</c> followed by <c>c.X = 777</c> moves the point.
/// The default <c>CoordinateArraySequenceFactory</c> wraps the caller's array rather than copying
/// it.</description></item>
/// <item><description><c>geometry.Coordinates[0].X = 555</c> mutates the geometry — that property
/// hands out live references.</description></item>
/// <item><description><c>point.Coordinate</c> is live for the same reason.</description></item>
/// </list>
/// <para>In C# that is a footgun. In VL it is worse: a value flowing into two branches of a patch
/// is <i>the same reference</i>, and there is no <c>readonly</c> to stop the second branch writing
/// through it. A patch that holds a <c>Coordinate</c>, builds a <c>Point</c> from it and later
/// edits that <c>Coordinate</c> would silently move geometry that has already been drawn and
/// handed to another package.</para>
/// <para>So every creation node copies on the way in and every reader node copies on the way out.
/// This is the one thing this package does that the raw API does not, and it is the reason these
/// are hand-written nodes rather than VL's reflection nodes over <c>GeometryFactory</c>.</para>
/// </remarks>
internal static class Defaults
{
    /// <summary>
    /// The factory used when a node's <c>Factory</c> pin is left unconnected.
    /// </summary>
    /// <remarks>
    /// <para><b>SRID 0 — "unset" — and floating precision.</b> This is NetTopologySuite's own
    /// default (<c>GeometryFactory.Default.SRID</c> is 0, measured), and it is the honest one for a
    /// package named after the library: NTS geometry is planar, not inherently geographic. A patch
    /// that means WGS84 says so with a <c>GeometryFactory</c> node.</para>
    /// <para>Deliberately not 4326. Stamping a CRS onto coordinates the package has never seen
    /// would be a claim it cannot check, and mixing SRIDs never throws in NTS (measured: 4326
    /// intersected with 0 returns 4326 silently), so a wrong CRS produces a confident wrong
    /// answer rather than an error.</para>
    /// <para>Shared and static, which is safe: a factory holds no connection, handle or thread, so
    /// the "must be a ProcessNode" rule does not apply to it.</para>
    /// </remarks>
    internal static readonly GeometryFactory Factory = new GeometryFactory(new PrecisionModel(), 0);

    /// <summary>Resolve a possibly unconnected <c>Factory</c> pin.</summary>
    /// <remarks>
    /// Unconnected is the only thing that can mean "the default" — an empty or zero value cannot,
    /// which is rule 8 of <c>docs/RULES.md</c> and was learned at the cost of 444 stray files.
    /// A <c>GeometryFactory</c> pin has no "empty" to confuse it with, so <c>null</c> is
    /// unambiguous here.
    /// </remarks>
    internal static GeometryFactory Or(GeometryFactory? factory) => factory ?? Factory;

    /// <summary>
    /// One <see cref="GeometryFactory"/> per SRID, reused.
    /// </summary>
    /// <remarks>
    /// <para>A <c>public static</c> method is a node that runs <b>every frame</b> — sixty times a
    /// second, from the moment the document opens. So the obvious spelling of the
    /// <c>GeometryFactory</c> node, <c>new GeometryFactory(new PrecisionModel(), srid)</c>, would
    /// allocate a factory and a precision model per frame forever, which is one of the
    /// inefficiencies §24 of the brief names outright.</para>
    /// <para>Caching also makes the node's output <b>reference-stable</b> for a given SRID, which
    /// matters more than the allocation: a stable instance is what lets anything downstream key a
    /// cache off the factory, and it is what keeps <see cref="ReaderFor"/> from building a new
    /// parser every frame.</para>
    /// <para>Bounded in practice — a patch uses one or two SRIDs — and the entries are small. Not a
    /// <c>ProcessNode</c>, because a factory holds no connection, handle or thread; this is the
    /// cheap end of the rule, not an exception to it.</para>
    /// </remarks>
    private static readonly ConcurrentDictionary<int, GeometryFactory> FactoriesBySrid = new();

    /// <summary>Get the shared factory for an SRID, creating it once.</summary>
    internal static GeometryFactory FactoryFor(int srid)
        => srid == 0
            ? Factory
            : FactoriesBySrid.GetOrAdd(srid, static s => new GeometryFactory(new PrecisionModel(), s));

    /// <summary>
    /// One <see cref="WKTReader"/> per factory, reused.
    /// </summary>
    /// <remarks>
    /// <para>Same per-frame reasoning as <see cref="FactoriesBySrid"/>: a reader is configuration
    /// rather than a per-call object, and rebuilding one sixty times a second to parse the same
    /// string is the inefficiency §24 of the brief names. Measured safe to reuse — one instance
    /// parses call after call, including after a call that threw.</para>
    /// <para>A <see cref="ConditionalWeakTable{TKey, TValue}"/> rather than a dictionary so a
    /// factory a patch has stopped using can be collected — the key is held weakly. That matters
    /// because a <c>Factory</c> pin can be fed by something other than the
    /// <c>GeometryFactory</c> node, including a factory built through VL's raw .NET nodes, and a
    /// strong dictionary would pin every one of them for the life of the process.</para>
    /// </remarks>
    private static readonly ConditionalWeakTable<GeometryFactory, WKTReader> ReadersByFactory = new();

    /// <summary>Get the shared WKT reader for a factory, creating it once.</summary>
    /// <remarks>
    /// <para><b>The SRID has to be routed through <see cref="NtsGeometryServices"/>, and that is the
    /// only sanctioned way.</b> Two dead ends, both measured against NTS 2.6.0 rather than assumed:
    /// <c>new WKTReader(factory)</c> does <i>not</i> adopt the factory's SRID — a reader built from a
    /// 4326 factory still returns geometry with SRID <b>-1</b> — and the <c>DefaultSRID</c> property
    /// that would fix that is <c>[Obsolete]</c>, warning that "the ability to set this value after an
    /// instance is created may be removed in a future release". So is the
    /// <c>WKTReader(GeometryFactory)</c> constructor. Building the services explicitly is both
    /// correct and the route NTS asks for.</para>
    /// <para>Without it, <c>Read WKT</c> and the creation nodes would disagree in every patch, and
    /// would disagree using two different numbers that both mean "unset" — -1 and 0.</para>
    /// <para>An <c>SRID=...;</c> prefix in the text still wins, which is correct: the document said
    /// something specific.</para>
    /// <para><c>FixStructure</c> closes unclosed rings, so <c>POLYGON ((0 0, 1 0, 1 1, 0 1))</c>
    /// parses instead of throwing <c>points must form a closed linestring</c> (measured — and the
    /// result is a valid polygon). Deliberately on, to match the <c>LinearRing</c> node, which
    /// closes rings for the same reason: four corners of a rectangle is what a person writes.</para>
    /// </remarks>
    internal static WKTReader ReaderFor(GeometryFactory factory)
        => ReadersByFactory.GetValue(factory, static f => new WKTReader(
            new NtsGeometryServices(f.CoordinateSequenceFactory, f.PrecisionModel, f.SRID))
        {
            FixStructure = true,
        });

    /// <summary>Copy one coordinate, preserving its Z and M ordinates.</summary>
    /// <remarks>
    /// <c>Coordinate.Copy()</c> is virtual and returns the same runtime type, so a
    /// <c>CoordinateZ</c> copies as a <c>CoordinateZ</c> and its Z survives. Verified against
    /// NTS 2.6.0: Z and M both pass through the default factory intact.
    /// </remarks>
    internal static Coordinate CopyOf(Coordinate coordinate) => coordinate.Copy();

    /// <summary>
    /// Copy a sequence of coordinates into a fresh array the caller cannot reach.
    /// </summary>
    /// <remarks>
    /// Both halves matter. A new array stops the patch replacing elements; copying each
    /// <c>Coordinate</c> stops it moving them. Handing NTS the caller's own array would leave the
    /// finished geometry writable from the patch, because the default coordinate sequence factory
    /// stores the array it is given.
    /// <para>A null or empty input yields an empty array, which every NTS factory method turns
    /// into a valid empty geometry rather than throwing (measured).</para>
    /// </remarks>
    internal static Coordinate[] CopyOf(IEnumerable<Coordinate>? coordinates)
    {
        if (coordinates is null)
            return [];

        var copy = new List<Coordinate>(coordinates is ICollection<Coordinate> c ? c.Count : 4);
        foreach (var coordinate in coordinates)
            copy.Add(coordinate.Copy());
        return copy.ToArray();
    }

    /// <summary>
    /// Copy a sequence of coordinates and close it, if it is not closed already.
    /// </summary>
    /// <remarks>
    /// A ring must be closed — NTS throws <c>ArgumentException: points must form a closed
    /// linestring</c> otherwise (measured) — and the natural thing for a patch author to produce
    /// is the four corners of a rectangle, not five. So closing is a real convenience rather than
    /// a guess, and it is documented on the node that does it rather than being silent.
    /// <para>An empty input stays empty: an empty ring is legal, and inventing a point for it
    /// would not be.</para>
    /// </remarks>
    internal static Coordinate[] CopyAndClose(IEnumerable<Coordinate>? coordinates)
    {
        var copy = CopyOf(coordinates);
        if (copy.Length == 0 || copy[0].Equals2D(copy[^1]))
            return copy;

        var closed = new Coordinate[copy.Length + 1];
        copy.CopyTo(closed, 0);
        closed[^1] = copy[0].Copy();
        return closed;
    }
}
