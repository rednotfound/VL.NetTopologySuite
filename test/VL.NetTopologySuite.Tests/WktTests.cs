using VL.NTS;
using Xunit;

namespace VL.NTS.Tests;

/// <summary>
/// WKT reading and writing, including the SRID correction and the failure-on-a-pin behaviour.
/// </summary>
public class WktTests
{
    [Fact]
    public void The_MVP_path_reads_WKT_into_a_native_geometry()
    {
        // Brief §35, second target: WKT -> Read WKT -> Geometry.
        var geometry = IONodes.ReadWKT("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", null, out var success);

        Assert.True(success);
        Assert.IsAssignableFrom<NetTopologySuite.Geometries.Geometry>(geometry);
        Assert.Equal("Polygon", GeometryNodes.GeometryType(geometry));
        Assert.Equal(1.0, GeometryNodes.Area(geometry), 12);
    }

    [Theory]
    [InlineData("POINT (13.4 52.5)", "Point")]
    [InlineData("LINESTRING (0 0, 1 1, 2 0)", "LineString")]
    [InlineData("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", "Polygon")]
    [InlineData("MULTIPOINT ((0 0), (1 1))", "MultiPoint")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", "MultiLineString")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 1, 0 0)))", "MultiPolygon")]
    [InlineData("GEOMETRYCOLLECTION (POINT (0 0))", "GeometryCollection")]
    public void Every_geometry_type_round_trips(string wkt, string expectedType)
    {
        var geometry = IONodes.ReadWKT(wkt, null, out var success);
        Assert.True(success);
        Assert.Equal(expectedType, GeometryNodes.GeometryType(geometry));

        var written = IONodes.WriteWKT(geometry);
        var again = IONodes.ReadWKT(written, null, out var successAgain);

        Assert.True(successAgain);
        Assert.True(geometry.EqualsTopologically(again));
    }

    [Fact]
    public void Coordinate_order_survives_the_round_trip_x_first()
    {
        var point = IONodes.ReadWKT("POINT (13.4 52.5)", null, out _);

        Assert.Equal(13.4, point.Coordinate.X, 12);
        Assert.Equal(52.5, point.Coordinate.Y, 12);
    }

    // ── Failure is a pin ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("POINT (")]
    [InlineData("NONSENSE")]
    [InlineData("POINT (1 2")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Malformed_text_reports_failure_instead_of_throwing(string? wkt)
    {
        // The whole point of the Success pin: half-typed text is the normal state of an IOBox
        // somebody is editing, and a node throwing sixty times a second is not a diagnostic.
        var geometry = IONodes.ReadWKT(wkt, null, out var success);

        Assert.False(success);
        Assert.NotNull(geometry);
        Assert.True(GeometryNodes.IsEmpty(geometry));
    }

    [Fact]
    public void A_reader_still_works_after_a_failure()
    {
        // The reader is shared and reused across frames, so a bad string must not poison it.
        IONodes.ReadWKT("GARBAGE", null, out _);
        var geometry = IONodes.ReadWKT("POINT (9 9)", null, out var success);

        Assert.True(success);
        Assert.Equal(9, geometry.Coordinate.X);
    }

    [Fact]
    public void An_unclosed_polygon_ring_is_accepted_and_closed()
    {
        // Consistent with the LinearRing node, which closes rings for the same reason. Without
        // FixStructure this throws "points must form a closed linestring".
        var geometry = IONodes.ReadWKT("POLYGON ((0 0, 1 0, 1 1, 0 1))", null, out var success);

        Assert.True(success);
        Assert.Equal(1.0, GeometryNodes.Area(geometry), 12);
        Assert.True(GeometryNodes.IsValid(geometry, out _, out _));
    }

    // ── SRID ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Read_WKT_agrees_with_the_creation_nodes_about_unset_SRID()
    {
        // The correction that matters: NTS's own reader stamps -1 here regardless of the factory,
        // so a patch would otherwise see -1 from typed geometry and 0 from built geometry.
        var read = IONodes.ReadWKT("POINT (1 2)", null, out _);
        var built = GeometryNodes.Point(GeometryNodes.Coordinate(1, 2));

        Assert.Equal(0, GeometryNodes.SRID(read));
        Assert.Equal(GeometryNodes.SRID(built), GeometryNodes.SRID(read));
    }

    [Fact]
    public void Read_WKT_takes_its_SRID_from_the_factory_pin()
    {
        var geometry = IONodes.ReadWKT("POINT (1 2)", GeometryNodes.GeometryFactory(4326), out _);

        Assert.Equal(4326, GeometryNodes.SRID(geometry));
    }

    [Fact]
    public void An_SRID_prefix_in_the_text_wins_over_the_factory()
    {
        var geometry = IONodes.ReadWKT(
            "SRID=32654;POINT (1 2)", GeometryNodes.GeometryFactory(4326), out var success);

        Assert.True(success);
        Assert.Equal(32654, GeometryNodes.SRID(geometry));
    }

    [Fact]
    public void Write_WKT_does_not_carry_SRID_and_does_not_pretend_to()
    {
        var geometry = GeometryNodes.Point(
            GeometryNodes.Coordinate(1, 2), GeometryNodes.GeometryFactory(4326));

        var written = IONodes.WriteWKT(geometry);

        Assert.DoesNotContain("4326", written);
        Assert.Equal("POINT (1 2)", written);
    }

    // ── Z ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Write_WKT_drops_Z_by_default_and_keeps_it_when_asked()
    {
        var point = GeometryNodes.Point(GeometryNodes.CoordinateZ(1, 2, 3));

        // Documented as a real way to lose data, so it is pinned down rather than trusted.
        Assert.Equal("POINT (1 2)", IONodes.WriteWKT(point));
        Assert.Contains("3", IONodes.WriteWKT(point, includeZ: true));
    }

    [Fact]
    public void Z_survives_a_round_trip_when_it_is_written()
    {
        var point = GeometryNodes.Point(GeometryNodes.CoordinateZ(1, 2, 3));

        var again = IONodes.ReadWKT(IONodes.WriteWKT(point, includeZ: true), null, out var success);

        Assert.True(success);
        Assert.Equal(3, again.Coordinate.Z);
    }

    [Fact]
    public void Write_WKT_survives_an_unconnected_pin()
    {
        Assert.Equal("", IONodes.WriteWKT(null));
    }
}
