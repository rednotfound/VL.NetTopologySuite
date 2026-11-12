using NetTopologySuite.Geometries;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The geometry editor window's tools as gestures: what is previewed, what is committed (written
/// into the document), and what the sketch is afterwards.
/// </summary>
public class SketchSessionTests
{
    private const double Tol = 0.1;

    private static Coordinate C(double x, double y) => new(x, y);

    private static SketchSession With(string wkt)
    {
        var s = new SketchSession();
        Assert.True(GeometrySketch.TryFromWKT(wkt, out var sketch));
        s.Sync(sketch);
        return s;
    }

    private static void Click(SketchSession s, double x, double y, SketchOutcome expected = SketchOutcome.Preview)
    {
        Assert.Equal(expected, s.LeftDown(C(x, y), Tol));
        s.LeftUp();
    }

    [Fact]
    public void The_point_tool_commits_every_click_and_two_points_are_a_multipoint()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Point);
        Click(s, 1, 1, SketchOutcome.Commit);
        Click(s, 2, 2, SketchOutcome.Commit);
        Assert.Equal("MULTIPOINT ((1 1), (2 2))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_line_is_previewed_while_drawn_and_committed_once_on_Enter()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 1, 0);
        Click(s, 1, 1);
        Assert.True(s.IsDrawing);
        Assert.Equal(SketchOutcome.Commit, s.Enter());
        Assert.False(s.IsDrawing);
        Assert.Equal("LINESTRING (0 0, 1 0, 1 1)", s.Sketch.ToWKT());
        Assert.Equal(0, s.SelectedPart);
    }

    [Fact]
    public void A_double_click_finishes_and_its_extra_vertex_is_dropped()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 2, 0);
        Click(s, 2, 0); // the double-click's second press
        Assert.Equal(SketchOutcome.Commit, s.DoubleClick(C(2, 0), Tol));
        Assert.Equal("LINESTRING (0 0, 2 0)", s.Sketch.ToWKT());
    }

    [Fact]
    public void Clicking_the_first_vertex_closes_a_polygon()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Polygon);
        Click(s, 0, 0);
        Click(s, 2, 0);
        Click(s, 2, 2);
        Click(s, 0.02, 0.02, SketchOutcome.Commit);
        Assert.Equal("POLYGON ((0 0, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_polygon_finished_with_two_points_is_discarded_with_a_hint()
    {
        var s = With("POINT (5 5)");
        s.SetTool(SketchTool.Polygon);
        Click(s, 0, 0);
        Click(s, 2, 0);
        Assert.Equal(SketchOutcome.Preview, s.Enter());
        Assert.Equal("POINT (5 5)", s.Sketch.ToWKT());
        Assert.NotNull(s.Hint);
    }

    [Fact]
    public void Escape_drops_the_part_being_drawn()
    {
        var s = With("POINT (5 5)");
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 1, 0);
        s.Escape();
        Assert.False(s.IsDrawing);
        Assert.Equal("POINT (5 5)", s.Sketch.ToWKT());
    }

    [Fact]
    public void Backspace_takes_the_last_drawn_vertex_back()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 1, 0);
        Click(s, 9, 9);
        s.Backspace();
        s.Enter();
        Assert.Equal("LINESTRING (0 0, 1 0)", s.Sketch.ToWKT());
    }

    [Fact]
    public void Switching_tool_finishes_the_part_being_drawn()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 1, 0);
        Assert.Equal(SketchOutcome.Commit, s.SetTool(SketchTool.Select));
        Assert.Equal("LINESTRING (0 0, 1 0)", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_hole_starts_only_inside_a_polygon_and_is_written_when_closed()
    {
        var s = With("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0))");
        s.SetTool(SketchTool.Hole);
        Assert.Equal(SketchOutcome.None, s.LeftDown(C(20, 20), Tol));
        Assert.NotNull(s.Hint);
        Click(s, 1, 1);
        Click(s, 2, 1);
        Click(s, 2, 2);
        Assert.Equal(SketchOutcome.Commit, s.Enter());
        Assert.Equal("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0), (1 1, 2 1, 2 2, 1 1))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Drawing_a_second_polygon_makes_a_multipolygon()
    {
        var s = With("POLYGON ((0 0, 1 0, 1 1, 0 0))");
        s.SetTool(SketchTool.Polygon);
        Click(s, 5, 5);
        Click(s, 6, 5);
        Click(s, 6, 6);
        s.Enter();
        Assert.Equal("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Drawing_a_line_next_to_a_polygon_makes_a_collection()
    {
        var s = With("POLYGON ((0 0, 1 0, 1 1, 0 0))");
        s.SetTool(SketchTool.Line);
        Click(s, 5, 5);
        Click(s, 6, 5);
        s.Enter();
        Assert.Equal("GEOMETRYCOLLECTION (POLYGON ((0 0, 1 0, 1 1, 0 0)), LINESTRING (5 5, 6 5))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_new_vertex_near_an_existing_one_lands_on_it()
    {
        var s = With("POLYGON ((0 0, 1 0, 1 1, 0 0))");
        s.SetTool(SketchTool.Line);
        Click(s, 1.05, 1.02);
        Click(s, 3, 3);
        s.Enter();
        Assert.Equal("GEOMETRYCOLLECTION (POLYGON ((0 0, 1 0, 1 1, 0 0)), LINESTRING (1 1, 3 3))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Dragging_a_vertex_previews_and_commits_once_on_release()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        Assert.Equal(SketchOutcome.None, s.LeftDown(C(2, 2), Tol));
        Assert.Equal(SketchOutcome.Preview, s.Move(C(3, 3)));
        Assert.Equal(SketchOutcome.Preview, s.Move(C(4, 4)));
        Assert.Equal(SketchOutcome.Commit, s.LeftUp());
        Assert.Equal("POLYGON ((0 0, 2 0, 4 4, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_click_on_a_vertex_without_moving_commits_nothing()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        s.LeftDown(C(2, 2), Tol);
        Assert.Equal(SketchOutcome.None, s.LeftUp());
    }

    [Fact]
    public void In_Select_a_click_on_an_edge_only_selects_and_a_double_click_inserts_a_vertex()
    {
        // The maintainer, 2026-11-11: "in Select mode I can still draw" - a click inserted a vertex.
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        Assert.Equal(SketchOutcome.None, s.LeftDown(C(1, 0.01), Tol));
        Assert.Equal(SketchOutcome.None, s.LeftUp());
        Assert.Equal(0, s.SelectedPart);
        Assert.Equal("POLYGON ((0 0, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
        Assert.Equal(SketchOutcome.Commit, s.DoubleClick(C(1, 0.01), Tol));
        Assert.Equal("POLYGON ((0 0, 1 0.01, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_click_on_empty_space_in_Select_adds_nothing()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        Assert.Equal(SketchOutcome.None, s.LeftDown(C(7, 7), Tol));
        s.LeftUp();
        Assert.Equal(SketchOutcome.None, s.DoubleClick(C(7, 7), Tol));
        Assert.Equal(-1, s.SelectedPart);
        Assert.Equal("POLYGON ((0 0, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void With_snap_the_real_position_hits_and_the_snapped_one_is_placed()
    {
        // The maintainer, 2026-11-11: with Snap on, nothing could be selected - the snapped
        // point (here 2 2 for a press at 2.3 2.3) was used to hit-test too.
        var s = With("POLYGON ((0 0, 2 0, 2.3 2.3, 0 0))");
        s.LeftDown(C(2.3, 2.3), Tol, place: C(2, 2));
        Assert.Equal(0, s.SelectedPart);
        Assert.NotNull(s.DraggedVertex);
        s.Move(C(3, 3));
        s.LeftUp();
        Assert.Equal("POLYGON ((0 0, 2 0, 3 3, 0 0))", s.Sketch.ToWKT());

        var p = new SketchSession();
        p.SetTool(SketchTool.Point);
        p.LeftDown(C(0.97, 1.04), Tol, place: C(1, 1));
        Assert.Equal("POINT (1 1)", p.Sketch.ToWKT());
    }

    [Fact]
    public void Dragging_a_shape_s_body_moves_the_whole_shape()
    {
        var s = With("GEOMETRYCOLLECTION (POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)), POINT (9 9))");
        s.LeftDown(C(2, 2), Tol);
        Assert.Equal(0, s.SelectedPart);
        s.Move(C(3, 2));
        Assert.Equal(SketchOutcome.Commit, s.LeftUp());
        Assert.Equal("GEOMETRYCOLLECTION (POLYGON ((1 0, 5 0, 5 4, 1 4, 1 0)), POINT (9 9))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Delete_removes_the_selected_shape()
    {
        var s = With("MULTIPOINT ((0 0), (5 5))");
        s.LeftDown(C(5, 5), Tol);
        s.LeftUp();
        Assert.Equal(SketchOutcome.Commit, s.Delete());
        Assert.Equal("MULTIPOINT ((0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Right_clicking_a_vertex_deletes_it()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        Assert.Equal(SketchOutcome.Commit, s.RightClick(C(2, 2), Tol));
        Assert.Equal("POLYGON ((0 0, 2 0, 0 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Deleting_a_vertex_of_a_triangle_removes_the_polygon_rather_than_turning_it_into_a_line()
    {
        var s = With("MULTIPOLYGON (((0 0, 2 0, 2 2, 0 0)), ((5 5, 6 5, 6 6, 5 5)))");
        s.RightClick(C(2, 2), Tol);
        Assert.Equal("MULTIPOLYGON (((5 5, 6 5, 6 6, 5 5)))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Deleting_a_vertex_of_a_triangular_hole_removes_the_hole()
    {
        var s = With("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0), (1 1, 2 1, 2 2, 1 1))");
        s.RightClick(C(2, 2), Tol);
        Assert.Equal("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0))", s.Sketch.ToWKT());
    }

    // ---- deleting a selected vertex (2026-11-12, the maintainer: "I can select a point and move
    // it, but not delete it") --------------------------------------------------------------------

    [Fact]
    public void Clicking_a_vertex_selects_it_and_Delete_removes_only_that_vertex()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        s.LeftDown(C(2, 2), Tol);
        s.LeftUp();
        Assert.Equal(new VertexRef(0, 0, 2), s.SelectedVertex);
        Assert.Equal(SketchOutcome.Commit, s.Delete());
        Assert.Equal("POLYGON ((0 0, 2 0, 0 2, 0 0))", s.Sketch.ToWKT());
        Assert.Null(s.SelectedVertex);
    }

    [Fact]
    public void Backspace_outside_drawing_deletes_the_selected_vertex_too()
    {
        var s = With("LINESTRING (0 0, 1 0, 2 0)");
        s.LeftDown(C(1, 0), Tol);
        s.LeftUp();
        Assert.Equal(SketchOutcome.Commit, s.Backspace());
        Assert.Equal("LINESTRING (0 0, 2 0)", s.Sketch.ToWKT());
    }

    [Fact]
    public void Clicking_a_shape_s_body_selects_the_shape_not_a_vertex_and_Delete_removes_the_shape()
    {
        var s = With("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 4, 0 0)), ((9 9, 10 9, 10 10, 9 9)))");
        s.LeftDown(C(2, 2), Tol);
        s.LeftUp();
        Assert.Null(s.SelectedVertex);
        Assert.Equal(0, s.SelectedPart);
        s.Delete();
        Assert.Equal("MULTIPOLYGON (((9 9, 10 9, 10 10, 9 9)))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Deleting_a_selected_vertex_of_a_triangle_removes_the_triangle()
    {
        var s = With("MULTIPOLYGON (((0 0, 2 0, 2 2, 0 0)), ((5 5, 6 5, 6 6, 5 5)))");
        s.LeftDown(C(2, 0), Tol);
        s.LeftUp();
        s.Delete();
        Assert.Equal("MULTIPOLYGON (((5 5, 6 5, 6 6, 5 5)))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Clicking_empty_space_clears_the_selection_and_Delete_then_does_nothing()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        s.LeftDown(C(2, 2), Tol);
        s.LeftUp();
        s.LeftDown(C(9, 9), Tol);
        s.LeftUp();
        Assert.Null(s.SelectedVertex);
        Assert.Equal(SketchOutcome.None, s.Delete());
        Assert.Equal("POLYGON ((0 0, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_dragged_vertex_stays_selected_and_can_be_deleted_after()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        s.LeftDown(C(2, 2), Tol);
        s.Move(C(3, 3));
        s.LeftUp();
        Assert.Equal(new VertexRef(0, 0, 2), s.SelectedVertex);
        s.Delete();
        Assert.Equal("POLYGON ((0 0, 2 0, 0 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_vertex_inserted_by_double_click_is_selected_so_one_Delete_takes_it_back()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        s.DoubleClick(C(1, 0.01), Tol);
        Assert.Equal(new VertexRef(0, 0, 1), s.SelectedVertex);
        s.Delete();
        Assert.Equal("POLYGON ((0 0, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_selected_vertex_that_no_longer_exists_after_an_undo_is_dropped()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        s.LeftDown(C(0, 2), Tol);
        s.LeftUp();
        Assert.Equal(new VertexRef(0, 0, 3), s.SelectedVertex);
        Assert.True(GeometrySketch.TryFromWKT("POLYGON ((0 0, 2 0, 2 2, 0 0))", out var smaller));
        s.Sync(smaller); // what Ctrl+Z in the patch looks like from here
        Assert.Null(s.SelectedVertex);
        Assert.Equal(SketchOutcome.Commit, s.Delete()); // the shape is still selected, so Delete takes it
    }

    [Fact]
    public void Changing_tool_drops_the_selected_vertex()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        s.LeftDown(C(2, 2), Tol);
        s.LeftUp();
        s.SetTool(SketchTool.Line);
        Assert.Null(s.SelectedVertex);
    }

    [Fact]
    public void A_sketch_arriving_from_the_node_is_not_taken_mid_gesture()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Assert.True(GeometrySketch.TryFromWKT("POINT (7 7)", out var other));
        s.Sync(other);
        Assert.Equal("POINT (0 0)", s.Sketch.ToWKT());
        s.Escape();
        s.Sync(other);
        Assert.Equal("POINT (7 7)", s.Sketch.ToWKT());
    }
}
