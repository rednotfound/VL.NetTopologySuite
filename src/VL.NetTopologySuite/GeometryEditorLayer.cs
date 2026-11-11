using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using SkiaSharp;
using Stride.Core.Mathematics;
using VL.Lib.IO;
using VL.Lib.IO.Notifications;
using VL.Skia;

namespace VL.NetTopologySuite;

// The canvas of GeometryEditor's window: a toolbar, a dot grid, the target editor's sketch, and the
// translation of mouse and keys into SketchSession calls. The editing rules are SketchSession's;
// this class only maps pixels to geometry (SketchView), draws, and hands the outcome to the node:
// Preview -> GeometryEditorNode.Preview, Commit -> GeometryEditorNode.Commit (written back).
//
// Everything is drawn in window PIXELS (the layer resets the canvas matrix), so the window's Space
// setting does not matter; mouse positions are scaled from the notification's client area to the
// viewport, which covers DPI scaling.
//
// Toolbar (also keys): Select V, Point P, Line L, Polygon A, Hole H | Snap S, Fit F, Delete Del.
// Right-drag or middle-drag pans, the wheel zooms about the cursor; a right-click without a drag
// is the session's (finish drawing / delete a vertex). Snap rounds every new or moved vertex to
// the grid shown, so a drawing made with it has short WKT (the first one was all 16-digit numbers).
//
// Colours are the GeometryViewer's (Leaflet's #3388ff), so the window and the IOBox agree; the
// selected part is drawn orange.

/// <summary>The drawing canvas of the geometry editor window.</summary>
internal sealed class GeometryEditorLayer : ILayer
{
    private const float HitPixels = 9;
    private const float ClickSlopPixels = 4;
    private const float ToolbarHeight = 30;

    private static readonly SKColor Blue = new(0x33, 0x88, 0xff);
    private static readonly SKColor Orange = new(0xff, 0x99, 0x22);
    private static readonly SKColor Background = new(0x26, 0x26, 0x26);
    private static readonly SKColor ButtonFill = new(0x3a, 0x3a, 0x3a);
    private static readonly SKColor ButtonActive = new(0x33, 0x88, 0xff);

    private sealed record Button(string Label, string Key, Action<GeometryEditorLayer> Press, Func<GeometryEditorLayer, bool> IsOn)
    {
        public SKRect Rect;
    }

    private readonly List<Button> _buttons = new()
    {
        Tool("Select", "V", SketchTool.Select),
        Tool("Point", "P", SketchTool.Point),
        Tool("Line", "L", SketchTool.Line),
        Tool("Polygon", "A", SketchTool.Polygon),
        Tool("Hole", "H", SketchTool.Hole),
        new("Snap", "S", l => l._snap = !l._snap, l => l._snap),
        new("Fit", "F", l => l._fitted = false, _ => false),
        new("Delete", "Del", l => l.Apply(l._session.Delete()), _ => false),
    };

    private static Button Tool(string label, string key, SketchTool tool)
        => new(label, key, l => l.Apply(l._session.SetTool(tool)), l => l._session.Tool == tool);

    private readonly SketchSession _session = new();
    private GeometryEditorNode? _target;
    private SketchView _view;
    private bool _fitted;
    private bool _snap;
    private Vector2 _size = new(800, 600);
    private Vector2 _mouse;
    private float _scaling = 1;

    // Panning with the right or middle button; a right press that does not move is a click.
    private bool _rightDown;
    private bool _middleDown;
    private bool _panning;
    private Vector2 _pressAt;
    private Vector2 _lastPan;

