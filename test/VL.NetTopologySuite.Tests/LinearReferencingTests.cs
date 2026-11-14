using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// Interpolate, Locate and Substring (tier 2, 2026-11-14): places on a line as a fraction from 0 to 1.
/// </summary>
public class LinearReferencingTests
{
    // 15 long: 10 east, then 5 north.
    private const string L = "LINESTRING (0 0, 10 0, 10 5)";

    private static global::NetTopologySuite.Geometries.Geometry W(string wkt)
        => IONodes.FromWKT(wkt, null, out _);

    [Theory]
    [InlineData(0, "POINT (0 0)")]
    [InlineData(0.5, "POINT (7.5 0)")]
    [InlineData(0.8, "POINT (10 2)")]
    [InlineData(1, "POINT (10 5)")]
    public void Interpolate_gives_the_point_at_a_fraction_of_the_length(double position, string expected)
        => Assert.Equal(expected, IONodes.ToWKT(OperationNodes.Interpolate(W(L), position)));

    [Theory]
    [InlineData(-0.5, "POINT (0 0)")]
    [InlineData(7, "POINT (10 5)")]
    [InlineData(double.NaN, "POINT (0 0)")]
    public void A_fraction_outside_0_to_1_stops_at_the_ends(double position, string expected)
        // NTS itself counts a negative length from the end (measured): a surprise in a patch.
        => Assert.Equal(expected, IONodes.ToWKT(OperationNodes.Interpolate(W(L), position)));

    [Fact]
    public void A_polygon_is_walked_along_its_outline()
    {
        // 16 round: a quarter of the way is the first corner after the start.
        var square = W("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))");
        Assert.Equal("POINT (4 0)", IONodes.ToWKT(OperationNodes.Interpolate(square, 0.25)));
        Assert.Equal("POINT (4 4)", IONodes.ToWKT(OperationNodes.Interpolate(square, 0.5)));
    }

    [Fact]
    public void A_multilinestring_is_walked_part_after_part()
        => Assert.Equal("POINT (5 5)",
            IONodes.ToWKT(OperationNodes.Interpolate(W("MULTILINESTRING ((0 0, 10 0), (0 5, 10 5))"), 0.75)));

    [Theory]
    [InlineData("POINT (1 1)")]
    [InlineData("LINESTRING EMPTY")]
    [InlineData("GEOMETRYCOLLECTION (LINESTRING (0 0, 1 0), POINT (5 5))")]
    public void Nothing_to_walk_gives_an_empty_point_not_a_throw(string wkt)
        // NTS throws on each of these: InvalidCast, IndexOutOfRange, "Lineal geometry is required".
        => Assert.True(OperationNodes.Interpolate(W(wkt), 0.5).IsEmpty);

    [Fact]
    public void Nothing_in_gives_an_empty_point()
        => Assert.True(OperationNodes.Interpolate(null, 0.5).IsEmpty);

    [Fact]
    public void Z_is_interpolated_and_the_SRID_kept()
    {
        var line = GeometryNodes.SetSRID(W("LINESTRING Z (0 0 0, 10 0 10)"), 4326);
        var p = OperationNodes.Interpolate(line, 0.3);
        Assert.Equal(3.0, p.Coordinate.Z, 9);
        Assert.Equal(4326, p.SRID);
    }

    [Fact]
    public void The_point_at_the_end_is_a_copy_not_the_lines_own_coordinate()
    {
        // ExtractPoint at the end hands out the input's own Coordinate (measured).
        var line = W(L);
        var p = OperationNodes.Interpolate(line, 1);
        p.Coordinate.X = 99;
        Assert.Equal("LINESTRING (0 0, 10 0, 10 5)", IONodes.ToWKT(line));
    }

    [Theory]
    [InlineData("POINT (7 3)", 7.0 / 15)]      // above the first leg
    [InlineData("POINT (12 4)", 14.0 / 15)]    // east of the second leg
    [InlineData("POINT (-5 -5)", 0)]           // before the start
    [InlineData("POINT (10 9)", 1)]            // past the end
    public void Locate_gives_the_fraction_of_the_nearest_place(string point, double expected)
        => Assert.Equal(expected, OperationNodes.Locate(W(L), W(point)), 9);

    [Fact]
    public void Locate_then_Interpolate_snaps_a_point_onto_the_line()
    {
        var line = W(L);
        var t = OperationNodes.Locate(line, W("POINT (7 3)"));
        Assert.Equal("POINT (7 0)", IONodes.ToWKT(OperationNodes.Interpolate(line, t)));
    }

    [Fact]
    public void Locate_takes_anything_else_at_its_centroid()
        => Assert.Equal(0.2, OperationNodes.Locate(W(L), W("POLYGON ((2 1, 4 1, 4 3, 2 3, 2 1))")), 9);

    [Fact]
    public void Locate_with_nothing_gives_0()
    {
        Assert.Equal(0, OperationNodes.Locate(null, W("POINT (1 1)")));
        Assert.Equal(0, OperationNodes.Locate(W(L), null));
        Assert.Equal(0, OperationNodes.Locate(W("LINESTRING (1 1, 1 1)"), W("POINT (5 5)")));
        Assert.Equal(0, OperationNodes.Locate(W("POINT (0 0)"), W("POINT (5 5)")));
    }

    [Theory]
    [InlineData(0, 1, "LINESTRING (0 0, 10 0, 10 5)")]
    [InlineData(0.2, 0.8, "LINESTRING (3 0, 10 0, 10 2)")]
    [InlineData(0.8, 0.2, "LINESTRING (10 2, 10 0, 3 0)")]   // reversed
    [InlineData(0, 0.4, "LINESTRING (0 0, 6 0)")]
    [InlineData(-3, 9, "LINESTRING (0 0, 10 0, 10 5)")]      // clamped
    public void Substring_gives_the_piece_between_two_fractions(double start, double end, string expected)
        => Assert.Equal(expected, IONodes.ToWKT(OperationNodes.Substring(W(L), start, end)));

    [Fact]
    public void Substring_of_a_polygon_follows_its_outline()
        => Assert.Equal("LINESTRING (0 0, 4 0, 4 4)",
            IONodes.ToWKT(OperationNodes.Substring(W("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))"), 0, 0.5)));

    [Fact]
    public void Substring_owns_its_coordinates_and_keeps_the_SRID()
    {
        var line = GeometryNodes.SetSRID(W(L), 4326);
        var piece = OperationNodes.Substring(line, 0, 1)!;
        piece.Coordinates[1].X = 99;
        Assert.Equal("LINESTRING (0 0, 10 0, 10 5)", IONodes.ToWKT(line));
        Assert.Equal(4326, piece.SRID);
    }

    [Fact]
    public void Substring_of_nothing_to_walk_is_an_empty_line()
    {
        Assert.Null(OperationNodes.Substring(null));
        Assert.True(OperationNodes.Substring(W("POINT (1 1)"))!.IsEmpty);
    }
}
