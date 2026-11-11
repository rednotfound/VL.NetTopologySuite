using System.Linq;
using NetTopologySuite.Geometries;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The model behind GeometryEditor's window: a sketch is parts; the written type follows them;
/// every editable WKT round-trips exactly; edits return new sketches; what cannot be edited
/// without loss is refused.
/// </summary>
public class GeometrySketchTests
{
    private static Coordinate C(double x, double y) => new(x, y);

    private static readonly Coordinate[] Square = { C(0, 0), C(2, 0), C(2, 2), C(0, 2) };

    private static GeometrySketch One(SketchPart part) => GeometrySketch.Create(new[] { part });

    [Theory]
    [InlineData("POINT (1 2)")]
    [InlineData("LINESTRING (0 0, 1 1, 2 0)")]
    [InlineData("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))")]
    [InlineData("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0), (1 1, 2 1, 2 2, 1 1), (5 5, 6 5, 6 6, 5 5))")]
    [InlineData("MULTIPOINT ((1 2), (3 4))")]
    [InlineData("MULTIPOINT ((1 2))")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)))")]
    [InlineData("GEOMETRYCOLLECTION (POINT (1 1), LINESTRING (0 0, 1 1), POLYGON ((0 0, 1 0, 1 1, 0 0)))")]
    [InlineData("GEOMETRYCOLLECTION (POINT (1 1), POINT (2 2))")]
    [InlineData("POINT EMPTY")]
    [InlineData("LINESTRING EMPTY")]
    [InlineData("POLYGON EMPTY")]
    [InlineData("MULTIPOLYGON EMPTY")]
    [InlineData("GEOMETRYCOLLECTION EMPTY")]
    public void Every_editable_WKT_round_trips(string wkt)
    {
        Assert.True(GeometrySketch.TryFromWKT(wkt, out var s));
        Assert.Equal(wkt, s.ToWKT());
    }

    [Theory]
    [InlineData("POINT Z (1 2 3)")]
    [InlineData("LINEARRING (0 0, 1 0, 1 1, 0 0)")]
    [InlineData("GEOMETRYCOLLECTION (MULTIPOINT ((1 1), (2 2)))")]
    [InlineData("GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (1 1)))")]
    [InlineData("not wkt at all")]
    public void What_cannot_be_edited_without_loss_is_refused(string wkt)
        => Assert.False(GeometrySketch.TryFromWKT(wkt, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_an_empty_sketch(string? wkt)
    {
        Assert.True(GeometrySketch.TryFromWKT(wkt, out var s));
        Assert.Equal(0, s.PartCount);
        Assert.Equal("GEOMETRYCOLLECTION EMPTY", s.ToWKT());
    }

    [Fact]
    public void The_written_type_follows_the_parts()
    {
        var s = GeometrySketch.Empty.AddPart(PartKind.Point, C(0, 0));
        Assert.Equal("POINT (0 0)", s.ToWKT());
        s = s.AddPart(PartKind.Point, C(1, 1));
        Assert.Equal("MULTIPOINT ((0 0), (1 1))", s.ToWKT());
        s = s.AddPart(GeometrySketch.LinePart(new[] { C(0, 0), C(3, 0) }));
        Assert.Equal("GEOMETRYCOLLECTION (POINT (0 0), POINT (1 1), LINESTRING (0 0, 3 0))", s.ToWKT());
    }

    [Fact]
    public void A_polygon_in_progress_is_the_lower_dimension_it_is()
    {
        var s = GeometrySketch.Empty.AddPart(PartKind.Polygon, C(0, 0));
        Assert.Equal("POINT (0 0)", s.ToWKT());
        s = s.Append(0, 0, C(1, 0));
        Assert.Equal("LINESTRING (0 0, 1 0)", s.ToWKT());
        s = s.Append(0, 0, C(1, 1));
        Assert.Equal("POLYGON ((0 0, 1 0, 1 1, 0 0))", s.ToWKT());
        Assert.True(s.Part(0).IsComplete);
    }

    [Fact]
    public void A_hole_in_progress_is_left_out_until_it_has_three_vertices()
    {
        var s = One(GeometrySketch.PolygonPart(new[] { C(0, 0), C(9, 0), C(9, 9), C(0, 9) }))
            .AddHole(0, C(1, 1)).Append(0, 1, C(2, 1));
        Assert.Equal("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0))", s.ToWKT());
        Assert.False(s.Part(0).IsComplete);
        s = s.Append(0, 1, C(2, 2));
        Assert.Equal("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0), (1 1, 2 1, 2 2, 1 1))", s.ToWKT());
        Assert.True(s.Part(0).IsComplete);
    }

    [Fact]
    public void A_hole_goes_only_into_a_polygon()
    {
        var line = One(GeometrySketch.LinePart(new[] { C(0, 0), C(1, 1) }));
        Assert.Same(line, line.AddHole(0, C(0.5, 0.5)));
    }

    [Fact]
    public void A_point_part_takes_no_second_vertex()
    {
        var s = GeometrySketch.Empty.AddPart(PartKind.Point, C(0, 0));
        Assert.Same(s, s.Append(0, 0, C(1, 1)));
    }

    [Fact]
    public void Removing_the_last_vertex_of_a_hole_removes_the_hole_and_of_a_shell_the_part()
    {
        var s = One(GeometrySketch.PolygonPart(Square)).AddHole(0, C(1, 1));
        Assert.Equal(2, s.Part(0).RingCount);
        s = s.Remove(new VertexRef(0, 1, 0));
        Assert.Equal(1, s.Part(0).RingCount);

        var p = GeometrySketch.Empty.AddPart(PartKind.Point, C(0, 0));
        Assert.Equal(0, p.Remove(new VertexRef(0, 0, 0)).PartCount);
    }

    [Fact]
    public void An_edit_returns_a_new_sketch_and_leaves_the_old_one()
    {
        var a = One(GeometrySketch.PolygonPart(Square));
        var b = a.Move(new VertexRef(0, 0, 0), C(-1, -1));
        Assert.NotSame(a, b);
        Assert.Equal(0, a[new VertexRef(0, 0, 0)].X);
        Assert.Equal(-1, b[new VertexRef(0, 0, 0)].X);
    }

    [Fact]
    public void An_edit_that_changes_nothing_returns_the_same_sketch()
    {
        var a = One(GeometrySketch.PolygonPart(Square));
        Assert.Same(a, a.Move(new VertexRef(0, 0, 99), C(5, 5)));
        Assert.Same(a, a.Remove(new VertexRef(3, 0, 0)));
        Assert.Same(a, a.RemovePart(-1));
        Assert.Same(a, a.Insert(new VertexRef(0, 0, 99), C(5, 5)));
    }

    [Fact]
    public void The_sketch_copies_coordinates_in_and_out()
    {
        var c = C(1, 1);
        var s = GeometrySketch.Empty.AddPart(PartKind.Point, c);
        c.X = 99;
        var v = new VertexRef(0, 0, 0);
        Assert.Equal(1, s[v].X);
        s[v].X = 42;
        Assert.Equal(1, s[v].X);
        s.Part(0).Ring(0)[0].X = 42;
        Assert.Equal(1, s[v].X);
        s.ToGeometry().Coordinates[0].X = 42;
        Assert.Equal(1, s[v].X);
    }

    [Fact]
    public void The_nearest_vertex_is_found_across_parts_and_rings_within_tolerance_only()
    {
        var s = One(GeometrySketch.PolygonPart(new[] { C(0, 0), C(9, 0), C(9, 9), C(0, 9) }, new[] { C(1, 1), C(2, 1), C(2, 2) }))
            .AddPart(PartKind.Point, C(20, 20));
        Assert.Equal(new VertexRef(0, 1, 2), s.NearestVertex(C(2.1, 2.1), 0.5));
        Assert.Equal(new VertexRef(1, 0, 0), s.NearestVertex(C(20, 20.2), 0.5));
        Assert.Null(s.NearestVertex(C(5, 5), 0.5));
    }

    [Fact]
    public void A_polygon_s_closing_edge_can_take_a_new_vertex_and_a_line_has_none()
    {
        var s = One(GeometrySketch.PolygonPart(Square));
        Assert.Equal(new VertexRef(0, 0, 4), s.NearestEdge(C(-0.1, 1), 0.5));
        var t = s.Insert(new VertexRef(0, 0, 1), C(1, 0));
        Assert.Equal("POLYGON ((0 0, 1 0, 2 0, 2 2, 0 2, 0 0))", t.ToWKT());

        var line = One(GeometrySketch.LinePart(new[] { C(0, 0), C(2, 0), C(2, 2) }));
        Assert.Null(line.NearestEdge(C(1, 1), 0.2));
    }

    [Fact]
    public void The_part_under_the_mouse_is_the_last_drawn_and_a_hole_is_not_inside()
    {
        var s = One(GeometrySketch.PolygonPart(new[] { C(0, 0), C(9, 0), C(9, 9), C(0, 9) }, new[] { C(3, 3), C(6, 3), C(6, 6), C(3, 6) }))
            .AddPart(GeometrySketch.PolygonPart(new[] { C(1, 1), C(2, 1), C(2, 2), C(1, 2) }));
        Assert.Equal(1, s.PartAt(C(1.5, 1.5), 0.1));
        Assert.Equal(0, s.PartAt(C(8, 8), 0.1));
        Assert.Equal(-1, s.PartAt(C(4.5, 4.5), 0.1));
        Assert.Equal(-1, s.PolygonAt(C(4.5, 4.5)));
        Assert.Equal(0, s.PolygonAt(C(8, 8)));
    }

    [Fact]
    public void Moving_a_part_moves_every_ring_of_it_and_nothing_else()
    {
        var s = One(GeometrySketch.PolygonPart(Square, new[] { C(0.5, 0.5), C(1, 0.5), C(1, 1) }))
            .AddPart(PartKind.Point, C(5, 5))
            .MovePart(0, 10, 0);
        Assert.Equal("GEOMETRYCOLLECTION (POLYGON ((10 0, 12 0, 12 2, 10 2, 10 0), (10.5 0.5, 11 0.5, 11 1, 10.5 0.5)), POINT (5 5))",
            s.ToWKT());
    }

    [Fact]
    public void A_read_multi_with_one_part_keeps_writing_multi_and_drawing_more_keeps_it()
    {
        Assert.True(GeometrySketch.TryFromWKT("MULTIPOINT ((1 1))", out var s));
        s = s.AddPart(PartKind.Point, C(2, 2));
        Assert.Equal("MULTIPOINT ((1 1), (2 2))", s.ToWKT());
    }

    [Fact]
    public void Deleting_every_part_writes_the_empty_form_of_what_was_read()
    {
        Assert.True(GeometrySketch.TryFromWKT("POLYGON ((0 0, 2 0, 2 2, 0 0))", out var s));
        Assert.Equal("POLYGON EMPTY", s.RemovePart(0).ToWKT());
    }

    [Fact]
    public void Removing_the_last_vertex_while_drawing_steps_back()
    {
        var s = GeometrySketch.Empty.AddPart(PartKind.Line, C(0, 0)).Append(0, 0, C(1, 0)).Append(0, 0, C(1, 1));
        Assert.Equal("LINESTRING (0 0, 1 0)", s.RemoveLast(0, 0).ToWKT());
        Assert.Equal(2, s.RemoveLast(0, 0).Part(0).VertexCount(0));
        Assert.Equal(3, s.VertexCount);
        Assert.Equal(2, s.Parts.Single().Ring(0).Length - 1);
    }
}
