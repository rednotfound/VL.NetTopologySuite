using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;
using SkiaSharp;
using Stride.Core.Mathematics;
using VL.Lib.IO;
using VL.Lib.IO.Notifications;
using VL.Skia;

namespace VL.NetTopologySuite;

// The canvas of GeometryEditor's window. The editing rules are SketchSession's (see its header for
// the 2026-11-12 redesign and the editors it was taken from); this class maps pixels to geometry
// (SketchView), draws what the session says, and hands outcomes to the node: Preview ->
// GeometryEditorNode.Preview, Commit -> GeometryEditorNode.Commit (written into the document).
//
// What is on screen:
//   toolbar      Select V · Point P · Line L · Polygon A · Rectangle R · Circle C | Undo · Redo |
//                Snap S · Fit F. Space held + left drag pans (2026-11-14: a touchpad has no
//                right or middle drag, and the left drag is the selection box)
//   canvas       unselected shapes as outlines; the selected one with its vertices and a "+" at the
//                middle of every edge; the selected vertex larger, orange, ringed in white
//   action bar   next to the selection or the shape being drawn: Move · Hole · Delete, Delete point,
//                Done · Cancel, Done · Back one point · Cancel - disabled ones grey, their reason in
//                the status bar when hovered
//   status bar   left: what can be done now (SketchSession.HintText); right: the mouse position in
//                the geometry's units (lon / lat on a geographic SRID), its CRS label (CrsHints),
//                the type and the point count. On lon/lat the view draws x shrunk by cos(latitude)
//
// Pointer: a left drag on empty space draws a selection box (SketchSession, 2026-11-12); right- and
// middle-drags pan; the
// wheel zooms about the cursor; a right-click without a drag finishes drawing or deletes a vertex.
// Every drag waits for DragPixels of movement, so a click never moves anything.
//
// Everything is drawn in window PIXELS (the canvas matrix is reset), so the window's Space setting
// does not matter; mouse positions are scaled from the notification's client area to the viewport,
// which covers DPI scaling. Colours are the GeometryViewer's (Leaflet's #3388ff); selection is orange.

/// <summary>The drawing canvas of the geometry editor window.</summary>
internal sealed class GeometryEditorLayer : ILayer
{
    private const float HitPixels = 9;
    private const float DragPixels = 6;
    private const float ToolbarHeight = 30;
    private const float StatusHeight = 24;

    private static readonly SKColor Blue = new(0x33, 0x88, 0xff);
    private static readonly SKColor LightBlue = new(0x9c, 0xc4, 0xff);
    private static readonly SKColor Orange = new(0xff, 0x99, 0x22);
    private static readonly SKColor Green = new(0x7f, 0xe0, 0xa1);
    private static readonly SKColor Background = new(0x26, 0x26, 0x26);
    private static readonly SKColor Panel = new(0x3a, 0x3a, 0x3a);
    private static readonly SKColor PanelHover = new(0x50, 0x50, 0x50);
    private static readonly SKColor StatusBack = new(0x1c, 0x1c, 0x1c);
    private static readonly SKColor Danger = new(0xff, 0x8f, 0x70);

    // A button: the toolbar's and the action bar's are the same shape.
    private sealed class Button
    {
        public required string Label;
        public string Key = "";
        public required Func<GeometryEditorLayer, bool> IsOn;
        public required Func<GeometryEditorLayer, bool> IsEnabled;
        public required Action<GeometryEditorLayer> Press;
        public Func<GeometryEditorLayer, string?> Reason = _ => null;
        public bool Danger;
        public SKRect Rect;
    }

    private readonly List<Button> _toolbar;
    private readonly List<Button> _actionBar = new();

    private readonly SketchSession _session = new();
    private GeometryEditorNode? _target;
    private SketchView _view;
    private bool _fitted;
    private int _fittedSrid;
    private bool _snap;
    private Vector2 _size = new(800, 600);
    private Vector2 _mouse;
    private float _scaling = 1;
    private string? _note; // a reason shown after pressing a disabled button

    // Panning: a left press the session hands back (empty space), or a right / middle press.
    private bool _leftDown, _rightDown, _middleDown, _panning;
    private bool _spaceHeld, _spaceDrag;
    private Vector2 _pressAt, _lastPan;

