using NetTopologySuite.IO;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The drawn IOBox fills each polygon on its own (2026-11-14): two polygons of a collection must
/// never share one path, or EvenOdd cuts their overlap out as if it were a hole.
/// </summary>
public class GeometryFillTests
{
    private static global::NetTopologySuite.Geometries.Geometry W(string wkt) => new WKTReader().Read(wkt);

    [Fact]
    public void Two_overlapping_polygons_of_a_collection_are_two_separate_fills()
    {
        // The case the maintainer saw: A and B drawn together through GeometryCollection.
        var parts = GeometryFillNode.PolygonRings(W(
            "GEOMETRYCOLLECTION (POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0)), POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1)))"));
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Single(p));
    }

    [Fact]
    public void A_polygon_keeps_its_holes_in_its_own_fill()
    {
        var parts = GeometryFillNode.PolygonRings(W("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 2, 1 1))"));
        var polygon = Assert.Single(parts);
        Assert.Equal(2, polygon.Length);
    }

    [Fact]
    public void A_multipolygon_and_nested_collections_give_one_fill_per_polygon()
    {
        var parts = GeometryFillNode.PolygonRings(W(
            "GEOMETRYCOLLECTION (MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5))), " +
            "GEOMETRYCOLLECTION (POLYGON ((9 9, 10 9, 10 10, 9 9))), POINT (3 3), LINESTRING (0 0, 9 9))"));
        Assert.Equal(3, parts.Count);
    }

    [Fact]
    public void Points_lines_empties_and_nothing_have_no_fill()
    {
        Assert.Empty(GeometryFillNode.PolygonRings(null));
        Assert.Empty(GeometryFillNode.PolygonRings(W("POLYGON EMPTY")));
        Assert.Empty(GeometryFillNode.PolygonRings(W("LINESTRING (0 0, 1 1)")));
    }

    [Fact]
    public void Y_is_negated_for_Skia_as_in_the_rest_of_the_viewer()
    {
        var shell = Assert.Single(GeometryFillNode.PolygonRings(W("POLYGON ((0 0, 2 0, 2 3, 0 0))")))[0];
        Assert.Equal(-3f, shell[2].Y);
        Assert.Equal(2f, shell[2].X);
    }
}
