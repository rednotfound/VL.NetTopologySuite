using System;
using System.Globalization;
using NetTopologySuite.Geometries;
using Stride.Core.Mathematics;
using VL.Core;
using VL.Core.Import;
using VL.Lib.Collections;

namespace VL.NetTopologySuite;

// The view a Geometry IOBox draws in - for VL.NetTopologySuite.HDE.vl's GeometryViewer only, so it
// carries the Hidden smell and the NodeBrowser does not list it (VL.Core.Import.SmellAttribute;
// smells show only "if enabled in settings", per VL.Lang's INodeInfo.Smells). Measured 2026-11-08:
// SymbolSmell.Internal is NOT that - it makes the node unresolvable from any other document
// ("Not found: ViewFrame" from a probe patch, and the HDE document drew nothing of it), while
// Hidden still resolves.
//
// Why it exists (the maintainer, 2026-11-08): the editor's LayerViewer fits whatever it is given
// into the box every frame, so a size change was invisible - a Rectangle made wider looked as if it
// became lower, a Circle looked the same at every radius. LayerViewer fits the bounds of the layers
// it draws, and those are ours to set (PointViewer already frames its pin with an invisible
// Rectangle). So this decides the frame, and adds the two things a map has for scale:
//
// - A STICKY view (hysteresis). It keeps the last frame while the shape fits inside it and fills
//   at least a third of it; only then does it re-fit, with headroom, so the next edits show as
//   the shape itself growing or shrinking. State lives in the node, one per viewer instance; a
//   tooltip makes a new viewer each hover and so always starts fitted, which is right for a glance.
// - A DOT GRID at a round step (1, 2 or 5 times a power of ten) and a label "4 × 2 · grid 1": the
//   absolute size, readable even right after a re-fit.
//
// Everything it outputs is in the viewer's space, which is Skia's: y DOWN. The geometry itself is
// y-up (a map's); GeometryViewer negates y before drawing, and so does this, in one place.
//
// The arithmetic is ViewFrame below, internal and tested; this class only holds the state.

/// <summary>
/// The frame, grid and size label a Geometry IOBox draws in. Used by the editor's viewer.
/// </summary>
[ProcessNode(Name = "ViewFrame", Category = "NetTopologySuite.Viewer")]
[Smell(SymbolSmell.Hidden)]
public sealed class ViewFrameNode
{
    Envelope? _view;

    /// <summary>
    /// Keeps the view while the geometry fits it, and lays out the grid and the label.
    /// </summary>
    /// <param name="geometry">The geometry being shown.</param>
    /// <param name="framePosition">The centre of the view, in Skia space.</param>
    /// <param name="frameSize">The size of the view.</param>
    /// <param name="grid">The grid points inside the view, in Skia space.</param>
    /// <param name="dotSize">The size of a grid dot.</param>
    /// <param name="label">The geometry's width and height, and the grid step.</param>
    /// <param name="labelPosition">The bottom-left corner of the label, in Skia space.</param>
    /// <param name="labelSize">The box the label is drawn in.</param>
    /// <param name="textSize">The height of the label text.</param>
    public void Update(
        Geometry? geometry,
        out Vector2 framePosition,
        out Vector2 frameSize,
        out Spread<Vector2> grid,
        out float dotSize,
        out string label,
        out Vector2 labelPosition,
        out Vector2 labelSize,
        out float textSize)
    {
        var shape = geometry is null || geometry.IsEmpty ? null : geometry.EnvelopeInternal;
        _view = ViewFrame.Next(_view, shape);
        ViewFrame.Layout(_view, shape, out framePosition, out frameSize, out grid, out dotSize,
            out label, out labelPosition, out labelSize, out textSize);
    }
}

internal static class ViewFrame
{
    // The editor's LayerViewer draws into a fixed 1.6 x 1.2 widget and makes our bounds square
    // (FixAspectRatio, reference 1 x 1, FitOut, then Scale 1.05; read out of
    // HDE.WidgetsAndViewers.vl). Measured on screen 2026-11-08: a 4:3 view came out at 59% of the
    // widget's width and height, a square one at 95% of its height. So the view is square, and
    // the strips left and right of it are LayerViewer's.
    internal const double Aspect = 1;

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

    internal static string Format(double value)
        => value.ToString("G4", CultureInfo.InvariantCulture);

    internal static void Layout(
        Envelope view, Envelope? shape,
        out Vector2 framePosition, out Vector2 frameSize, out Spread<Vector2> grid, out float dotSize,
        out string label, out Vector2 labelPosition, out Vector2 labelSize, out float textSize)
    {
        var step = Step(view.Width);

        framePosition = new Vector2((float)view.Centre.X, (float)-view.Centre.Y);
        frameSize = new Vector2((float)view.Width, (float)view.Height);

        label = shape is null
            ? $"grid {Format(step)}"
            : $"{Format(shape.Width)} × {Format(shape.Height)} · grid {Format(step)}";

        // The label sits in the bottom-left corner, inside the margin Headroom leaves below a
        // freshly fitted shape (a tenth of the height), in a box its own height and about its own
        // width: the Text layer centres the text in that box (seen 2026-11-08: with a box of zero
        // width, half the label hung out of the view). Map coordinates here; Skia's below.
        var text = view.Height * 0.075;
        var margin = view.Height * 0.02;
        var left = view.MinX + margin;
        var bottom = view.MinY + margin;
        var right = left + label.Length * text * 0.6;
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
