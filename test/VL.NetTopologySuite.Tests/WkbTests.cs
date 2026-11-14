using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// WKB and its hex form (tier 2, 2026-11-14): round trips, the SRID correction, EWKB, Z, and
/// failure on a pin. The hex strings are the ones PostGIS prints for the same geometries.
/// </summary>
public class WkbTests
{
    private const string PointHex = "0101000000000000000000F03F0000000000000040";            // POINT (1 2)
    private const string PointEwkbHex = "0101000020E6100000000000000000F03F0000000000000040"; // SRID=4326;POINT (1 2)
    private const string PointZEwkbHex = "01010000A0E6100000000000000000F03F00000000000000400000000000000840"; // SRID=4326;POINT Z (1 2 3)

    private static global::NetTopologySuite.Geometries.Geometry W(string wkt)
        => IONodes.FromWKT(wkt, null, out _);

    [Theory]
    [InlineData("POINT (13.4 52.5)")]
    [InlineData("LINESTRING (0 0, 1 1, 2 0)")]
    [InlineData("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 2, 1 1))")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")]
    [InlineData("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (0 0, 1 1))")]
    public void Every_geometry_type_round_trips_through_bytes_and_hex(string wkt)
    {
        var geometry = W(wkt);

        var fromBytes = IONodes.FromWKB(IONodes.ToWKB(geometry), null, out var ok1);
        var fromHex = IONodes.FromWKBHex(IONodes.ToWKBHex(geometry), null, out var ok2);

        Assert.True(ok1);
        Assert.True(ok2);
        Assert.True(geometry.EqualsExact(fromBytes));
        Assert.True(geometry.EqualsExact(fromHex));
    }

    [Fact]
    public void The_hex_is_what_PostGIS_prints()
    {
        Assert.Equal(PointHex, IONodes.ToWKBHex(W("POINT (1 2)")));
        Assert.Equal(PointEwkbHex, IONodes.ToWKBHex(SetSrid(W("POINT (1 2)"), 4326), includeSRID: true));
    }

    [Fact]
    public void Plain_WKB_gets_the_Factory_SRID_and_0_when_unconnected_not_minus_1()
    {
        // NTS's own reader stamps -1 (measured), as its WKT reader does.
        Assert.Equal(0, IONodes.FromWKBHex(PointHex, null, out _).SRID);
        Assert.Equal(3857, IONodes.FromWKBHex(PointHex, GeometryNodes.GeometryFactory(3857), out _).SRID);
    }

    [Fact]
    public void The_SRID_inside_EWKB_wins_over_the_Factory()
    {
        var g = IONodes.FromWKBHex(PointEwkbHex, GeometryNodes.GeometryFactory(3857), out var ok);
        Assert.True(ok);
        Assert.Equal(4326, g.SRID);
    }

    [Fact]
    public void Include_SRID_off_writes_no_SRID()
    {
        var bytes = IONodes.ToWKB(SetSrid(W("POINT (1 2)"), 4326));
        Assert.Equal(0, IONodes.FromWKB(bytes, null, out _).SRID);
    }

    [Fact]
    public void Z_is_dropped_unless_Include_Z_is_on()
    {
        var pz = W("POINT Z (1 2 3)");

        Assert.True(double.IsNaN(IONodes.FromWKB(IONodes.ToWKB(pz), null, out _).Coordinate.Z));
        Assert.Equal(3.0, IONodes.FromWKB(IONodes.ToWKB(pz, includeZ: true), null, out _).Coordinate.Z);
    }

    [Fact]
    public void PostGIS_EWKB_with_Z_reads_with_its_SRID_and_Z()
    {
        var g = IONodes.FromWKBHex(PointZEwkbHex, null, out var ok);
        Assert.True(ok);
        Assert.Equal(4326, g.SRID);
        Assert.Equal(3.0, g.Coordinate.Z);
    }

    [Theory]
    [InlineData("0101000000000000000000f03f0000000000000040")]        // lower case
    [InlineData("0x0101000000000000000000F03F0000000000000040")]      // 0x prefix
    [InlineData("\\x0101000000000000000000F03F0000000000000040")]     // PostGIS bytea display
    [InlineData("  0101000000000000000000F03F0000000000000040\r\n")]  // pasted with a line break
    [InlineData("00000000013FF00000000000004000000000000000")]        // big-endian (XDR)
    public void Hex_is_read_in_the_forms_people_paste(string hex)
    {
        var g = IONodes.FromWKBHex(hex, null, out var ok);
        Assert.True(ok);
        Assert.Equal("POINT (1 2)", IONodes.ToWKT(g));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x")]
    [InlineData("zz")]
    [InlineData("010")]   // odd: NTS's own HexToBytes drops the last digit silently (measured)
    [InlineData("01010000")]  // truncated
    public void Bad_hex_reports_failure_instead_of_throwing(string? hex)
    {
        var g = IONodes.FromWKBHex(hex, null, out var ok);
        Assert.False(ok);
        Assert.True(GeometryNodes.IsEmpty(g));
    }

    [Fact]
    public void Bad_or_missing_bytes_report_failure_and_the_reader_still_works_after()
    {
        Assert.True(GeometryNodes.IsEmpty(IONodes.FromWKB(null, null, out var a)));
        IONodes.FromWKB([], null, out var b);
        IONodes.FromWKB([1, 1, 0, 0], null, out var c);
        Assert.False(a);
        Assert.False(b);
        Assert.False(c);

        Assert.Equal("POINT (1 2)", IONodes.ToWKT(IONodes.FromWKBHex(PointHex, null, out var ok)));
        Assert.True(ok);
    }

    [Fact]
    public void A_spread_of_bytes_is_read_like_an_array()
    {
        // FromWKB takes IEnumerable<byte> as CoreLib's FromBytes does, so a Spread<Byte> links in.
        var spread = VL.Lib.Collections.Spread.Create(IONodes.ToWKB(W("POINT (1 2)")));
        var g = IONodes.FromWKB(spread, null, out var ok);
        Assert.True(ok);
        Assert.Equal("POINT (1 2)", IONodes.ToWKT(g));
    }

    [Fact]
    public void Changing_the_input_bytes_afterwards_moves_nothing()
    {
        var bytes = IONodes.ToWKB(W("POINT (1 2)"));
        var g = IONodes.FromWKB(bytes, null, out _);
        bytes[5] = 0xFF;
        Assert.Equal("POINT (1 2)", IONodes.ToWKT(g));
    }

    [Fact]
    public void Nothing_in_gives_nothing_out()
    {
        Assert.Empty(IONodes.ToWKB(null));
        Assert.Equal("", IONodes.ToWKBHex(null));
    }

    private static global::NetTopologySuite.Geometries.Geometry SetSrid(global::NetTopologySuite.Geometries.Geometry g, int srid)
    {
        var copy = g.Copy();
        copy.SRID = srid;
        return copy;
    }
}
