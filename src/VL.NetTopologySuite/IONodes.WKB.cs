using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// WKB - Well-Known Binary - is the form a geometry takes in a database column (PostGIS, SpatiaLite,
// GeoPackage), a shapefile-free binary exchange, or a message on the wire. Tier 2, 2026-11-14.
//
// Four nodes, in vvvv's own shape (measured over the 7.4 install): CoreLib's String ToBytes outputs
// a byte array and its FromBytes takes IEnumerable<Byte>, so FromWKB takes IEnumerable<byte> (a
// Spread<Byte>, a byte array, anything) and ToWKB outputs byte[]. The hex text PostGIS prints
// (ST_AsHexEWKB, and what a bytea column shows) is a version of each, `FromWKB (Hex)` /
// `ToWKB (Hex)`, as CoreLib has `ToBytes (Base64)`. CoreLib has no bytes-to-hex node to chain
// instead (its ToHexString takes an Int32).
//
// Measured against NetTopologySuite 2.6.0 (scratch probe, 2026-11-14):
// - A plain WKB read gives SRID -1, exactly as WKTReader does, so the reader is built through
//   NtsGeometryServices with the Factory pin's SRID - the same correction as FromWKT.
// - EWKB (PostGIS's form, SRID inside) is read with its SRID, which wins over the Factory pin's,
//   as an SRID=...; prefix wins in FromWKT. A collection's parts get it too.
// - The default writer is little-endian, writes no SRID and drops Z silently: POINT Z (1 2 3)
//   comes back as POINT (1 2). Hence the Include SRID and Include Z pins.
// - With Z on, NTS writes the ISO type code (1001 for a Z point). With SRID and Z both on it writes
//   the ISO code plus EWKB's flag bits (01E90300A0...), where PostGIS writes 01010000A0. NTS reads
//   both, and reads PostGIS's. Not measured against PostGIS itself, so the remark does not promise it.
// - Both byte orders are read; hex is read in either case.
// - WKBReader.HexToBytes throws on 0x or junk, and an odd-length string loses its last digit
//   SILENTLY ("010" is one byte). So hex is parsed here: whitespace trimmed, a 0x or \x prefix
//   (PostGIS's bytea display) dropped, an odd length is a failure.
// - Truncated or empty input throws ParseException; trailing bytes after a geometry are ignored.
//   A reader still works after a throw, so it is shared per factory like the WKT one.
// - The geometry read owns its coordinates: changing the input bytes afterwards moves nothing.

public static partial class IONodes
{
    private static readonly ConditionalWeakTable<GeometryFactory, WKBReader> WkbReadersByFactory = new();

    private static WKBReader WkbReaderFor(GeometryFactory factory)
        => WkbReadersByFactory.GetValue(factory, static f => new WKBReader(
            new NtsGeometryServices(f.CoordinateSequenceFactory, f.PrecisionModel, f.SRID)));

    /// <summary>
    /// Makes a geometry from Well-Known Binary, as stored in a database.
    /// </summary>
    /// <remarks>
    /// PostGIS's EWKB is read too, and the SRID inside it wins over the Factory's.
    /// </remarks>
    /// <param name="wkb">The bytes to read.</param>
    /// <param name="factory">Sets the SRID, 0 when unconnected.</param>
    /// <param name="success">False until the bytes read; the result is then an empty geometry.</param>
    [Name("FromWKB")]
    public static Geometry FromWKB(
        [Pin(Name = "WKB")] IEnumerable<byte>? wkb,
        GeometryFactory? factory,
        out bool success)
        => Read(wkb as byte[] ?? wkb?.ToArray(), factory, out success);

    /// <summary>
    /// Makes a geometry from Well-Known Binary written as hex text, as PostGIS prints it.
    /// </summary>
    /// <remarks>
    /// Upper or lower case, with or without a 0x or \x prefix. The SRID inside EWKB wins over the Factory's.
    /// </remarks>
    /// <param name="hex">The hex text to read, such as 0101000000000000000000F03F0000000000000040.</param>
    /// <param name="factory">Sets the SRID, 0 when unconnected.</param>
    /// <param name="success">False until the text reads; the result is then an empty geometry.</param>
    [Name("FromWKB (Hex)")]
    public static Geometry FromWKBHex(
        [Pin(Name = "Hex")] string? hex,
        GeometryFactory? factory,
        out bool success)
        => Read(BytesOfHex(hex), factory, out success);

    // Failure is a pin, as in FromWKT: half-typed hex in an IOBox is the normal state, not an error.
    private static Geometry Read(byte[]? bytes, GeometryFactory? factory, out bool success)
    {
        var f = Defaults.Or(factory);
        if (bytes is null || bytes.Length == 0)
        {
            success = false;
            return f.CreatePoint();
        }

        try
        {
            var geometry = WkbReaderFor(f).Read(bytes);
            success = geometry is not null;
            return geometry ?? f.CreatePoint();
        }
        catch
        {
            // ParseException for truncated input, ArgumentException for a geometry that reads and is
            // then illegal; from the patch's side one problem.
            success = false;
            return f.CreatePoint();
        }
    }

    /// <summary>The bytes of a hex string, or null when it is not one. Internal for the tests.</summary>
    internal static byte[]? BytesOfHex(string? hex)
    {
        if (hex is null)
            return null;

        var s = hex.AsSpan().Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("\\x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        if (s.Length == 0 || s.Length % 2 != 0)
            return null;

        try
        {
            return Convert.FromHexString(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // A writer per call, as in ToWKT: its configuration is the pins, and a shared writer would have
    // to be changed per call. Little-endian (NDR), what PostGIS writes; both orders read back.
    private static byte[] Write(Geometry geometry, bool includeSRID, bool includeZ)
        => new WKBWriter(ByteOrder.LittleEndian, includeSRID, includeZ).Write(geometry);

    /// <summary>
    /// Turns a geometry into Well-Known Binary.
    /// </summary>
    /// <remarks>
    /// Include SRID writes PostGIS's EWKB; plain WKB has no place for the SRID.
    /// </remarks>
    /// <param name="geometry">The geometry to write.</param>
    /// <param name="includeSRID">Writes the SRID inside, as PostGIS's EWKB.</param>
    /// <param name="includeZ">Writes the elevation too. Off, Z is dropped.</param>
    [Name("ToWKB")]
    public static byte[] ToWKB(
        Geometry? geometry,
        [Pin(Name = "Include SRID")] bool includeSRID = false,
        bool includeZ = false)
        => geometry is null ? [] : Write(geometry, includeSRID, includeZ);

    /// <summary>
    /// Turns a geometry into Well-Known Binary written as hex text, as PostGIS prints it.
    /// </summary>
    /// <remarks>
    /// Include SRID writes PostGIS's EWKB; plain WKB has no place for the SRID.
    /// </remarks>
    /// <param name="geometry">The geometry to write.</param>
    /// <param name="includeSRID">Writes the SRID inside, as PostGIS's EWKB.</param>
    /// <param name="includeZ">Writes the elevation too. Off, Z is dropped.</param>
    [Name("ToWKB (Hex)")]
    public static string ToWKBHex(
        Geometry? geometry,
        [Pin(Name = "Include SRID")] bool includeSRID = false,
        bool includeZ = false)
        => geometry is null ? "" : Convert.ToHexString(Write(geometry, includeSRID, includeZ));
}
