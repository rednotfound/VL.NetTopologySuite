using System.Linq;
using NetTopologySuite.Geometries;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The geometry editor window's rules as gestures (the 2026-11-12 redesign: select first, then
/// edit; "+" handles add points; moving a whole shape is its own mode; a drag needs a threshold;
/// actions next to the selection, disabled with a reason; undo / redo in the window).
/// </summary>
public class SketchSessionTests
{
    private const double Tol = 0.1; // the default drag threshold is two thirds of it

    private static Coordinate C(double x, double y) => new(x, y);

    private static SketchSession With(string wkt)
    {
        var s = new SketchSession();
        Assert.True(GeometrySketch.TryFromWKT(wkt, out var sketch));
        s.Sync(sketch);
        return s;
    }

    private static SketchOutcome Click(SketchSession s, double x, double y)
    {
        var down = s.LeftDown(C(x, y), Tol);
        var up = s.LeftUp();
        return (SketchOutcome)System.Math.Max((int)down, (int)up);
    }

    private static SketchOutcome Drag(SketchSession s, double fromX, double fromY, double toX, double toY)
    {
        s.LeftDown(C(fromX, fromY), Tol);
        s.Move(C(toX, toY));
        return s.LeftUp();
    }

    private const string Square = "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))";

    // ---- select first, then edit -------------------------------------------------------------

    [Fact]
    public void An_unselected_shape_cannot_be_reshaped_a_press_on_its_vertex_only_selects_it()
    {
        var s = With(Square);
        Assert.Equal(SketchOutcome.None, Drag(s, 2, 2, 5, 5));
        Assert.Equal(Square, s.Sketch.ToWKT());
        Assert.Equal(0, s.SelectedPart);
        Assert.Null(s.SelectedVertex);
    }

    [Fact]
    public void Dragging_a_selected_shape_s_body_does_not_move_it()
    {
        // The maintainer, 2026-11-12: "I wanted to add a point and moved the whole shape instead".
        var s = With(Square);
        Click(s, 1, 1);
        Assert.Equal(SketchOutcome.None, Drag(s, 1, 1, 6, 6));
        Assert.Equal(Square, s.Sketch.ToWKT());
    }

    [Fact]
    public void A_vertex_of_the_selected_shape_drags_and_commits_once_on_release()
    {
        var s = With(Square);
        Click(s, 1, 1);
        s.LeftDown(C(2, 2), Tol);
        Assert.Equal(SketchOutcome.Preview, s.Move(C(3, 3)));
        Assert.Equal(SketchOutcome.Preview, s.Move(C(4, 4)));
        Assert.Equal(SketchOutcome.Commit, s.LeftUp());
        Assert.Equal("POLYGON ((0 0, 2 0, 4 4, 0 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_jitter_below_the_threshold_moves_nothing()
    {
        var s = With(Square);
        Click(s, 1, 1);
        s.LeftDown(C(2, 2), Tol);
        Assert.Equal(SketchOutcome.None, s.Move(C(2.03, 2.03)));
        Assert.Equal(SketchOutcome.None, s.LeftUp());
        Assert.Equal(Square, s.Sketch.ToWKT());
        Assert.Equal(new VertexRef(0, 0, 2), s.SelectedVertex); // it was a click: the vertex is selected
    }

    [Fact]
    public void Pressing_empty_space_deselects_and_asks_the_window_to_pan()
    {
        var s = With(Square);
        Click(s, 1, 1);
        s.LeftDown(C(9, 9), Tol);
        Assert.True(s.WantsPan);
        Assert.Equal(-1, s.SelectedPart);
        s.LeftUp();
        Assert.False(s.WantsPan);
    }

    // ---- "+" handles -------------------------------------------------------------------------

    [Fact]
    public void The_selected_shape_offers_a_plus_at_the_middle_of_every_edge_including_the_closing_one()
    {
        var s = With(Square);
        Assert.Empty(s.Midpoints());
        Click(s, 1, 1);
        var mids = s.Midpoints().Select(m => (m.At.X, m.At.Y)).ToArray();
        Assert.Equal(new[] { (1.0, 0.0), (2.0, 1.0), (1.0, 2.0), (0.0, 1.0) }, mids);
    }

    [Fact]
    public void Clicking_a_plus_adds_a_point_at_the_middle_and_selects_it()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Assert.Equal(SketchOutcome.Commit, Click(s, 1, 0));
        Assert.Equal("POLYGON ((0 0, 1 0, 2 0, 2 2, 0 2, 0 0))", s.Sketch.ToWKT());
        Assert.Equal(new VertexRef(0, 0, 1), s.SelectedVertex);
    }

    [Fact]
    public void Dragging_a_plus_pulls_the_new_point_out()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Assert.Equal(SketchOutcome.Commit, Drag(s, 2, 1, 3, 1));
        Assert.Equal("POLYGON ((0 0, 2 0, 3 1, 2 2, 0 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_line_has_no_plus_on_a_closing_edge()
    {
        var s = With("LINESTRING (0 0, 2 0, 2 2)");
        Click(s, 1, 0);
        Assert.Equal(2, s.Midpoints().Count());
    }

    [Fact]
    public void Double_clicking_an_edge_of_the_selected_shape_still_inserts_there()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Assert.Equal(SketchOutcome.Commit, s.DoubleClick(C(2, 0.5), Tol));
        Assert.Equal("POLYGON ((0 0, 2 0, 2 0.5, 2 2, 0 2, 0 0))", s.Sketch.ToWKT());
    }

    // ---- deleting ----------------------------------------------------------------------------

    [Fact]
    public void Delete_removes_the_selected_point()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Click(s, 2, 2);
        Assert.Equal(SketchOutcome.Commit, s.Delete());
        Assert.Equal("POLYGON ((0 0, 2 0, 0 2, 0 0))", s.Sketch.ToWKT());
        Assert.Null(s.SelectedVertex);
    }

