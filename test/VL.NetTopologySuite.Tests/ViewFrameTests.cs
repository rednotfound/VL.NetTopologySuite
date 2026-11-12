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
    public void A_fresh_view_is_4_by_3_and_the_shape_fills_it_to_the_headroom(double w, double h)
    {
        // The maintainer, 2026-11-08: the shape used about half of the box, then: why are the
        // strips left and right empty? The view has the widget's 4:3 (MapLayerViewer fits it).
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
        var view = ViewFrame.Next(null, Box(-2, -2, 2, 2));   // 6.67 x 5 around a 4 x 4
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
        ViewFrame.Layout(view, shape, out var grid, out _, out var label, out var labelAt, out _, out _);
        Assert.True(labelAt.Y > -view.Centre.Y);     // the label sits at the bottom: larger Skia y
        Assert.Equal("4 × 2 · grid 1", label);
        Assert.Contains(grid, p => p.X == 0 && p.Y == 0);
        Assert.Contains(grid, p => p.X == 1 && p.Y == -1);  // map (1, 1) is Skia (1, -1)
    }

    [Fact]
    public void Nothing_to_show_gives_a_unit_view_and_no_size_in_the_label()
    {
        var view = ViewFrame.Next(null, null);
        ViewFrame.Layout(view, null, out var grid, out _, out var label, out _, out _, out _);
        Assert.Equal(1, view.Width, 9);
        Assert.Equal(0.75, view.Height, 9);
        Assert.StartsWith("grid ", label);
        Assert.NotEmpty(grid);
    }

    [Fact]
    public void The_label_sits_in_the_bottom_left_corner_with_no_dots_under_it()
    {
        var shape = Box(0, 0, 2000, 3000);                   // the maintainer's tall rectangle
        var view = ViewFrame.Next(null, shape);
        ViewFrame.Layout(view, shape, out var grid, out _, out var label, out var at, out var box, out var text);
        var right = at.X + ViewFrame.LabelWidth(label, text);
        Assert.Equal(text, box.Y, 2);                        // a box the text's height and width
        Assert.Equal(right - at.X, box.X, 2);
        Assert.True(at.X > view.MinX && at.X < view.MinX + view.Width * 0.05);
        Assert.True(-at.Y > view.MinY && -at.Y + text <= shape.MinY);   // below the shape, inside the view
        Assert.DoesNotContain(grid, p => p.X >= at.X && p.X <= right && -p.Y <= -at.Y + text);
    }

    [Fact]
    public void The_grid_fills_the_view_beside_a_tall_shape()
    {
        // The maintainer, 2026-11-08: why are the strips left and right empty?
        var shape = Box(0, 0, 2, 3);
        var view = ViewFrame.Next(null, shape);
        ViewFrame.Layout(view, shape, out var grid, out _, out _, out _, out _, out _);
        var step = ViewFrame.Step(view.Width);
        Assert.Contains(grid, p => p.X < view.MinX + step);
        Assert.Contains(grid, p => p.X > view.MaxX - step);
        Assert.All(grid, p => Assert.True(view.Contains(p.X, -p.Y)));
    }

    [Fact]
    public void The_view_maps_exactly_onto_the_widget()
    {
        // TransformSRT: p * scaling + translation, in Skia space (y down).
        var view = ViewFrame.Next(null, Box(10, 20, 14, 22));
        ViewFrame.ToWidget(view, out var s, out var t);
        Assert.Equal(s.X, s.Y);                                           // uniform: no distortion
        Assert.Equal(0f, (float)view.MinX * s.X + t.X, 4);                // north-west corner -> (0, 0)
        Assert.Equal(0f, (float)-view.MaxY * s.Y + t.Y, 4);
        Assert.Equal((float)ViewFrame.WidgetWidth, (float)view.MaxX * s.X + t.X, 4);   // south-east -> (1.6, 1.2)
        Assert.Equal((float)ViewFrame.WidgetHeight, (float)-view.MinY * s.Y + t.Y, 4);
    }

    [Fact]
    public void The_node_keeps_its_view_between_frames()
    {
        var node = new ViewFrameNode();
        node.Update(GeometryNodes.Rectangle(new Coordinate(0, 0), 4, 2), out _, out _, out _, out _, out _, out _, out _, out var s1, out var t1);
        node.Update(GeometryNodes.Rectangle(new Coordinate(0, 0), 5, 2), out _, out _, out _, out var label, out _, out _, out _, out var s2, out var t2);
        Assert.Equal(s1, s2);                        // the same zoom and offset: the view stayed
        Assert.Equal(t1, t2);
        Assert.StartsWith("5 × 2", label);
    }

    [Fact]
    public void On_lon_lat_a_square_block_is_drawn_square_and_labelled_in_degrees()
    {
        // The maintainer, 2026-11-12: the IOBox should draw what the editor window draws.
        var k = System.Math.Cos(52.0 * System.Math.PI / 180);
        var block = Defaults.FactoryFor(4326).ToGeometry(new Envelope(4.355, 4.365, 52.0, 52.0 + 0.01 * k));
        var xScale = ViewFrame.XScaleOf(block);
        var drawn = ViewFrame.Squeeze(block, xScale);
        Assert.Equal(1, drawn.EnvelopeInternal.Width / drawn.EnvelopeInternal.Height, 3); // cos is taken at the centre, 52.003
        Assert.Equal(0.01, block.EnvelopeInternal.Width, 9); // the input is not touched

        var view = ViewFrame.Next(null, drawn.EnvelopeInternal);
        ViewFrame.Layout(view, drawn.EnvelopeInternal, out _, out _, out var label, out _, out _, out _, xScale, ViewFrame.UnitOf(4326));
        Assert.StartsWith("0.01° × 0.006157°", label);
    }

    [Fact]
    public void Without_a_geographic_SRID_nothing_is_squeezed()
    {
        var g = Defaults.FactoryFor(28992).ToGeometry(new Envelope(0, 10, 52, 62));
        Assert.Equal(1, ViewFrame.XScaleOf(g));
        Assert.Same(g, ViewFrame.Squeeze(g, 1));
        Assert.Equal(" m", ViewFrame.UnitOf(28992));
        Assert.Equal("", ViewFrame.UnitOf(0));
    }

    [Fact]
    public void The_lon_lat_grid_has_the_same_step_in_degrees_both_ways()
    {
        var k = 0.5;
        var view = ViewFrame.Fit(new Envelope(0, 1, 0, 2)); // drawn space
        ViewFrame.Layout(view, null, out var grid, out _, out _, out _, out _, out _, k, "°");
        var xs = grid.Select(v => v.X).Distinct().OrderBy(x => x).ToArray();
        var ys = grid.Select(v => v.Y).Distinct().OrderBy(y => y).ToArray();
        Assert.Equal((ys[1] - ys[0]) * k, xs[1] - xs[0], 5);
    }
}