    public GeometryEditorLayer()
    {
        _toolbar = new()
        {
            Tool("Select", "V", SketchTool.Select),
            Tool("Point", "P", SketchTool.Point),
            Tool("Line", "L", SketchTool.Line),
            Tool("Polygon", "A", SketchTool.Polygon),
            Tool("Rectangle", "R", SketchTool.Rectangle),
            Tool("Circle", "C", SketchTool.Circle),
            new Button { Label = "Undo", Key = "Ctrl+Z", IsOn = _ => false, IsEnabled = l => l._session.CanUndo, Press = l => l.Apply(l._session.Undo()) },
            new Button { Label = "Redo", Key = "Ctrl+Y", IsOn = _ => false, IsEnabled = l => l._session.CanRedo, Press = l => l.Apply(l._session.Redo()) },
            new Button { Label = "Snap", Key = "S", IsOn = l => l._snap, IsEnabled = _ => true, Press = l => l._snap = !l._snap },
            new Button { Label = "Fit", Key = "F", IsOn = _ => false, IsEnabled = _ => true, Press = l => l._fitted = false },
        };
    }

    private static Button Tool(string label, string key, SketchTool tool) => new()
    {
        Label = label, Key = key,
        IsOn = l => l._session.Tool == tool || (tool == SketchTool.Polygon && l._session.Tool == SketchTool.Hole),
        IsEnabled = l => l.Editable,
        Press = l => l.Apply(l._session.SetTool(tool)),
    };