    [Fact]
    public void Delete_with_only_the_shape_selected_removes_the_shape()
    {
        var s = With("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 4, 0 0)), ((9 9, 10 9, 10 10, 9 9)))");
        Click(s, 2, 2);
        Assert.Equal(SketchOutcome.Commit, s.Delete());
        Assert.Equal("MULTIPOLYGON (((9 9, 10 9, 10 10, 9 9)))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_triangle_s_point_is_not_deleted_and_the_reason_is_given()
    {
        var s = With("POLYGON ((0 0, 2 0, 2 2, 0 0))");
        Click(s, 1.5, 0.5);
        Click(s, 2, 2);
        var deletePoint = s.Actions().Single(a => a.Action == SketchAction.DeletePoint);
        Assert.False(deletePoint.Enabled);
        Assert.Contains("at least 3 points", deletePoint.Reason);
        Assert.Equal(SketchOutcome.None, s.Delete());
        Assert.Equal("POLYGON ((0 0, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
        Assert.Contains("at least 3 points", s.HintText);
    }

    [Fact]
    public void A_two_point_line_keeps_its_points()
    {
        var s = With("LINESTRING (0 0, 2 0)");
        Click(s, 1, 0.05);
        Click(s, 2, 0);
        Assert.Equal(SketchOutcome.None, s.Delete());
    }

    [Fact]
    public void Deleting_a_point_shape_s_point_removes_the_point()
    {
        var s = With("MULTIPOINT ((0 0), (5 5))");
        Click(s, 5, 5);
        Assert.Equal(SketchOutcome.Commit, s.Delete());
        Assert.Equal("MULTIPOINT ((0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Right_clicking_a_point_of_the_selected_shape_deletes_it()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Assert.Equal(SketchOutcome.Commit, s.RightClick(C(2, 2), Tol));
        Assert.Equal("POLYGON ((0 0, 2 0, 0 2, 0 0))", s.Sketch.ToWKT());
    }

    // ---- move mode ---------------------------------------------------------------------------

    [Fact]
    public void Move_mode_moves_the_whole_shape_and_Done_commits_it()
    {
        var s = With("GEOMETRYCOLLECTION (POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)), POINT (9 9))");
        Click(s, 2, 2);
        s.Run(SketchAction.Move);
        Assert.True(s.IsMoving);
        Assert.Equal(SketchOutcome.None, Drag(s, 2, 2, 3, 2)); // previewed, not yet written
        Assert.Equal(SketchOutcome.Commit, s.Run(SketchAction.Done));
        Assert.False(s.IsMoving);
        Assert.Equal("GEOMETRYCOLLECTION (POLYGON ((1 0, 5 0, 5 4, 1 4, 1 0)), POINT (9 9))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Cancel_puts_a_moved_shape_back()
    {
        var s = With(Square);
        Click(s, 1, 1);
        s.StartMove();
        Drag(s, 1, 1, 5, 5);
        Assert.Equal(SketchOutcome.Preview, s.Run(SketchAction.Cancel));
        Assert.Equal(Square, s.Sketch.ToWKT());
    }

    [Fact]
    public void Escape_during_a_vertex_drag_gives_the_drag_back()
    {
        var s = With(Square);
        Click(s, 1, 1);
        s.LeftDown(C(2, 2), Tol);
        s.Move(C(4, 4));
        Assert.Equal(SketchOutcome.Preview, s.Escape());
        Assert.Equal(SketchOutcome.None, s.LeftUp());
        Assert.Equal(Square, s.Sketch.ToWKT());
    }

    // ---- actions and hints ------------------------------------------------------------------

    [Fact]
    public void The_actions_follow_the_selection()
    {
        var s = With(Square);
        Assert.Empty(s.Actions());
        Click(s, 1, 1);
        Assert.Equal(new[] { SketchAction.Move, SketchAction.Hole, SketchAction.DeleteShape }, s.Actions().Select(a => a.Action));
        Click(s, 2, 2);
        Assert.Equal(new[] { SketchAction.DeletePoint }, s.Actions().Select(a => a.Action));
        s.Escape();
        s.StartMove();
        Assert.Equal(new[] { SketchAction.Done, SketchAction.Cancel }, s.Actions().Select(a => a.Action));
    }

    [Fact]
    public void A_line_offers_no_hole()
    {
        var s = With("LINESTRING (0 0, 2 0, 2 2)");
        Click(s, 1, 0);
        Assert.DoesNotContain(s.Actions(), a => a.Action == SketchAction.Hole);
    }

    [Fact]
    public void While_drawing_Done_is_disabled_until_the_shape_has_enough_points()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Polygon);
        Click(s, 0, 0);
        Click(s, 1, 0);
        var done = s.Actions().First();
        Assert.Equal(SketchAction.Finish, done.Action);
        Assert.False(done.Enabled);
        Click(s, 1, 1);
        Assert.True(s.Actions().First().Enabled);
    }

    [Fact]
    public void The_hint_says_what_can_be_done_now()
    {
        var s = With(Square);
        Assert.Contains("Click a shape", s.HintText);
        Click(s, 1, 1);
        Assert.Contains("add a point", s.HintText);
        Click(s, 2, 2);
        Assert.Contains("Delete removes this point", s.HintText);
        s.Escape();
        s.StartMove();
        Assert.Contains("Esc puts it back", s.HintText);
    }

    // ---- undo / redo -------------------------------------------------------------------------

    [Fact]
    public void Undo_and_redo_walk_the_committed_edits()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Click(s, 1, 0);                    // + at the bottom edge
        Click(s, 2, 2);
        s.Delete();                        // the top-right corner
        Assert.Equal("POLYGON ((0 0, 1 0, 2 0, 0 2, 0 0))", s.Sketch.ToWKT());
        Assert.Equal(SketchOutcome.Commit, s.Undo());
        Assert.Equal("POLYGON ((0 0, 1 0, 2 0, 2 2, 0 2, 0 0))", s.Sketch.ToWKT());
        Assert.Equal(SketchOutcome.Commit, s.Undo());
        Assert.Equal(Square, s.Sketch.ToWKT());
        Assert.False(s.CanUndo);
        Assert.Equal(SketchOutcome.Commit, s.Redo());
        Assert.Equal("POLYGON ((0 0, 1 0, 2 0, 2 2, 0 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_new_edit_after_undo_drops_the_redo()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Click(s, 1, 0);
        s.Undo();
        Assert.True(s.CanRedo);
        Click(s, 1, 1);
        Click(s, 2, 1);
        Assert.False(s.CanRedo);
    }

    [Fact]
    public void Undo_while_drawing_takes_the_last_point_back()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 1, 0);
        Click(s, 9, 9);
        Assert.False(s.CanUndo);
        s.Undo();
        s.Enter();
        Assert.Equal("LINESTRING (0 0, 1 0)", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_change_from_outside_the_window_starts_the_history_again()
    {
        var s = With(Square);
        Click(s, 1, 1);
        Click(s, 1, 0);
        Assert.True(s.CanUndo);
        Assert.True(GeometrySketch.TryFromWKT("POINT (7 7)", out var typed));
        s.Sync(typed);
        Assert.False(s.CanUndo);
        Assert.Equal(-1, s.SelectedPart);
    }

    // ---- drawing -----------------------------------------------------------------------------

    [Fact]
    public void The_point_tool_commits_every_click_and_stays_for_the_next()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Point);
        Assert.Equal(SketchOutcome.Commit, Click(s, 1, 1));
        Assert.Equal(SketchOutcome.Commit, Click(s, 2, 2));
        Assert.Equal(SketchTool.Point, s.Tool);
        Assert.Equal("MULTIPOINT ((1 1), (2 2))", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_finished_line_goes_back_to_Select_with_the_line_selected()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 1, 0);
        Assert.True(s.IsDrawing);
        Assert.Equal(SketchOutcome.Commit, s.Enter());
        Assert.Equal("LINESTRING (0 0, 1 0)", s.Sketch.ToWKT());
        Assert.Equal(SketchTool.Select, s.Tool);
        Assert.Equal(0, s.SelectedPart);
        Assert.NotEmpty(s.Midpoints()); // ready to adjust
    }

    [Fact]
    public void A_double_click_finishes_and_its_extra_point_is_dropped()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 2, 0);
        Click(s, 2, 0);
        Assert.Equal(SketchOutcome.Commit, s.DoubleClick(C(2, 0), Tol));
        Assert.Equal("LINESTRING (0 0, 2 0)", s.Sketch.ToWKT());
    }

