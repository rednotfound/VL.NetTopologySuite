using System.Linq;
using NetTopologySuite.Geometries;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The view a Geometry IOBox draws in: sticky while the shape fits, re-fitted when it outgrows the
/// view or fills less than a third of it; the editor's 4:3 aspect; a round grid step; Skia space (y down) out.
/// </summary>
public class ViewFrameTests
{
    private static Envelope Box(double minX, double minY, double maxX, double maxY) => new(minX, maxX, minY, maxY);

    [Fact]
    public void A_first_shape_is_fitted_with_headroom()
    {
        var view = ViewFrame.Next(null, Box(0, 0, 4, 2));
        Assert.Equal(4 * ViewFrame.Headroom, view.Width, 9);
        Assert.Equal(2, view.Centre.X, 9);
        Assert.Equal(1, view.Centre.Y, 9);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(1, 3)]
    [InlineData(10, 0)]
    public void A_fresh_view_is_square_and_the_shape_fills_it_to_the_headroom(double w, double h)
    {
        // The maintainer, 2026-11-08: the shape used about half of the box; 1.6 headroom was half
        // of that, the editor's LayerViewer margin the other half (it shows a square, so ours is).
        var view = ViewFrame.Next(null, Box(0, 0, w, h));
        Assert.Equal(ViewFrame.Aspect, view.Width / view.Height, 9);
        Assert.Equal(1 / ViewFrame.Headroom, ViewFrame.Fill(view, Box(0, 0, w, h)), 9);
    }

    [Fact]
    public void The_view_stays_while_the_shape_grows_inside_it()
    {
        // The point of the whole node: a wider rectangle must LOOK wider, so the frame must not move.
        var view = ViewFrame.Next(null, Box(-2, -1, 2, 1));
        var after = ViewFrame.Next(view, Box(-2.4, -1, 2.4, 1));   // 20% wider, inside the 25% headroom
        Assert.Same(view, after);
    }

    [Fact]
    public void The_view_refits_when_the_shape_outgrows_it()
    {
        var view = ViewFrame.Next(null, Box(-2, -1, 2, 1));
        var after = ViewFrame.Next(view, Box(-10, -1, 10, 1));
        Assert.NotSame(view, after);
        Assert.True(after.Contains(Box(-10, -1, 10, 1)));
    }

    [Fact]
    public void The_view_refits_when_the_shape_shrinks_below_a_third()
    {
        var view = ViewFrame.Next(null, Box(-2, -2, 2, 2));   // 5 x 5 around a 4 x 4
        Assert.Same(view, ViewFrame.Next(view, Box(-0.9, -0.9, 0.9, 0.9)));
        Assert.NotSame(view, ViewFrame.Next(view, Box(-0.7, -0.7, 0.7, 0.7)));
    }

    [Fact]
    public void A_line_or_a_point_still_gets_a_frame()
    {
        Assert.True(ViewFrame.Next(null, Box(0, 0, 5, 0)).Height > 0);
        Assert.True(ViewFrame.Next(null, Box(3, 3, 3, 3)).Width > 0);
    }

    [Theory]
    [InlineData(6, 1)]
    [InlineData(10, 2)]
    [InlineData(30, 5)]
    [InlineData(60, 10)]
    [InlineData(0.06, 0.01)]
    public void The_grid_step_is_one_two_or_five_times_a_power_of_ten(double extent, double step)
    {
        Assert.Equal(step, ViewFrame.Step(extent), 9);
    }

    [Fact]
    public void Layout_is_in_Skia_space_with_y_down()
    {
        var shape = Box(0, 0, 4, 2);
        var view = ViewFrame.Next(null, shape);
        ViewFrame.Layout(view, shape, out var position, out var size, out var grid, out _, out var label, out var labelAt, out _, out _);
        Assert.Equal(2f, position.X, 4);
        Assert.Equal(-1f, position.Y, 4);           // centre y = 1 on the map, -1 in Skia
        Assert.Equal((float)view.Width, size.X, 4);
        Assert.True(labelAt.Y > position.Y);         // the label sits at the bottom: larger Skia y
        Assert.Equal("4 × 2 · grid 1", label);
        Assert.Contains(grid, p => p.X == 0 && p.Y == 0);
        Assert.Contains(grid, p => p.X == 1 && p.Y == -1);  // map (1, 1) is Skia (1, -1)
    }

    [Fact]
    public void Nothing_to_show_gives_a_unit_view_and_no_size_in_the_label()
    {
        var view = ViewFrame.Next(null, null);
        ViewFrame.Layout(view, null, out _, out _, out var grid, out _, out var label, out _, out _, out _);
        Assert.Equal(1, view.Width, 9);
        Assert.Equal(1, view.Height, 9);
        Assert.StartsWith("grid ", label);
        Assert.NotEmpty(grid);
    }

    [Fact]
    public void The_label_sits_in_the_bottom_left_corner_with_no_dots_under_it()
    {
        var shape = Box(0, 0, 4, 2);
        var view = ViewFrame.Next(null, shape);
        ViewFrame.Layout(view, shape, out _, out _, out var grid, out _, out var label, out var at, out var box, out var text);
        Assert.Equal(text, box.Y, 5);                       // a box exactly the text's height
        Assert.True(at.X > view.MinX && at.X < view.MinX + view.Width * 0.05);
        Assert.True(-at.Y > view.MinY && -at.Y < view.MinY + view.Height * 0.05);
        Assert.True(-at.Y + text <= shape.MinY);            // below the freshly fitted shape
        var right = at.X + label.Length * text * 0.6;
        Assert.DoesNotContain(grid, p => p.X >= at.X && p.X <= right && -p.Y <= -at.Y + text);
    }

    [Fact]
    public void The_node_keeps_its_view_between_frames()
    {
        var node = new ViewFrameNode();
        node.Update(GeometryNodes.Rectangle(new Coordinate(0, 0), 4, 2), out var p1, out var s1, out _, out _, out _, out _, out _, out _);
        node.Update(GeometryNodes.Rectangle(new Coordinate(0, 0), 5, 2), out var p2, out var s2, out _, out _, out var label, out _, out _, out _);
        Assert.Equal(s1, s2);
        Assert.Equal(p1, p2);
        Assert.StartsWith("5 × 2", label);
    }
}
