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
// became lower, a Circle looked the same at every radius. So GeometryViewer decides its own view:
// this node picks it, and hands out the Scaling and Translation that put it onto the viewer's
// LayerWidget, plus the two things a map has for scale:
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
// The arithmetic is ViewFrame (ViewFrame.cs), internal and tested; this class only holds the state.

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
    /// <param name="grid">The grid points inside the view, in Skia space.</param>
    /// <param name="dotSize">The size of a grid dot.</param>
    /// <param name="label">The geometry's width and height, and the grid step.</param>
    /// <param name="labelPosition">The bottom-left corner of the label, in Skia space.</param>
    /// <param name="labelSize">The box the label is drawn in.</param>
    /// <param name="textSize">The height of the label text.</param>
    /// <param name="scaling">Scales the view into the viewer's widget.</param>
    /// <param name="translation">Moves the view into the viewer's widget.</param>
    public void Update(
        Geometry? geometry,
        out Spread<Vector2> grid,
        out float dotSize,
        out string label,
        out Vector2 labelPosition,
        out Vector2 labelSize,
        out float textSize,
        out Vector2 scaling,
        out Vector2 translation)
    {
        var shape = geometry is null || geometry.IsEmpty ? null : geometry.EnvelopeInternal;
        _view = ViewFrame.Next(_view, shape);
        ViewFrame.Layout(_view, shape, out grid, out dotSize,
            out label, out labelPosition, out labelSize, out textSize);
        ViewFrame.ToWidget(_view, out scaling, out translation);
    }
}