    [Fact]
    public void Clicking_the_first_point_closes_a_polygon()
    {
        var s = new SketchSession();
        s.SetTool(SketchTool.Polygon);
        Click(s, 0, 0);
        Click(s, 2, 0);
        Click(s, 2, 2);
        Assert.Equal(SketchOutcome.Commit, Click(s, 0.02, 0.02));
        Assert.Equal("POLYGON ((0 0, 2 0, 2 2, 0 0))", s.Sketch.ToWKT());
    }

    [Fact]
    public void Escape_drops_the_part_being_drawn_and_Back_takes_a_point_back()
    {
        var s = With("POINT (5 5)");
        s.SetTool(SketchTool.Line);
        Click(s, 0, 0);
        Click(s, 1, 0);
        Click(s, 9, 9);
        s.Run(SketchAction.BackOnePoint);
        Assert.Equal(2, s.Sketch.Part(1).VertexCount(0));
        s.Escape();
        Assert.False(s.IsDrawing);
        Assert.Equal("POINT (5 5)", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_hole_is_started_from_the_selected_polygon()
    {
        var s = With("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0))");
        Click(s, 5, 5);
        s.Run(SketchAction.Hole);
        Assert.Equal(SketchTool.Hole, s.Tool);
        Assert.Equal(SketchOutcome.None, s.LeftDown(C(20, 20), Tol)); // outside: refused
        Assert.Contains("inside", s.HintText);
        Click(s, 1, 1);
        Click(s, 2, 1);
        Click(s, 2, 2);
        Assert.Equal(SketchOutcome.Commit, s.Enter());
        Assert.Equal("POLYGON ((0 0, 9 0, 9 9, 0 9, 0 0), (1 1, 2 1, 2 2, 1 1))", s.Sketch.ToWKT());
        Assert.Equal(SketchTool.Select, s.Tool);
    }

    [Fact]
    public void Drawing_a_second_polygon_makes_a_multipolygon_and_a_line_makes_a_collection()
    {
        var s = With("POLYGON ((0 0, 1 0, 1 1, 0 0))");
        s.SetTool(SketchTool.Polygon);
        Click(s, 5, 5); Click(s, 6, 5); Click(s, 6, 6);
        s.Enter();
        Assert.Equal("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", s.Sketch.ToWKT());
        s.SetTool(SketchTool.Line);
        Click(s, 8, 8); Click(s, 9, 8);
        s.Enter();
        Assert.StartsWith("GEOMETRYCOLLECTION", s.Sketch.ToWKT());
    }

    [Fact]
    public void A_polygon_finished_with_two_points_is_discarded_with_a_message()
    {
        var s = With("POINT (5 5)");
        s.SetTool(SketchTool.Polygon);
        Click(s, 0, 0);
        Click(s, 2, 0);
        Assert.Equal(SketchOutcome.Preview, s.Enter());
        Assert.Equal("POINT (5 5)", s.Sketch.ToWKT());
        Assert.Contains("discarded", s.HintText);
    }

    [Fact]
    public void A_new_point_near_an_existing_one_lands_on_it()
    {
        var s = With("POLYGON ((0 0, 1 0, 1 1, 0 0))");
        s.SetTool(SketchTool.Line);
        Click(s, 1.05, 1.02);
        Click(s, 3, 3);
        s.Enter();
        Assert.Equal("GEOMETRYCOLLECTION (POLYGON ((0 0, 1 0, 1 1, 0 0)), LINESTRING (1 1, 3 3))", s.Sketch.ToWKT());
    }

    [Fact]
    public void With_snap_the_real_position_hits_and_the_snapped_one_is_placed()
    {
        var s = With("POLYGON ((0 0, 2 0, 2.3 2.3, 0 0))");
        Click(s, 1.5, 0.5);
        s.LeftDown(C(2.3, 2.3), Tol, place: C(2, 2));
        Assert.NotNull(s.SelectedVertex);
        s.Move(C(3, 3));
        s.LeftUp();
        Assert.Equal("POLYGON ((0 0, 2 0, 3 3, 0 0))", s.Sketch.ToWKT());

        var p = new SketchSession();
        p.SetTool(SketchTool.Point);
        p.LeftDown(C(0.97, 1.04), Tol, place: C(1, 1));
        Assert.Equal("POINT (1 1)", p.Sketch.ToWKT());
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