    public GeometryEditorNode? Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(value, _target)) return;
            if (_session.IsDrawing || _session.IsMoving) Apply(_session.Escape()); // belongs to the old target
            _target = value;
            _fitted = false;
        }
    }

    public RectangleF? Bounds => null;

    private bool Editable => _target is { IsEditable: true };

    // ---- drawing ----------------------------------------------------------------------------

    public void Render(CallerInfo caller)
    {
        var canvas = caller.Canvas;
        var viewport = caller.ViewportBounds;
        _size = new Vector2(viewport.Width, viewport.Height);
        _scaling = Math.Max(1f, caller.Scaling);

        var target = _target;
        if (target is not null) _session.Sync(target.Sketch);
        _session.XScale = _view.XScale; // a circle is drawn round on screen, also on lon/lat
        if (target is not null && target.Srid != _fittedSrid) _fitted = false; // degrees <-> metres changes the aspect

        if (!_fitted)
        {
            var g = target?.Geometry;
            var degrees = target is not null && CrsHints.UnitsOf(target.Srid) == CrsHints.Units.Degrees;
            _view = SketchView.Fit(g is null || g.IsEmpty ? null : g.EnvelopeInternal, new Vector2(_size.X, _size.Y - TopBar - StatusHeight * _scaling), degrees);
            _fittedSrid = target?.Srid ?? 0;
            _fitted = true;
        }

        canvas.Save();
        canvas.ResetMatrix();
        canvas.Translate(viewport.Left, viewport.Top);
        canvas.ClipRect(new SKRect(0, 0, _size.X, _size.Y));
        canvas.Clear(Background);
        DrawGrid(canvas);

        if (target is null)
        {
            DrawToolbar(canvas);
            DrawStatus(canvas, "No GeometryEditor yet: place one in a patch and select it.", "");
            canvas.Restore();
            return;
        }

        if (target.IsEditable) DrawSketch(canvas, _session.Sketch);
        else DrawReadOnly(canvas, target.Geometry);

        DrawToolbar(canvas);
        if (target.IsEditable) DrawActionBar(canvas);

        var left = !target.IsEditable
            ? "Shown only: geometry with Z, nested collections and LINEARRING are not editable here."
            : HoveredReason() ?? _note ?? _session.HintText;
        // The CRS name (from VL.PROJ's catalogue) can be long; when the right side would crowd the hint
        // to a few words, the name goes and the code and units stay (seen 2026-11-12 with EPSG:2263).
        var right = StatusRight(target, named: true);
        using (var measure = new SKPaint { TextSize = 11.5f * _scaling })
            if (measure.MeasureText(right) > _size.X * 0.5f) right = StatusRight(target, named: false);
        DrawStatus(canvas, left, right);
        canvas.Restore();
    }

    private string StatusRight(GeometryEditorNode target, bool named)
    {
        var at = MouseInGeometry;
        var step = _view.GridStep(40 * _scaling);
        var decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step)) + 1, 0, 8);
        var f = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        var srid = CrsHints.Label(target.Srid, named);
        var degrees = CrsHints.UnitsOf(target.Srid) == CrsHints.Units.Degrees;
        var s = _session.Sketch;
        var type = target.IsEditable ? s.ToGeometry().GeometryType.ToUpperInvariant() : target.Geometry.GeometryType.ToUpperInvariant();
        var points = target.IsEditable ? s.VertexCount : target.Geometry.NumPoints;
        var (xName, yName) = degrees ? ("lon", "lat") : ("x", "y");
        return $"{xName} {at.X.ToString(f, CultureInfo.InvariantCulture)}  {yName} {at.Y.ToString(f, CultureInfo.InvariantCulture)} · {srid} · {type} · {points} points";
    }

    private void DrawGrid(SKCanvas canvas)
    {
        var step = _view.GridStep(40 * _scaling);
        var topLeft = _view.ToGeometry(Vector2.Zero, _size);
        var bottomRight = _view.ToGeometry(_size, _size);
        using var dot = new SKPaint { Color = new SKColor(255, 255, 255, 40), IsAntialias = true };
        using var axis = new SKPaint { Color = new SKColor(255, 255, 255, 70), StrokeWidth = _scaling, IsAntialias = true };

        var x0 = Math.Floor(topLeft.X / step) * step;
        var y0 = Math.Floor(bottomRight.Y / step) * step;
        var count = 0;
        for (var x = x0; x <= bottomRight.X && count < 20000; x += step)
            for (var y = y0; y <= topLeft.Y && count < 20000; y += step, count++)
            {
                var p = _view.ToPixels(new Coordinate(x, y), _size);
                canvas.DrawCircle(p.X, p.Y, 1.2f * _scaling, dot);
            }

        var origin = _view.ToPixels(new Coordinate(0, 0), _size);
        canvas.DrawLine(origin.X, 0, origin.X, _size.Y, axis);
        canvas.DrawLine(0, origin.Y, _size.X, origin.Y, axis);
    }

    private void DrawSketch(SKCanvas canvas, GeometrySketch sketch)
    {
        // The part's old place while moving, dashed, for comparison.
        if (_session.BeforeMove is { } before)
        foreach (var moved in _session.MovedParts.Where(m => m < before.PartCount))
        {
            using var ghost = new SKPaint { Color = Orange.WithAlpha(120), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * _scaling, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new[] { 5 * _scaling, 4 * _scaling }, 0) };
            using var path = PathOf(before.Part(moved));
            canvas.DrawPath(path, ghost);
        }

        var hoveredPart = HoveredPart();
        var drawing = _session.DrawingRing;
        for (var p = 0; p < sketch.PartCount; p++)
        {
            var selected = p == _session.SelectedPart || drawing?.Part == p || _session.SelectedParts.Contains(p);
            DrawPart(canvas, sketch.Part(p), selected, p == hoveredPart);
        }

        if (drawing is { } d) DrawRubberBand(canvas, sketch, d.Part, d.Ring);

        // Handles only on the selected part (or the one being drawn): vertices, then the "+".
        var handlesOf = drawing?.Part ?? (_session.Tool == SketchTool.Select ? _session.SelectedPart : -1);
        if (handlesOf >= 0 && handlesOf < sketch.PartCount)
        {
            var hovered = HoveredHandle();
            foreach (var (insert, at) in _session.Midpoints())
                DrawPlus(canvas, _view.ToPixels(at, _size), hovered is { } h && h.Plus && h.Ref == insert);
            DrawVertices(canvas, sketch, handlesOf, hovered);
        }

        if (_session.Box is { } box)
        {
            var a = _view.ToPixels(new Coordinate(box.MinX, box.MaxY), _size);
            var b = _view.ToPixels(new Coordinate(box.MaxX, box.MinY), _size);
            var rect = new SKRect(a.X, a.Y, b.X, b.Y);
            using var boxFill = new SKPaint { Color = LightBlue.WithAlpha(40), Style = SKPaintStyle.Fill };
            using var boxLine = new SKPaint { Color = LightBlue, Style = SKPaintStyle.Stroke, StrokeWidth = _scaling, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new[] { 5 * _scaling, 3 * _scaling }, 0) };
            canvas.DrawRect(rect, boxFill);
            canvas.DrawRect(rect, boxLine);
        }
    }

    private void DrawPart(SKCanvas canvas, SketchPart part, bool selected, bool hovered)
    {
        var colour = selected ? Orange : hovered ? LightBlue : Blue;
        using var fill = new SKPaint { Color = colour.WithAlpha(hovered && !selected ? (byte)90 : (byte)64), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var stroke = new SKPaint { Color = colour, Style = SKPaintStyle.Stroke, StrokeWidth = (hovered && !selected ? 3 : 2) * _scaling, IsAntialias = true };
        if (part.Kind == PartKind.Point)
        {
            // Points are always visible as markers: there is no outline to show otherwise.
            var c = _view.ToPixels(part.Ring(0)[0], _size);
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawCircle(c.X, c.Y, 5.5f * _scaling, selected ? fill : white);
            canvas.DrawCircle(c.X, c.Y, 5.5f * _scaling, stroke);
            return;
        }
        using var path = PathOf(part);
        if (part.Kind == PartKind.Polygon) canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    private SKPath PathOf(SketchPart part)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        for (var ring = 0; ring < part.RingCount; ring++)
            AddPoly(path, part.Ring(ring), part.Kind == PartKind.Polygon && part.VertexCount(ring) >= 3);
        return path;
    }

    private void DrawRubberBand(SKCanvas canvas, GeometrySketch sketch, int partIndex, int ringIndex)
    {
        var part = sketch.Part(partIndex);
        var ring = part.Ring(ringIndex);
        if (ring.Length == 0) return;
        using var band = new SKPaint { Color = Orange.WithAlpha(180), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * _scaling, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new[] { 6 * _scaling, 4 * _scaling }, 0) };
        var last = _view.ToPixels(ring[^1], _size);
        canvas.DrawLine(last.X, last.Y, _mouse.X, _mouse.Y, band);
        if (part.Kind != PartKind.Polygon) return;
        var first = _view.ToPixels(ring[0], _size);
        if (ring.Length >= 2) canvas.DrawLine(_mouse.X, _mouse.Y, first.X, first.Y, band);
        if (ring.Length >= 3)
        {
            // "Click here to close": the first point ringed in green, labelled.
            using var close = new SKPaint { Color = Green, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * _scaling, IsAntialias = true };
            canvas.DrawCircle(first.X, first.Y, 11 * _scaling, close);
            using var text = new SKPaint { Color = Green, TextSize = 11 * _scaling, IsAntialias = true };
            canvas.DrawText("click to close", first.X + 14 * _scaling, first.Y + 18 * _scaling, text);
        }
    }

    private void DrawVertices(SKCanvas canvas, GeometrySketch sketch, int partIndex, (VertexRef Ref, bool Plus)? hovered)
    {
        var part = sketch.Part(partIndex);
        if (part.Kind == PartKind.Point && _session.SelectedVertex is null) return; // the marker is the vertex
        using var ringPaint = new SKPaint { Color = Orange, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * _scaling, IsAntialias = true };
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        using var halo = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * _scaling, IsAntialias = true };
        var r = 4.5f * _scaling;
        for (var ring = 0; ring < part.RingCount; ring++)
        {
            var vertices = part.Ring(ring);
            for (var i = 0; i < vertices.Length; i++)
            {
                var v = new VertexRef(partIndex, ring, i);
                var c = _view.ToPixels(vertices[i], _size);
                var isSelected = _session.SelectedVertex == v;
                var isPicked = _session.SelectedPoints.Contains(v);
                var isHovered = hovered is { } h && !h.Plus && h.Ref == v;
                fill.Color = isSelected || isPicked || isHovered ? Orange : SKColors.White;
                var radius = r * (isSelected ? 1.5f : isPicked || isHovered ? 1.25f : 1f);
                canvas.DrawCircle(c.X, c.Y, radius, fill);
                canvas.DrawCircle(c.X, c.Y, radius, ringPaint);
                if (isSelected)
                {
                    canvas.DrawCircle(c.X, c.Y, radius + 3 * _scaling, halo);
                    using var text = new SKPaint { Color = new SKColor(255, 255, 255, 170), TextSize = 10.5f * _scaling, IsAntialias = true };
                    canvas.DrawText(FormatCoordinate(vertices[i]), c.X + radius + 8 * _scaling, c.Y + 22 * _scaling, text);
                }
            }
        }
    }

    private void DrawPlus(SKCanvas canvas, Vector2 at, bool hovered)
    {
        var r = (hovered ? 7.5f : 6f) * _scaling;
        using var fill = new SKPaint { Color = Orange.WithAlpha(hovered ? (byte)230 : (byte)90), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var stroke = new SKPaint { Color = Orange, Style = SKPaintStyle.Stroke, StrokeWidth = _scaling, IsAntialias = true };
        using var cross = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * _scaling, IsAntialias = true };
        canvas.DrawCircle(at.X, at.Y, r, fill);
        canvas.DrawCircle(at.X, at.Y, r, stroke);
        var a = r * 0.55f;
        canvas.DrawLine(at.X - a, at.Y, at.X + a, at.Y, cross);
        canvas.DrawLine(at.X, at.Y - a, at.X, at.Y + a, cross);
    }

    private void DrawReadOnly(SKCanvas canvas, Geometry geometry)
    {
        using var fill = new SKPaint { Color = Blue.WithAlpha(64), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var stroke = new SKPaint { Color = Blue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * _scaling, IsAntialias = true };
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        GeometryNodes.Outlines(geometry, out var rings, out var lines, out var points);
        foreach (var ring in rings) AddPoly(path, ring, close: true);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
        using var linePath = new SKPath();
        foreach (var line in lines) AddPoly(linePath, line, close: false);
        canvas.DrawPath(linePath, stroke);
        foreach (var c in points)
        {
            var p = _view.ToPixels(c, _size);
            canvas.DrawCircle(p.X, p.Y, 4 * _scaling, stroke);
        }
    }

    private void AddPoly(SKPath path, IReadOnlyList<Coordinate> coordinates, bool close)
    {
        if (coordinates.Count == 0) return;
        var p0 = _view.ToPixels(coordinates[0], _size);
        path.MoveTo(p0.X, p0.Y);
        for (var i = 1; i < coordinates.Count; i++)
        {
            var p = _view.ToPixels(coordinates[i], _size);
            path.LineTo(p.X, p.Y);
        }
        if (close) path.Close();
    }

    // ---- toolbar, action bar, status bar ----------------------------------------------------

    // Ten buttons with their key letters need about 700 px; the window docked beside a patch is
    // often 600 (2026-11-14, after Rectangle and Circle joined). The letters stay (the maintainer:
    // hiding them lost the shortcuts); a group that does not fit moves to a second row, and the
    // canvas starts below however many rows there are.
    private int _toolbarRows = 1;

    private float TopBar => ToolbarHeight * _toolbarRows * _scaling;

    private static readonly int[] GroupStarts = { 0, 6, 8 }; // tools | history | view

    private void DrawToolbar(SKCanvas canvas)
    {
        var x = 8 * _scaling;
        var row = 0;
        for (var g = 0; g < GroupStarts.Length; g++)
        {
            var end = g + 1 < GroupStarts.Length ? GroupStarts[g + 1] : _toolbar.Count;
            var groupWidth = 0f;
            for (var i = GroupStarts[g]; i < end; i++) groupWidth += ButtonWidth(_toolbar[i]) + 5 * _scaling;
            if (g > 0) x += 10 * _scaling;
            if (x > 8 * _scaling && x + groupWidth > _size.X - 8 * _scaling)
            {
                row++;
                x = 8 * _scaling;
            }
            for (var i = GroupStarts[g]; i < end; i++)
            {
                if (x > 8 * _scaling && x + ButtonWidth(_toolbar[i]) > _size.X - 8 * _scaling)
                {
                    row++; // even one group is wider than the window
                    x = 8 * _scaling;
                }
                x = DrawButton(canvas, _toolbar[i], x, (6 + ToolbarHeight * row) * _scaling) + 5 * _scaling;
            }
        }
        _toolbarRows = row + 1;
    }

    private float ButtonWidth(Button b)
    {
        using var text = new SKPaint { TextSize = 12 * _scaling };
        using var keyText = new SKPaint { TextSize = 10 * _scaling };
        var keyWidth = b.Key.Length > 0 ? keyText.MeasureText(b.Key) + 6 * _scaling : 0;
        return text.MeasureText(b.Label) + keyWidth + 18 * _scaling;
    }

    private void DrawActionBar(SKCanvas canvas)
    {
        _actionBar.Clear();
        var actions = _session.Actions();
        if (actions.Count == 0) return;
        foreach (var a in actions)
        {
            var action = a.Action;
            _actionBar.Add(new Button
            {
                Label = a.Label, IsOn = _ => false, IsEnabled = _ => a.Enabled, Reason = _ => a.Reason,
                Danger = action is SketchAction.DeleteShape or SketchAction.DeletePoint or SketchAction.DeleteShapes or SketchAction.DeletePoints,
                Press = l => l.Apply(l._session.Run(action)),
            });
        }

        // Next to what it acts on: above and to the right of the selection's box, kept on screen.
        using var measure = new SKPaint { TextSize = 12 * _scaling };
        var width = _actionBar.Sum(b => measure.MeasureText(b.Label) + 18 * _scaling) + 5 * _scaling * (_actionBar.Count - 1) + 8 * _scaling;
        var h = (ToolbarHeight - 8) * _scaling;
        var box = AnchorBox();
        var x = box is { } bb ? bb.Right + 10 * _scaling : _size.X - width - 10 * _scaling;
        var y = box is { } bt ? bt.Top - h - 10 * _scaling : TopBar + 8 * _scaling;
        x = Math.Clamp(x, 8 * _scaling, Math.Max(8 * _scaling, _size.X - width - 8 * _scaling));
        y = Math.Clamp(y, TopBar + 6 * _scaling, Math.Max(TopBar + 6 * _scaling, _size.Y - (StatusHeight + 8) * _scaling - h - 8 * _scaling));

        using var back = new SKPaint { Color = new SKColor(0x2e, 0x2e, 0x2e), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(x - 4 * _scaling, y - 4 * _scaling, x + width, y + h + 4 * _scaling), 7 * _scaling, 7 * _scaling, back);
        foreach (var b in _actionBar) x = DrawButton(canvas, b, x, y) + 5 * _scaling;
    }

    // The pixel box of the selected part, or of the part being drawn; null when neither.
    private SKRect? AnchorBox()
    {
        var s = _session.Sketch;
        var parts = _session.DrawingRing is { } d ? new[] { d.Part } : _session.MovedParts;
        var e = new Envelope();
        foreach (var part in parts.Where(p => p >= 0 && p < s.PartCount))
            if (s.Part(part).ToGeometry(Defaults.Factory) is { IsEmpty: false } g) e.ExpandToInclude(g.EnvelopeInternal);
        if (e.IsNull) return null;
        var a = _view.ToPixels(new Coordinate(e.MinX, e.MaxY), _size);
        var b = _view.ToPixels(new Coordinate(e.MaxX, e.MinY), _size);
        return new SKRect(a.X, a.Y, b.X, b.Y);
    }

    private float DrawButton(SKCanvas canvas, Button b, float x, float y)
    {
        using var text = new SKPaint { TextSize = 12 * _scaling, IsAntialias = true };
        using var keyText = new SKPaint { Color = new SKColor(255, 255, 255, 120), TextSize = 10 * _scaling, IsAntialias = true };
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        var enabled = b.IsEnabled(this);
        var h = (ToolbarHeight - 8) * _scaling;
        var key = b.Key;
        var keyWidth = key.Length > 0 ? keyText.MeasureText(key) + 6 * _scaling : 0;
        var w = text.MeasureText(b.Label) + keyWidth + 18 * _scaling;
        b.Rect = new SKRect(x, y, x + w, y + h);
        var hovered = b.Rect.Contains(_mouse.X, _mouse.Y);
        fill.Color = b.IsOn(this) ? Blue : hovered && enabled ? PanelHover : Panel;
        canvas.DrawRoundRect(b.Rect, 4 * _scaling, 4 * _scaling, fill);
        text.Color = !enabled ? new SKColor(255, 255, 255, 90) : b.Danger ? Danger : SKColors.White;
        var baseline = y + h / 2 + 4 * _scaling;
        canvas.DrawText(b.Label, x + 9 * _scaling, baseline, text);
        if (key.Length > 0) canvas.DrawText(key, x + 15 * _scaling + text.MeasureText(b.Label), baseline, keyText);
        return x + w;
    }

    private void DrawStatus(SKCanvas canvas, string left, string right)
    {
        var top = _size.Y - StatusHeight * _scaling;
        using var back = new SKPaint { Color = StatusBack, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(0, top, _size.X, _size.Y), back);
        using var leftPaint = new SKPaint { Color = new SKColor(230, 230, 230), TextSize = 12 * _scaling, IsAntialias = true };
        using var rightPaint = new SKPaint { Color = new SKColor(170, 170, 170), TextSize = 11.5f * _scaling, IsAntialias = true };
        var baseline = top + StatusHeight * _scaling / 2 + 4 * _scaling;
        var rightWidth = right.Length > 0 ? rightPaint.MeasureText(right) : 0;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, top, _size.X - rightWidth - 20 * _scaling, _size.Y));
        canvas.DrawText(left, 10 * _scaling, baseline, leftPaint);
        canvas.Restore();
        if (right.Length > 0) canvas.DrawText(right, _size.X - rightWidth - 10 * _scaling, baseline, rightPaint);
    }

    private string? HoveredReason()
        => _actionBar.Concat(_toolbar).FirstOrDefault(b => b.Rect.Contains(_mouse.X, _mouse.Y) && !b.IsEnabled(this))?.Reason(this);

    // ---- what the mouse is over -------------------------------------------------------------

    // Along x, the axis with fewer pixels per unit on lon/lat, so a vertex is never harder to hit there.
    private double Tolerance => HitPixels * _scaling / _view.PixelsPerUnitX;

    private double DragThreshold => DragPixels * _scaling / _view.PixelsPerUnitX;

    private Coordinate MouseInGeometry => _view.ToGeometry(_mouse, _size);

    // Where a new or moved vertex goes: the mouse, rounded to the grid when Snap is on.
    private Coordinate Placed
    {
        get
        {
            var c = MouseInGeometry;
            if (!_snap) return c;
            var step = _view.GridStep(40 * _scaling);
            return new Coordinate(Math.Round(c.X / step) * step, Math.Round(c.Y / step) * step);
        }
    }

    private string FormatCoordinate(Coordinate c)
    {
        var step = _view.GridStep(40 * _scaling);
        var decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step)) + 1, 0, 8);
        var f = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        return c.X.ToString(f, CultureInfo.InvariantCulture) + ", " + c.Y.ToString(f, CultureInfo.InvariantCulture);
    }

    private bool OverUi => _mouse.Y < TopBar || _mouse.Y > _size.Y - StatusHeight * _scaling
                           || _actionBar.Any(b => b.Rect.Contains(_mouse.X, _mouse.Y));

    // In Select, nothing pressed: the part a click would select, for its hover highlight.
    private int HoveredPart()
    {
        if (_session.Tool != SketchTool.Select || _session.IsMoving || _leftDown || OverUi) return -1;
        var part = _session.Sketch.PartAt(MouseInGeometry, Tolerance);
        return part == _session.SelectedPart || _session.SelectedParts.Contains(part) ? -1 : part;
    }

    // The handle of the selected part under the mouse: a vertex, or a "+".
    private (VertexRef Ref, bool Plus)? HoveredHandle()
    {
        if (_session.Tool != SketchTool.Select || _session.IsMoving || OverUi) return null;
        var at = MouseInGeometry;
        var tol = Tolerance;
        var s = _session.Sketch;
        if (_session.SelectedPart >= 0 && _session.SelectedPart < s.PartCount)
        {
            var part = s.Part(_session.SelectedPart);
            for (var r = 0; r < part.RingCount; r++)
            {
                var ring = part.Ring(r);
                for (var i = 0; i < ring.Length; i++)
                    if (ring[i].Distance(at) <= tol) return (new VertexRef(_session.SelectedPart, r, i), false);
            }
        }
        foreach (var (insert, mid) in _session.Midpoints())
            if (mid.Distance(at) <= tol) return (insert, true);
        return null;
    }

    // ---- input ------------------------------------------------------------------------------

    public bool Notify(INotification notification, CallerInfo caller)
    {
        _scaling = Math.Max(1f, caller.Scaling);
        if (notification is MouseNotification mouse)
        {
            // Position is in the client area's units; the viewport is in pixels.
            var client = mouse.ClientArea;
            var viewport = caller.ViewportBounds;
            _mouse = client.X > 0 && client.Y > 0
                ? new Vector2(mouse.Position.X * viewport.Width / client.X, mouse.Position.Y * viewport.Height / client.Y)
                : mouse.Position;
            return OnMouse(mouse);
        }
        if (notification is KeyUpNotification up && up.KeyCode == Keys.Space)
        {
            _spaceHeld = false;
            return true;
        }
        if (notification is KeyDownNotification key)
            return OnKey(key);
        return false;
    }

    private bool OnMouse(MouseNotification mouse)
    {
        switch (mouse)
        {
            case MouseWheelNotification wheel:
                _view = _view.ZoomAt(_mouse, _size, Math.Pow(1.2, wheel.WheelDelta / 120.0));
                return true;

            case MouseDownNotification down when (down.Buttons & MouseButtons.Left) != 0:
                {
                    _note = null;
                    var button = _toolbar.Concat(_actionBar).FirstOrDefault(b => b.Rect.Contains(_mouse.X, _mouse.Y));
                    if (button is not null)
                    {
                        if (button.IsEnabled(this)) button.Press(this);
                        else _note = button.Reason(this);
                        return true;
                    }
                    if (_spaceHeld && !OverUi)
                    {
                        _spaceDrag = true;
                        _panning = true;
                        _lastPan = _mouse;
                        return true;
                    }
                    if (!Editable || OverUi) return false;
                    _leftDown = true;
                    _panning = false;
                    _pressAt = _lastPan = _mouse;
                    // The real mouse decides what is hit, the snapped point where a vertex goes.
                    Apply(_session.LeftDown(MouseInGeometry, Tolerance, Placed, DragThreshold));
                    return true;
                }

            case MouseClickNotification click when (click.Buttons & MouseButtons.Left) != 0 && click.ClickCount == 2:
                if (!Editable || OverUi) return false;
                Apply(_session.DoubleClick(MouseInGeometry, Tolerance, Placed));
                return true;

            case MouseDownNotification down when (down.Buttons & MouseButtons.Right) != 0:
                _rightDown = true;
                _panning = false;
                _pressAt = _lastPan = _mouse;
                return true;

            case MouseDownNotification down when (down.Buttons & MouseButtons.Middle) != 0:
                _middleDown = true;
                _panning = true;
                _lastPan = _mouse;
                return true;

            case MouseMoveNotification:
                if (_rightDown || _middleDown || _spaceDrag)
                {
                    if (!_panning && Vector2.Distance(_mouse, _pressAt) > DragPixels * _scaling) _panning = true;
                    if (_panning)
                    {
                        _view = _view.Pan(_mouse - _lastPan);
                        _lastPan = _mouse;
                    }
                    return true;
                }
                if (Editable) Apply(_session.Move(MouseInGeometry, Placed));
                return true;

            case MouseUpNotification up when (up.Buttons & MouseButtons.Left) != 0 && _spaceDrag:
                _spaceDrag = false;
                _panning = false;
                return true;

            case MouseUpNotification up when (up.Buttons & MouseButtons.Left) != 0:
                if (_leftDown && Editable) Apply(_session.LeftUp());
                _leftDown = false;
                _panning = false;
                return true;

            case MouseUpNotification up when (up.Buttons & MouseButtons.Right) != 0:
                if (_rightDown && !_panning && Editable) Apply(_session.RightClick(MouseInGeometry, Tolerance));
                _rightDown = false;
                _panning = false;
                return true;

            case MouseUpNotification up when (up.Buttons & MouseButtons.Middle) != 0:
                _middleDown = false;
                _panning = false;
                return true;

            case MouseLostNotification:
                // The window lost the mouse mid-gesture: give the gesture back rather than half-apply it.
                if (_leftDown && Editable) Apply(_session.Escape());
                _leftDown = _rightDown = _middleDown = _panning = _spaceDrag = _spaceHeld = false;
                return false;
        }
        return false;
    }

    private bool OnKey(KeyDownNotification key)
    {
        var code = key.KeyCode;
        _note = null;
        if (key.CtrlKey)
        {
            if (code == Keys.Z && !key.ShiftKey) { Apply(_session.Undo()); return true; }
            if (code == Keys.Y || (code == Keys.Z && key.ShiftKey)) { Apply(_session.Redo()); return true; }
            return false;
        }
        switch (code)
        {
            case Keys.F: _fitted = false; return true;
            case Keys.Space: _spaceHeld = true; return true; // held: a left drag pans
            case Keys.S: _snap = !_snap; return true;
        }
        if (!Editable) return false;
        switch (code)
        {
            case Keys.V: Apply(_session.SetTool(SketchTool.Select)); return true;
            case Keys.P: Apply(_session.SetTool(SketchTool.Point)); return true;
            case Keys.L: Apply(_session.SetTool(SketchTool.Line)); return true;
            case Keys.A: Apply(_session.SetTool(SketchTool.Polygon)); return true;
            case Keys.R: Apply(_session.SetTool(SketchTool.Rectangle)); return true;
            case Keys.C: Apply(_session.SetTool(SketchTool.Circle)); return true;
            case Keys.M: Apply(_session.StartMove()); return true;
            case Keys.H: Apply(_session.StartHole()); return true;
            case Keys.Enter: Apply(_session.Enter()); return true;
            case Keys.Escape: Apply(_session.Escape()); return true;
            case Keys.Back: Apply(_session.Backspace()); return true;
            case Keys.Delete: Apply(_session.Delete()); return true;
        }
        return false;
    }

    private void Apply(SketchOutcome outcome)
    {
        var target = _target;
        if (target is null) return;
        switch (outcome)
        {
            case SketchOutcome.Preview: target.Preview(_session.Sketch); break;
            case SketchOutcome.Commit: target.Commit(_session.Sketch); break;
        }
    }
}
