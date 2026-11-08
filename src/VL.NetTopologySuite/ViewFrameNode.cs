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
// - A STICKY view (hysteresis). It keeps the last frame while the shape fits inside it and is at
//   least a quarter of its size; only then does it re-fit, with headroom, so the next edits show as
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
    /// <param name="textSize">The height of the label text.</param>
    public void Update(
        Geometry? geometry,
        out Vector2 framePosition,
        out Vector2 frameSize,
        out Spread<Vector2> grid,
        out float dotSize,
        out string label,
        out Vector2 labelPosition,
        out float textSize)
    {
        var shape = geometry is null || geometry.IsEmpty ? null : geometry.EnvelopeInternal;
        _view = ViewFrame.Next(_view, shape);
        ViewFrame.Layout(_view, shape, out framePosition, out frameSize, out grid, out dotSize,
            out label, out labelPosition, out textSize);
    }
}

internal static class ViewFrame
{
    // The shape takes up 1 / Headroom of a fresh view, so it can grow by that much before the view
    // moves; it re-fits when it shrinks below 1 / ShrinkBelow of the view.
    internal const double Headroom = 1.6;
    internal const double ShrinkBelow = 4;

    internal static Envelope Next(Envelope? view, Envelope? shape)
    {
        if (shape is null)
            return view ?? new Envelope(-0.5, 0.5, -0.5, 0.5);
        if (view is null || !view.Contains(shape) || Extent(shape) * ShrinkBelow < Extent(view))
            return Fit(shape);
        return view;
    }

    // The larger side, never zero: a horizontal line or a single point still gets a frame.
    internal static double Extent(Envelope e)
    {
        var size = Math.Max(e.Width, e.Height);
        return size > 0 ? size : 1;
    }

    // Centred on the shape, Headroom times its larger side in both directions, so the frame is a
    // square and a shape growing in either direction has the same room.
    internal static Envelope Fit(Envelope shape)
    {
        var half = Extent(shape) * Headroom / 2;
        var c = shape.Centre;
        return new Envelope(c.X - half, c.X + half, c.Y - half, c.Y + half);
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
        out string label, out Vector2 labelPosition, out float textSize)
    {
        var extent = Extent(view);
        var step = Step(extent);

        framePosition = new Vector2((float)view.Centre.X, (float)-view.Centre.Y);
        frameSize = new Vector2((float)view.Width, (float)view.Height);

        var builder = new SpreadBuilder<Vector2>();
        var x0 = Math.Ceiling(view.MinX / step);
        var y0 = Math.Ceiling(view.MinY / step);
        for (var i = x0; i * step <= view.MaxX && i - x0 < 40; i++)
            for (var j = y0; j * step <= view.MaxY && j - y0 < 40; j++)
                builder.Add(new Vector2((float)(i * step), (float)-(j * step)));
        grid = builder.ToSpread();

        dotSize = (float)(extent * 0.018);
        textSize = (float)(extent * 0.07);
        label = shape is null
            ? $"grid {Format(step)}"
            : $"{Format(shape.Width)} × {Format(shape.Height)} · grid {Format(step)}";
        // Bottom-left on a map is the smallest x and the smallest y, which in Skia space is the
        // largest y - hence the minus on MinY.
        labelPosition = new Vector2((float)(view.MinX + extent * 0.03), (float)-(view.MinY + extent * 0.03));
    }
}
