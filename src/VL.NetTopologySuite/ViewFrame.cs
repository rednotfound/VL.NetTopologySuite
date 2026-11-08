using System;
using System.Globalization;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Lib.Collections;

namespace VL.NetTopologySuite;

// The arithmetic behind ViewFrameNode (ViewFrameNode.cs): which view a Geometry IOBox shows, the
// grid, the label, and the transform onto the viewer's widget. Internal and tested
// (ViewFrameTests); the node only holds the state.
internal static class ViewFrame
{
    // The viewer draws into a LayerWidget of WidgetWidth x WidgetHeight - the size the editor's own
    // LayerViewer uses - whose space starts at its top-left corner, y down, in the widget's units
    // (the pattern of VL.Skia's "Example Particle Life.HDE.vl": LayerWidget (Stateful) around a
    // TransformSRT). ToWidget below maps the view onto it exactly, so the view has its aspect.
    // Not LayerViewer: it squares whatever bounds it gets, so the strips left and right of a view
    // stayed empty, and a grid drawn into them only widened the bounds and zoomed everything out
    // (measured 2026-11-08: the circle went from 310 to 240 pixels).
    internal const double WidgetWidth = 1.6;
    internal const double WidgetHeight = 1.2;
    internal const double Aspect = WidgetWidth / WidgetHeight;

    // Uniform scale and offset that put the view's top-left corner (Skia space: MinX, -MaxY) at the
    // widget's (0, 0) and its bottom-right at (WidgetWidth, WidgetHeight). TransformSRT scales,
    // then translates.
    internal static void ToWidget(Envelope view, out Vector2 scaling, out Vector2 translation)
    {
        var s = WidgetWidth / view.Width;
        scaling = new Vector2((float)s, (float)s);
        translation = new Vector2((float)(-view.MinX * s), (float)(view.MaxY * s));
    }

    // A fresh view gives the shape 1 / Headroom of the view in its binding direction, so it can
    // grow by a quarter before the view moves; the view re-fits when the shape fills less than
    // 1 / ShrinkBelow of it. Was 1.6 and 4 with a square view: the maintainer saw the shape use
    // about half of the box (2026-11-08).
    internal const double Headroom = 1.25;
    internal const double ShrinkBelow = 3;

    internal static Envelope Next(Envelope? view, Envelope? shape)
    {
        if (shape is null)
            return view ?? Around(0, 0, 1);
        if (view is null || !view.Contains(shape) || Fill(view, shape) * ShrinkBelow < 1)
            return Fit(shape);
        return view;
    }

    // How much of the view the shape fills, in its fuller direction.
    internal static double Fill(Envelope view, Envelope shape)
        => Math.Max(shape.Width / view.Width, shape.Height / view.Height);

    // Centred on the shape, Headroom times the shape in whichever direction binds; never zero, so
    // a horizontal line or a single point still gets a frame.
    internal static Envelope Fit(Envelope shape)
    {
        var width = Math.Max(shape.Width, shape.Height * Aspect);
        if (width <= 0)
            width = 1;
        return Around(shape.Centre.X, shape.Centre.Y, width * Headroom);
    }

    static Envelope Around(double x, double y, double width)
    {
        var height = width / Aspect;
        return new Envelope(x - width / 2, x + width / 2, y - height / 2, y + height / 2);
    }

    // 1, 2 or 5 times a power of ten, giving between about 4 and 10 cells across the view.
    internal static double Step(double extent)
    {
        var raw = extent / 6;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normal = raw / magnitude;
        var nice = normal < 1.5 ? 1 : normal < 3.5 ? 2 : normal < 7.5 ? 5 : 10;
        return nice * magnitude;
    }

    // An estimate of the label's width: measured on screen, the font runs about 0.55 of its size
    // per character.
    internal static double LabelWidth(string label, double textSize) => label.Length * textSize * 0.55;

    internal static string Format(double value)
        => value.ToString("G4", CultureInfo.InvariantCulture);

    internal static void Layout(
        Envelope view, Envelope? shape,
        out Spread<Vector2> grid, out float dotSize,
        out string label, out Vector2 labelPosition, out Vector2 labelSize, out float textSize)
    {
        var step = Step(view.Width);

        label = shape is null
            ? $"grid {Format(step)}"
            : $"{Format(shape.Width)} × {Format(shape.Height)} · grid {Format(step)}";

        // The label sits in the bottom-left corner, inside the margin Headroom leaves below a
        // freshly fitted shape (a tenth of the height), in a box its own height and about its own
        // width: the Text layer centres the text in that box (seen 2026-11-08: with a box of zero
        // width, half the label hung out of the view). Map coordinates here; Skia's below.
        var text = view.Height * 0.065;
        var margin = view.Height * 0.02;
        var left = view.MinX + margin;
        var bottom = view.MinY + margin;
        var right = left + LabelWidth(label, text);
        var top = bottom + text;
        textSize = (float)text;
        labelPosition = new Vector2((float)left, (float)-bottom);
        labelSize = new Vector2((float)(right - left), (float)text);

        // Grid dots everywhere in the view except under the label.
        var builder = new SpreadBuilder<Vector2>();
        var x0 = Math.Ceiling(view.MinX / step);
        var y0 = Math.Ceiling(view.MinY / step);
        for (var i = x0; i * step <= view.MaxX && i - x0 < 40; i++)
            for (var j = y0; j * step <= view.MaxY && j - y0 < 40; j++)
            {
                double x = i * step, y = j * step;
                if (x >= left - margin && x <= right + margin && y <= top + margin)
                    continue;
                builder.Add(new Vector2((float)x, (float)-y));
            }
        grid = builder.ToSpread();
        dotSize = (float)(view.Width * 0.014);
    }
}