    public GeometryEditorNode? Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(value, _target)) return;
            if (_session.IsDrawing) Apply(_session.Escape()); // a half-drawn part belongs to the old target
            _target = value;
            _fitted = false;
        }
    }

    public RectangleF? Bounds => null;

    // ---- drawing ----------------------------------------------------------------------------

    public void Render(CallerInfo caller)
    {
        var canvas = caller.Canvas;
        var viewport = caller.ViewportBounds;
        _size = new Vector2(viewport.Width, viewport.Height);
        _scaling = Math.Max(1f, caller.Scaling);

        var target = _target;
        if (target is not null) _session.Sync(target.Sketch);

        if (!_fitted)
        {
            var g = target?.Geometry;
            _view = SketchView.Fit(g is null || g.IsEmpty ? null : g.EnvelopeInternal, _size);
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
            DrawLine(canvas, 0, "No GeometryEditor yet: place one in a patch and select it.");
            canvas.Restore();
            return;
        }

        if (target.IsEditable)
            DrawSketch(canvas, _session.Sketch);
        else
            DrawReadOnly(canvas, target.Geometry);

        DrawToolbar(canvas);
        DrawLine(canvas, 0, target.IsEditable ? Summary(_session.Sketch) : "Shown only: geometry with Z, nested collections and LINEARRING are not editable here.");
        DrawLine(canvas, 1, _session.Hint ?? HelpFor(_session.Tool, _session.IsDrawing));
        var wkt = target.IsEditable ? _session.Sketch.ToWKT() : "";
        if (wkt.Length > 180) wkt = wkt.Substring(0, 177) + "...";
        DrawLine(canvas, -1, wkt);

        canvas.Restore();
    }

    private static string Summary(GeometrySketch s)
    {
        var type = s.ToGeometry().GeometryType.ToUpperInvariant();
        return $"{type} · {s.PartCount} part{(s.PartCount == 1 ? "" : "s")} · {s.VertexCount} vertices";
    }

    private static string HelpFor(SketchTool tool, bool drawing) => tool switch
    {
        SketchTool.Select => "click to select · drag a vertex or a shape · double-click an edge to add a vertex · right-click a vertex to delete it · Del deletes the shape",
        SketchTool.Point => "click to add a point",
        SketchTool.Line when drawing => "click to add · double-click, Enter or right-click to finish · Backspace undoes a point · Esc cancels",
        SketchTool.Line => "click to start a line",
        SketchTool.Polygon when drawing => "click to add · click the first point, double-click or Enter to close · Backspace · Esc cancels",
        SketchTool.Polygon => "click to start a polygon",
        SketchTool.Hole when drawing => "click to add · click the first point, double-click or Enter to close · Esc cancels",
        _ => "click inside a polygon to start a hole in it",
    };

    private void DrawToolbar(SKCanvas canvas)
    {
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        using var text = new SKPaint { Color = SKColors.White, TextSize = 12 * _scaling, IsAntialias = true };
        using var keyText = new SKPaint { Color = new SKColor(255, 255, 255, 140), TextSize = 10 * _scaling, IsAntialias = true };
        var x = 8 * _scaling;
        var y = 6 * _scaling;
        var h = (ToolbarHeight - 8) * _scaling;
        for (var i = 0; i < _buttons.Count; i++)
        {
            var b = _buttons[i];
            if (i == 5) x += 10 * _scaling; // a gap between the tools and the toggles
            var w = text.MeasureText(b.Label) + keyText.MeasureText(b.Key) + 22 * _scaling;
            b.Rect = new SKRect(x, y, x + w, y + h);
            var hovered = b.Rect.Contains(_mouse.X, _mouse.Y);
            fill.Color = b.IsOn(this) ? ButtonActive : hovered ? new SKColor(0x50, 0x50, 0x50) : ButtonFill;
            canvas.DrawRoundRect(b.Rect, 4 * _scaling, 4 * _scaling, fill);
            var baseline = y + h / 2 + 4 * _scaling;
            canvas.DrawText(b.Label, x + 8 * _scaling, baseline, text);
            canvas.DrawText(b.Key, x + 12 * _scaling + text.MeasureText(b.Label), baseline, keyText);
            x += w + 5 * _scaling;
        }
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
        for (var p = 0; p < sketch.PartCount; p++)
            DrawPart(canvas, sketch.Part(p), p == _session.SelectedPart);

        // The rubber band from the last vertex to the mouse while drawing.
        if (_session.DrawingRing is { } d)
        {
            var ring = sketch.Part(d.Part).Ring(d.Ring);
            if (ring.Length > 0)
            {
                using var band = new SKPaint { Color = Orange.WithAlpha(180), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * _scaling, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new[] { 6 * _scaling, 4 * _scaling }, 0) };
                var last = _view.ToPixels(ring[^1], _size);
                canvas.DrawLine(last.X, last.Y, _mouse.X, _mouse.Y, band);
                if (sketch.Part(d.Part).Kind == PartKind.Polygon && ring.Length >= 2)
                {
                    var first = _view.ToPixels(ring[0], _size);
                    canvas.DrawLine(_mouse.X, _mouse.Y, first.X, first.Y, band);
                }
            }
        }

        // Vertices on top, the one under the mouse (or being dragged) highlighted.
        var hovered = _session.DraggedVertex ?? sketch.NearestVertex(MouseInGeometry, Tolerance);
        using var ringPaint = new SKPaint { Color = Blue, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * _scaling, IsAntialias = true };
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        var r = 4.5f * _scaling;
        for (var p = 0; p < sketch.PartCount; p++)
        {
            var part = sketch.Part(p);
            ringPaint.Color = p == _session.SelectedPart ? Orange : Blue;
            for (var ring = 0; ring < part.RingCount; ring++)
            {
                var vertices = part.Ring(ring);
                for (var i = 0; i < vertices.Length; i++)
                {
                    var c = _view.ToPixels(vertices[i], _size);
                    var isHovered = hovered is { } h && h.Part == p && h.Ring == ring && h.Index == i;
                    fill.Color = isHovered ? Orange : SKColors.White;
                    var radius = part.Kind == PartKind.Point ? r * 1.3f : r;
                    canvas.DrawCircle(c.X, c.Y, radius, fill);
                    canvas.DrawCircle(c.X, c.Y, radius, ringPaint);
                }
            }
        }
    }

    private void DrawPart(SKCanvas canvas, SketchPart part, bool selected)
    {
        var colour = selected ? Orange : Blue;
        using var fill = new SKPaint { Color = colour.WithAlpha(64), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var stroke = new SKPaint { Color = colour, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * _scaling, IsAntialias = true };
        if (part.Kind == PartKind.Point) return; // drawn as a vertex marker

        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        for (var ring = 0; ring < part.RingCount; ring++)
        {
            var close = part.Kind == PartKind.Polygon && part.VertexCount(ring) >= 3;
            AddPoly(path, part.Ring(ring), close);
        }
        if (part.Kind == PartKind.Polygon) canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
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

    // Row 0 and 1 under the toolbar, row -1 at the bottom.
    private void DrawLine(SKCanvas canvas, int row, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = new SKPaint { Color = new SKColor(255, 255, 255, 200), TextSize = 12 * _scaling, IsAntialias = true };
        var line = 17 * _scaling;
        var y = row >= 0 ? (ToolbarHeight + 14) * _scaling + line * row : _size.Y - 10 * _scaling;
        canvas.DrawText(text, 10 * _scaling, y, paint);
    }

    // ---- input ------------------------------------------------------------------------------

    private double Tolerance => HitPixels * _scaling / _view.PixelsPerUnit;

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
        if (notification is KeyDownNotification key)
            return OnKey(key.KeyCode);
        return false;
    }

    private bool Editable => _target is { IsEditable: true };

    private bool OnMouse(MouseNotification mouse)
    {
        switch (mouse)
        {
            case MouseWheelNotification wheel:
                _view = _view.ZoomAt(_mouse, _size, Math.Pow(1.2, wheel.WheelDelta / 120.0));
                return true;

            case MouseDownNotification down when (down.Buttons & MouseButtons.Left) != 0:
                {
                    var button = _buttons.FirstOrDefault(b => b.Rect.Contains(_mouse.X, _mouse.Y));
                    if (button is not null)
                    {
                        button.Press(this);
                        return true;
                    }
                    if (!Editable) return false;
                    // The real mouse decides what is hit, the snapped point where a vertex goes.
                    Apply(_session.LeftDown(MouseInGeometry, Tolerance, Placed));
                    return true;
                }

            case MouseClickNotification click when (click.Buttons & MouseButtons.Left) != 0 && click.ClickCount == 2:
                if (!Editable) return false;
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
                if (_rightDown || _middleDown)
                {
                    if (!_panning && Vector2.Distance(_mouse, _pressAt) > ClickSlopPixels) _panning = true;
                    if (_panning)
                    {
                        _view = _view.Pan(_mouse - _lastPan);
                        _lastPan = _mouse;
                    }
                    return true;
                }
                if (Editable) Apply(_session.Move(Placed));
                return true;

            case MouseUpNotification up when (up.Buttons & MouseButtons.Left) != 0:
                if (Editable) Apply(_session.LeftUp());
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
                // The window lost the mouse mid-gesture: keep what was dragged so far.
                if (Editable) Apply(_session.LeftUp());
                _rightDown = _middleDown = _panning = false;
                return false;
        }
        return false;
    }

    private bool OnKey(Keys key)
    {
        var button = _buttons.FirstOrDefault(b => KeyOf(b) == key);
        if (button is not null)
        {
            if (Editable || button.Label is "Fit" or "Snap") button.Press(this);
            return true;
        }
        if (!Editable) return false;
        switch (key)
        {
            case Keys.Enter: Apply(_session.Enter()); return true;
            case Keys.Escape: Apply(_session.Escape()); return true;
            case Keys.Back: Apply(_session.Backspace()); return true;
        }
        return false;
    }

    private static Keys KeyOf(Button b) => b.Key switch
    {
        "V" => Keys.V,
        "P" => Keys.P,
        "L" => Keys.L,
        "A" => Keys.A,
        "H" => Keys.H,
        "S" => Keys.S,
        "F" => Keys.F,
        "Del" => Keys.Delete,
        _ => Keys.None,
    };

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
