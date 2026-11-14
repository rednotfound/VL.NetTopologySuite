using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace VL.NetTopologySuite;

// The editing rules of GeometryEditor's window as a state machine, in geometry coordinates, with no
// Skia and no vvvv - so every gesture is a test (SketchSessionTests). GeometryEditorLayer turns
// pixels into coordinates, calls this, draws what it says, and passes the outcome to the node:
// Preview shows a sketch at once, Commit also writes it into the document.
//
// Redesigned 2026-11-12 after the maintainer found two things: adding a point to an edge was lost,
// and "I wanted to add a point and moved the whole shape instead". The cause was one Select tool in
// which the same left-drag meant select / move vertex / move shape / insert, depending on a few
// pixels of hit-testing. A survey of 15 editors (Mapbox GL Draw, iD, QGIS, ArcGIS, Leaflet-Geoman,
// OpenLayers, JOSM, Figma, Illustrator, Inkscape, Excalidraw, tldraw, draw.io, PowerPoint, Keynote;
// the maintainer's design page, 2026-11-12) gave the rules, each used by several of them:
//
//   1. Select first, then edit. Vertex handles exist only on the SELECTED part; an unselected part
//      shows its outline and cannot be reshaped by a stray click.
//   2. Adding a point is a "+" handle at the middle of each edge of the selected part: click it to
//      add a point there, or drag it to pull the new point out.
//   3. Moving a whole part is its own mode, entered with Move (or M) and left with Done / Enter
//      or Cancel / Esc. Dragging a part's body otherwise does nothing.
//   4. A drag starts only past a threshold (6 px from the layer; JOSM 5 px, Excalidraw 10 px), so
//      a click never moves anything.
//   5. Actions sit next to the selection (Actions()), and one that cannot be done stays, disabled,
//      with the reason - a triangle's vertex cannot be deleted, rather than the triangle vanishing.
//   6. One line of hint says what can be done now (HintText).
//   7. Undo / Redo of every committed edit, in the window itself.
//
// Decisions the maintainer took on the design page: after finishing a line or polygon the tool goes
// back to Select with the new part selected - Point too since 2026-11-14 (the maintainer: placing
// point after point by default was too far from the other tools and too easy to misclick); dragging empty space
// pans the view; dragging a selected part's body does NOT move it.
//
// A part being drawn is only previewed and committed when finished, so one stroke is one undo step
// and a half-drawn part never reaches the document. A move is previewed and committed on Done.
//
// Box selection (2026-11-12, the maintainer: "很多时候就是要框选来删除的"). The two choices were
// theirs:
//   - A left drag on empty space draws a box, as in JOSM, Figma and Illustrator. Before this, the
//     same drag panned the view like a web map. Panning is now the right or middle drag, and the
//     wheel zooms.
//   - What the box takes depends on the selection, as in JOSM and QGIS's vertex tool. With one shape
//     selected, it takes that shape's points. When it catches none of them, or nothing was
//     selected, it takes every shape lying wholly inside it.
// The box is a WINDOW selection: a shape only partly inside is not taken, because the box is
// mostly used for deleting, and a shape grazed by the box edge should not vanish.
// Several shapes can be moved (Move) and deleted together. Several points can be deleted together,
// unless that would leave a line under 2 points or a ring under 3; the action then stays disabled
// and says which, in the same style as the single-point rule.

/// <summary>A tool of the geometry editor window.</summary>
internal enum SketchTool
{
    Select,
    Point,
    Line,
    Polygon,

    /// <summary>Drawing a hole in a polygon; entered from that polygon's Hole action, not the toolbar.</summary>
    Hole,

    /// <summary>A rectangle, dragged from one corner to the other.</summary>
    Rectangle,

    /// <summary>A circle, dragged from the centre outwards.</summary>
    Circle,
}

/// <summary>What an input did to the sketch.</summary>
internal enum SketchOutcome
{
    /// <summary>Nothing changed.</summary>
    None,

    /// <summary>Show the sketch, do not write it yet.</summary>
    Preview,

    /// <summary>Show the sketch and write it into the document.</summary>
    Commit,
}

/// <summary>An action the window offers next to the selection.</summary>
internal enum SketchAction
{
    Move,
    Hole,
    DeleteShape,
    DeletePoint,
    DeleteShapes,
    DeletePoints,
    Done,
    Cancel,
    Finish,
    BackOnePoint,
}

/// <summary>An action as the floating bar shows it: enabled, or disabled with the reason.</summary>
internal readonly record struct ActionState(SketchAction Action, string Label, bool Enabled, string? Reason = null);

/// <summary>The editing state of the geometry editor window.</summary>
internal sealed class SketchSession
{
    private GeometrySketch _sketch = GeometrySketch.Empty;
    private SketchTool _tool = SketchTool.Select;

    // History: the last state written into the document, and what came before and after it.
    private GeometrySketch _committed = GeometrySketch.Empty;
    private readonly Stack<GeometrySketch> _undo = new();
    private readonly Stack<GeometrySketch> _redo = new();

    // A part or hole being drawn: which part, which ring, and the sketch before it began (Esc).
    private int _drawPart = -1;
    private int _drawRing = -1;
    private int _holeTarget = -1;
    private GeometrySketch? _beforeDrawing;

    // A left press in Select: what it landed on, where, and whether it has become a drag.
    private enum Press { None, Vertex, Insert, MoveShape, Box, Shape }
    private Coordinate? _shapeFrom;
    private Press _press;
    private Coordinate? _boxTo;
    private bool _boxForPoints;
    private bool _boxOnBody;
    private VertexRef _pressRef;
    private Coordinate? _pressAt;
    private Coordinate? _dragLast;
    private double _threshold;
    private bool _dragging;
    private GeometrySketch? _beforeDrag;

    // Move mode: entered with the Move action, the part follows every drag until Done or Cancel.
    private bool _moving;
    private GeometrySketch? _beforeMove;

    public GeometrySketch Sketch => _sketch;

    public SketchTool Tool => _tool;

    /// <summary>The selected part, -1 when none.</summary>
    public int SelectedPart { get; private set; } = -1;

    /// <summary>The selected vertex of the selected part, null when none.</summary>
    public VertexRef? SelectedVertex { get; private set; }

    // Several shapes (SelectedPart is then -1) or several points of SelectedPart, from a box; empty
    // otherwise. Never a single one: one shape or point caught by a box becomes the plain selection.
    private List<int> _parts = new();
    private List<VertexRef> _points = new();

    /// <summary>The shapes a box selected, when it caught two or more; empty otherwise.</summary>
    public IReadOnlyList<int> SelectedParts => _parts;

    /// <summary>The points of the selected shape a box selected, when it caught two or more; empty otherwise.</summary>
    public IReadOnlyList<VertexRef> SelectedPoints => _points;

    /// <summary>The selection box being dragged, in geometry units; null when there is none.</summary>
    public Envelope? Box => _press == Press.Box && _dragging && _pressAt is not null && _boxTo is not null
        ? new Envelope(_pressAt, _boxTo)
        : null;

    /// <summary>A one-off message (why something was refused), shown instead of the hint until the next input.</summary>
    public string? Message { get; private set; }

    public bool IsDrawing => _drawPart >= 0;

    public bool IsMoving => _moving;

    private void ClearMany()
    {
        _parts = new();
        _points = new();
    }

    /// <summary>True while a part is being drawn, dragged or moved: the node's sketch is not taken then.</summary>
    public bool IsBusy => _drawPart >= 0 || _moving || (_press is Press.Vertex or Press.Insert or Press.MoveShape or Press.Shape && _dragging);

    /// <summary>The ring being drawn, for the rubber band; null when not drawing.</summary>
    public (int Part, int Ring)? DrawingRing => _drawPart >= 0 ? (_drawPart, _drawRing) : null;

    /// <summary>The sketch before the move began, for the ghost outline; null when not moving.</summary>
    public GeometrySketch? BeforeMove => _moving ? _beforeMove : null;

    public bool CanUndo => _undo.Count > 0 && !_moving && _drawPart < 0;

    public bool CanRedo => _redo.Count > 0 && !_moving && _drawPart < 0;

    /// <summary>Takes the sketch the node holds now, unless a gesture is in progress.</summary>
    public void Sync(GeometrySketch sketch)
    {
        if (IsBusy || ReferenceEquals(sketch, _sketch)) return;
        _sketch = sketch;
        if (!ReferenceEquals(sketch, _committed))
        {
            // Changed outside the window (typed WKT, Ctrl+Z in the patch): this window's history no
            // longer describes the document, so it starts again from here.
            _committed = sketch;
            _undo.Clear();
            _redo.Clear();
            // ...and its selection pointed into the old content: index 0 of a different shape is
            // not the shape the user picked.
            SelectedPart = -1;
            SelectedVertex = null;
            ClearMany();
        }
        Validate();
    }

    private void Validate()
    {
        if (SelectedPart >= _sketch.PartCount) SelectedPart = -1;
        if (SelectedVertex is { } v && (SelectedPart != v.Part || !Exists(_sketch, v))) SelectedVertex = null;
        if (_parts.Any(p => p < 0 || p >= _sketch.PartCount)) _parts = new();
        if (_points.Any(p => p.Part != SelectedPart || !Exists(_sketch, p))) _points = new();
    }

    private static bool Exists(GeometrySketch s, VertexRef v)
        => v.Part >= 0 && v.Part < s.PartCount
        && v.Ring >= 0 && v.Ring < s.Part(v.Part).RingCount
        && v.Index >= 0 && v.Index < s.Part(v.Part).VertexCount(v.Ring);

    private SketchOutcome Committed()
    {
        if (ReferenceEquals(_sketch, _committed)) return SketchOutcome.None;
        _undo.Push(_committed);
        _redo.Clear();
        _committed = _sketch;
        return SketchOutcome.Commit;
    }

    /// <summary>Changes the tool, finishing a part being drawn and keeping a move.</summary>
    public SketchOutcome SetTool(SketchTool tool)
    {
        Message = null;
        var outcome = SketchOutcome.None;
        if (_drawPart >= 0) outcome = Finish();
        if (_moving) outcome = Max(outcome, DoneMove());
        _tool = tool;
        if (tool != SketchTool.Select)
        {
            SelectedPart = -1;
            SelectedVertex = null;
            ClearMany();
        }
        return outcome;
    }

    private static SketchOutcome Max(SketchOutcome a, SketchOutcome b) => (SketchOutcome)System.Math.Max((int)a, (int)b);

    // ---- pointer ---------------------------------------------------------------------------

    // Two positions per press (2026-11-11): AT is where the mouse really is and decides what is hit;
    // PLACE is where a new or moved vertex goes - the same point, or the grid point with Snap on.
    // dragThreshold is in geometry units (the layer converts its 6 pixels); a press becomes a drag
    // only once the mouse is that far from where it went down.

    /// <summary>A left press at <paramref name="at"/>; a vertex it creates goes to <paramref name="place"/> (default: at).</summary>
    public SketchOutcome LeftDown(Coordinate at, double tolerance, Coordinate? place = null, double dragThreshold = -1)
    {
        Message = null;
        place ??= at;
        _threshold = dragThreshold > 0 ? dragThreshold : tolerance * 0.66;
        _pressAt = at.Copy();
        _dragging = false;
        _beforeDrag = _sketch;

        switch (_tool)
        {
            case SketchTool.Select:
                return SelectDown(at, place, tolerance);

            case SketchTool.Point:
                _press = Press.None;
                _sketch = _sketch.AddPart(PartKind.Point, Snapped(at, place, tolerance));
                _tool = SketchTool.Select; // one point, like every other tool; P again for the next
                SelectedPart = _sketch.PartCount - 1;
                SelectedVertex = null;
                return Committed();

            case SketchTool.Rectangle or SketchTool.Circle:
                _press = Press.Shape;
                _shapeFrom = place.Copy();
                return SketchOutcome.None;

            default:
                _press = Press.None;
                return DrawDown(at, place, tolerance);
        }
    }

    private SketchOutcome SelectDown(Coordinate at, Coordinate place, double tolerance)
    {
        if (_moving)
        {
            _press = Press.MoveShape;
            _dragLast = place.Copy();
            return SketchOutcome.None;
        }

        if (SelectedPart >= 0)
        {
            if (NearestVertexIn(SelectedPart, at, tolerance) is { } v)
            {
                _press = Press.Vertex;
                _pressRef = v;
                SelectedVertex = v;
                _points = new();
                return SketchOutcome.None;
            }
            if (NearestMidpointIn(SelectedPart, at, tolerance) is { } m)
            {
                _press = Press.Insert;
                _pressRef = m;
                return SketchOutcome.None;
            }
        }

        var part = _sketch.PartAt(at, tolerance);
        if (part >= 0 && part != SelectedPart)
        {
            // Selecting another part: never a move (rule 3).
            SelectedPart = part;
            SelectedVertex = null;
            ClearMany();
            _press = Press.None;
            return SketchOutcome.None;
        }

        // Empty space, or the body of the selected part: a box may start here. A click without a
        // drag decides on release (LeftUp): on empty space it deselects, on the body it keeps the
        // part and drops a selected point.
        _press = Press.Box;
        _boxTo = at.Copy();
        _boxForPoints = SelectedPart >= 0;
        _boxOnBody = part >= 0;
        return SketchOutcome.None;
    }

    // A box released: the selected part's points inside it, or else every part wholly inside it.
    private void SelectInBox(Envelope box)
    {
        if (_boxForPoints && SelectedPart >= 0 && SelectedPart < _sketch.PartCount)
        {
            var part = _sketch.Part(SelectedPart);
            var inside = new List<VertexRef>();
            for (var r = 0; r < part.RingCount; r++)
            {
                var ring = part.Ring(r);
                for (var i = 0; i < ring.Length; i++)
                    if (box.Covers(ring[i])) inside.Add(new VertexRef(SelectedPart, r, i));
            }
            if (inside.Count > 0)
            {
                SelectedVertex = inside.Count == 1 ? inside[0] : null;
                _points = inside.Count > 1 ? inside : new();
                return;
            }
        }

        var parts = new List<int>();
        for (var p = 0; p < _sketch.PartCount; p++)
            if (_sketch.Part(p).ToGeometry(Defaults.Factory) is { IsEmpty: false } g && box.Covers(g.EnvelopeInternal))
                parts.Add(p);
        SelectedVertex = null;
        _points = new();
        SelectedPart = parts.Count == 1 ? parts[0] : -1;
        _parts = parts.Count > 1 ? parts : new();
    }

    public SketchOutcome Move(Coordinate at, Coordinate? place = null)
    {
        place ??= at;
        if (_press is Press.None || _pressAt is null) return SketchOutcome.None;

        if (_press == Press.Shape && _shapeFrom is not null && _beforeDrag is not null)
        {
            if (!_dragging && at.Distance(_pressAt) <= _threshold) return SketchOutcome.None;
            _dragging = true;
            _sketch = ShapePart(_shapeFrom, place) is { } shape ? _beforeDrag.AddPart(shape) : _beforeDrag;
            return SketchOutcome.Preview;
        }

        if (_press == Press.Box)
        {
            // The box follows the real mouse, not the snapped point.
            if (!_dragging && at.Distance(_pressAt) <= _threshold) return SketchOutcome.None;
            _dragging = true;
            _boxTo = at.Copy();
            return SketchOutcome.None;
        }

        if (!_dragging)
        {
            if (at.Distance(_pressAt) <= _threshold) return SketchOutcome.None;
            _dragging = true;
            if (_press == Press.Insert)
            {
                // Pulling a "+" out: the new vertex exists from here on and follows the mouse.
                _sketch = _sketch.Insert(_pressRef, place);
                SelectedVertex = _pressRef;
                _press = Press.Vertex;
            }
        }

        switch (_press)
        {
            case Press.Vertex:
                _sketch = _sketch.Move(_pressRef, place);
                return SketchOutcome.Preview;
            case Press.MoveShape when _dragLast is not null && MovedParts.Count > 0:
                foreach (var p in MovedParts)
                    _sketch = _sketch.MovePart(p, place.X - _dragLast.X, place.Y - _dragLast.Y);
                _dragLast = place.Copy();
                return SketchOutcome.Preview;
        }
        return SketchOutcome.None;
    }

    /// <summary>The parts a Move moves: the box's several, or the one selected.</summary>
    public IReadOnlyList<int> MovedParts => _parts.Count > 0 ? _parts : SelectedPart >= 0 ? new[] { SelectedPart } : System.Array.Empty<int>();

    public SketchOutcome LeftUp()
    {
        var press = _press;
        var dragging = _dragging;
        var box = Box;
        _press = Press.None;
        _dragging = false;
        _pressAt = null;
        _boxTo = null;

        switch (press)
        {
            case Press.Shape when dragging && _beforeDrag is not null && _sketch.PartCount > _beforeDrag.PartCount:
                // The maintainer's choice for drawn parts: back to Select with it selected.
                _tool = SketchTool.Select;
                SelectedPart = _sketch.PartCount - 1;
                SelectedVertex = null;
                return Committed();
            case Press.Shape:
                _sketch = _beforeDrag ?? _sketch;
                Message = _tool == SketchTool.Rectangle ? "Drag from one corner to the other to draw a rectangle." : "Drag from the centre outwards to draw a circle.";
                return dragging ? SketchOutcome.Preview : SketchOutcome.None;
            case Press.Box when dragging && box is not null:
                SelectInBox(box);
                return SketchOutcome.None;
            case Press.Box when _boxOnBody:
                // A click on the selected part's body: the part stays selected, its points do not.
                SelectedVertex = null;
                _points = new();
                return SketchOutcome.None;
            case Press.Box:
                // A click on empty space: nothing selected.
                SelectedPart = -1;
                SelectedVertex = null;
                ClearMany();
                return SketchOutcome.None;
            case Press.Insert when !dragging:
                // A click on "+": a point at the middle of that edge, selected.
                _sketch = _sketch.Insert(_pressRef, MidpointOf(_pressRef));
                SelectedVertex = _pressRef;
                return Committed();
            case Press.Vertex when dragging:
                return Committed();
            case Press.MoveShape:
                return SketchOutcome.None; // previewed; the move is kept with Done
        }
        return SketchOutcome.None;
    }

    /// <summary>
    /// A double-click: finishes a part being drawn; in Select, on an edge of the selected part,
    /// inserts a vertex there (the expert route; the "+" handles are the main one).
    /// </summary>
    public SketchOutcome DoubleClick(Coordinate at, double tolerance, Coordinate? place = null)
    {
        if (_drawPart >= 0)
        {
            // The double-click's second press already added a vertex on top of the previous one.
            DropTrailingDuplicates(tolerance);
            return Finish();
        }
        if (_tool != SketchTool.Select || _moving || SelectedPart < 0) return SketchOutcome.None;
        if (NearestVertexIn(SelectedPart, at, tolerance) is not null) return SketchOutcome.None;
        if (NearestEdgeIn(SelectedPart, at, tolerance) is not { } e) return SketchOutcome.None;
        _sketch = _sketch.Insert(e, place ?? at);
        SelectedVertex = e;
        return Committed();
    }

    /// <summary>A right-click (without a drag): finishes drawing, or deletes the vertex of the selected part under it.</summary>
    public SketchOutcome RightClick(Coordinate at, double tolerance)
    {
        Message = null;
        if (_drawPart >= 0) return Finish();
        if (_tool != SketchTool.Select || _moving || SelectedPart < 0) return SketchOutcome.None;
        if (NearestVertexIn(SelectedPart, at, tolerance) is not { } v) return SketchOutcome.None;
        SelectedVertex = v;
        return DeletePoint();
    }

    // ---- keys and actions -------------------------------------------------------------------

    /// <summary>Enter: finishes drawing, or keeps a move.</summary>
    public SketchOutcome Enter()
    {
        if (_drawPart >= 0) return Finish();
        if (_moving) return DoneMove();
        return SketchOutcome.None;
    }

    /// <summary>Escape: undoes the gesture in progress, or steps the selection back.</summary>
    public SketchOutcome Escape()
    {
        Message = null;
        if (_press == Press.Box)
        {
            // A box being dragged (or the mouse lost mid-box): drop it, keep the selection.
            _press = Press.None;
            _dragging = false;
            _boxTo = null;
            return SketchOutcome.None;
        }
        if (_dragging && _beforeDrag is not null)
        {
            _sketch = _beforeDrag;
            _press = Press.None;
            _dragging = false;
            Validate();
            return SketchOutcome.Preview;
        }
        if (_moving) return CancelMove();
        if (_drawPart >= 0)
        {
            _sketch = _beforeDrawing ?? _sketch;
            EndDrawing();
            if (_tool == SketchTool.Hole) _tool = SketchTool.Select;
            Validate();
            return SketchOutcome.Preview;
        }
        if (_points.Count > 0) _points = new();
        else if (SelectedVertex is not null) SelectedVertex = null;
        else
        {
            SelectedPart = -1;
            _parts = new();
        }
        return SketchOutcome.None;
    }

    /// <summary>Backspace: takes the last drawn point back while drawing, otherwise deletes like Delete.</summary>
    public SketchOutcome Backspace() => _drawPart >= 0 ? BackOnePoint() : Delete();

    /// <summary>Delete: removes the selected point, or the selected part when no point is selected.</summary>
    public SketchOutcome Delete()
    {
        if (_drawPart >= 0 || _moving) return SketchOutcome.None;
        if (_points.Count > 0) return DeletePoints();
        if (SelectedVertex is not null) return DeletePoint();
        if (_parts.Count > 0) return DeleteShapes();
        return DeleteShape();
    }

    /// <summary>Removes the points a box selected, unless that would leave a line or ring too short.</summary>
    public SketchOutcome DeletePoints()
    {
        Message = null;
        if (_points.Count == 0 || _drawPart >= 0 || _moving) return SketchOutcome.None;
        if (WhyNotDeletePoints() is { } reason)
        {
            Message = reason;
            return SketchOutcome.None;
        }
        // Highest index first in each ring, so the indices still to remove stay valid.
        foreach (var v in _points.OrderByDescending(v => v.Ring).ThenByDescending(v => v.Index))
            _sketch = _sketch.Remove(v);
        _points = new();
        SelectedVertex = null;
        Validate();
        return Committed();
    }

    private string? WhyNotDeletePoints()
    {
        if (_points.Count == 0 || SelectedPart < 0) return null;
        var part = _sketch.Part(SelectedPart);
        foreach (var ring in _points.GroupBy(v => v.Ring))
        {
            var left = part.VertexCount(ring.Key) - ring.Count();
            var reason = part.Kind switch
            {
                PartKind.Line when left < 2 => "A line needs at least 2 points. To remove it, delete the whole shape.",
                PartKind.Polygon when left < 3 && ring.Key == 0 => "A polygon needs at least 3 points. To remove it, delete the whole shape.",
                PartKind.Polygon when left < 3 => "A hole needs at least 3 points.",
                _ => null,
            };
            if (reason is not null) return reason;
        }
        return null;
    }

    /// <summary>Removes the shapes a box selected.</summary>
    public SketchOutcome DeleteShapes()
    {
        Message = null;
        if (_parts.Count == 0 || _drawPart >= 0 || _moving) return SketchOutcome.None;
        foreach (var p in _parts.OrderByDescending(p => p))
            _sketch = _sketch.RemovePart(p);
        _parts = new();
        SelectedPart = -1;
        SelectedVertex = null;
        return Committed();
    }

    /// <summary>Removes the selected point, unless that would leave the part less than its kind.</summary>
    public SketchOutcome DeletePoint()
    {
        Message = null;
        if (SelectedVertex is not { } v || !Exists(_sketch, v)) return SketchOutcome.None;
        if (WhyNotDeletePoint(v) is { } reason)
        {
            Message = reason;
            return SketchOutcome.None;
        }
        _sketch = _sketch.Remove(v); // a point part's only vertex takes the part along
        SelectedVertex = null;
        Validate();
        return Committed();
    }

    // Refused, not "helpfully" turned into deleting the whole part (the 2026-11-11 rule did that):
    // a beginner who wants one point gone should not see the whole shape vanish. The reason says
    // what to do instead.
    private string? WhyNotDeletePoint(VertexRef v)
    {
        var part = _sketch.Part(v.Part);
        var count = part.VertexCount(v.Ring);
        return part.Kind switch
        {
            PartKind.Line when count <= 2 => "A line needs at least 2 points. To remove it, delete the whole shape.",
            PartKind.Polygon when count <= 3 && v.Ring == 0 => "A polygon needs at least 3 points. To remove it, delete the whole shape.",
            PartKind.Polygon when count <= 3 => "A hole needs at least 3 points.",
            _ => null,
        };
    }

    /// <summary>Removes the selected part.</summary>
    public SketchOutcome DeleteShape()
    {
        Message = null;
        if (SelectedPart < 0 || _drawPart >= 0 || _moving) return SketchOutcome.None;
        _sketch = _sketch.RemovePart(SelectedPart);
        SelectedPart = -1;
        SelectedVertex = null;
        return Committed();
    }

    /// <summary>Enters move mode for the selected part.</summary>
    public SketchOutcome StartMove()
    {
        Message = null;
        if (MovedParts.Count == 0 || _drawPart >= 0 || _moving) return SketchOutcome.None;
        _moving = true;
        _beforeMove = _sketch;
        SelectedVertex = null;
        _points = new();
        return SketchOutcome.None;
    }

    /// <summary>Keeps the move and leaves move mode.</summary>
    public SketchOutcome DoneMove()
    {
        if (!_moving) return SketchOutcome.None;
        _moving = false;
        _beforeMove = null;
        _press = Press.None;
        _dragging = false;
        return Committed();
    }

    /// <summary>Puts the part back where it was and leaves move mode.</summary>
    public SketchOutcome CancelMove()
    {
        if (!_moving) return SketchOutcome.None;
        _sketch = _beforeMove ?? _sketch;
        _moving = false;
        _beforeMove = null;
        _press = Press.None;
        _dragging = false;
        return SketchOutcome.Preview;
    }

    /// <summary>Starts drawing a hole in the selected polygon.</summary>
    public SketchOutcome StartHole()
    {
        Message = null;
        if (SelectedPart < 0 || _sketch.Part(SelectedPart).Kind != PartKind.Polygon || _drawPart >= 0 || _moving) return SketchOutcome.None;
        _holeTarget = SelectedPart;
        _tool = SketchTool.Hole;
        SelectedVertex = null;
        return SketchOutcome.None;
    }

    /// <summary>Takes the last drawn point back (the Back button while drawing).</summary>
    public SketchOutcome BackOnePoint()
    {
        if (_drawPart < 0) return SketchOutcome.None;
        var count = _sketch.Part(_drawPart).VertexCount(_drawRing);
        if (count <= 1) return Escape();
        _sketch = _sketch.RemoveLast(_drawPart, _drawRing);
        return SketchOutcome.Preview;
    }

    /// <summary>Undoes the last committed edit. While drawing it takes the last point back.</summary>
    public SketchOutcome Undo()
    {
        Message = null;
        if (_drawPart >= 0) return BackOnePoint();
        if (_moving) return CancelMove();
        if (_undo.Count == 0) return SketchOutcome.None;
        _redo.Push(_committed);
        _sketch = _committed = _undo.Pop();
        SelectedVertex = null;
        ClearMany();
        Validate();
        return SketchOutcome.Commit;
    }

    /// <summary>Redoes the last undone edit.</summary>
    public SketchOutcome Redo()
    {
        Message = null;
        if (_drawPart >= 0 || _moving || _redo.Count == 0) return SketchOutcome.None;
        _undo.Push(_committed);
        _sketch = _committed = _redo.Pop();
        SelectedVertex = null;
        ClearMany();
        Validate();
        return SketchOutcome.Commit;
    }

    /// <summary>Runs an action of the floating bar.</summary>
    public SketchOutcome Run(SketchAction action) => action switch
    {
        SketchAction.Move => StartMove(),
        SketchAction.Hole => StartHole(),
        SketchAction.DeleteShape => DeleteShape(),
        SketchAction.DeletePoint => DeletePoint(),
        SketchAction.DeleteShapes => DeleteShapes(),
        SketchAction.DeletePoints => DeletePoints(),
        SketchAction.Done => DoneMove(),
        SketchAction.Cancel => _moving ? CancelMove() : Escape(),
        SketchAction.Finish => Enter(),
        SketchAction.BackOnePoint => BackOnePoint(),
        _ => SketchOutcome.None,
    };

    // ---- what the window shows --------------------------------------------------------------

    /// <summary>The actions to offer next to the selection now, in order.</summary>
    public IReadOnlyList<ActionState> Actions()
    {
        var list = new List<ActionState>();
        if (_drawPart >= 0)
        {
            var part = _sketch.Part(_drawPart);
            var count = part.VertexCount(_drawRing);
            var needed = part.Kind == PartKind.Line ? 2 : 3;
            list.Add(new(SketchAction.Finish, "Done", count >= needed, count >= needed ? null : $"Needs at least {needed} points."));
            list.Add(new(SketchAction.BackOnePoint, "Back one point", count > 1));
            list.Add(new(SketchAction.Cancel, "Cancel", true));
            return list;
        }
        if (_moving)
        {
            list.Add(new(SketchAction.Done, "Done", true));
            list.Add(new(SketchAction.Cancel, "Cancel", true));
            return list;
        }
        if (_tool != SketchTool.Select) return list;
        if (_parts.Count > 0)
        {
            list.Add(new(SketchAction.Move, "Move", true));
            list.Add(new(SketchAction.DeleteShapes, $"Delete {_parts.Count} shapes", true));
            return list;
        }
        if (SelectedPart < 0) return list;
        if (_points.Count > 0)
        {
            var reason = WhyNotDeletePoints();
            list.Add(new(SketchAction.DeletePoints, $"Delete {_points.Count} points", reason is null, reason));
            return list;
        }

        if (SelectedVertex is { } v && Exists(_sketch, v))
        {
            var reason = WhyNotDeletePoint(v);
            list.Add(new(SketchAction.DeletePoint, "Delete point", reason is null, reason));
            return list;
        }
        list.Add(new(SketchAction.Move, "Move", true));
        if (_sketch.Part(SelectedPart).Kind == PartKind.Polygon) list.Add(new(SketchAction.Hole, "Hole", true));
        list.Add(new(SketchAction.DeleteShape, "Delete", true));
        return list;
    }

    /// <summary>One line: what can be done now.</summary>
    public string HintText
    {
        get
        {
            if (Message is not null) return Message;
            if (_moving) return "Drag to move the whole shape · Enter or Done keeps it · Esc puts it back";
            if (_drawPart >= 0)
            {
                var part = _sketch.Part(_drawPart);
                var count = part.VertexCount(_drawRing);
                if (part.Kind == PartKind.Line)
                    return count < 2 ? "Click to add the next point · Esc cancels"
                                     : "Click to add points · double-click or Enter finishes · Backspace takes a point back · Esc cancels";
                return count < 3 ? "Click to add the next point · Esc cancels"
                                 : "Click the first point or press Enter to close · Backspace takes a point back · Esc cancels";
            }
            return _tool switch
            {
                SketchTool.Point => "Click to place a point · Esc or V to select",
                SketchTool.Line => "Click to start a line",
                SketchTool.Polygon => "Click to start a polygon",
                SketchTool.Rectangle => "Drag from one corner to the other · Esc cancels",
                SketchTool.Circle => "Drag from the centre outwards · Esc cancels",
                SketchTool.Hole => "Click inside the polygon to start the hole · Esc cancels",
                _ when _parts.Count > 0 => $"{_parts.Count} shapes selected · Delete removes them · Move moves them · Esc deselects",
                _ when _points.Count > 0 => $"{_points.Count} points selected · Delete removes them · Esc deselects",
                _ when SelectedVertex is not null => "Delete removes this point · drag to move it · Esc deselects",
                _ when SelectedPart >= 0 => "Drag a point to reshape · click or drag + to add a point · drag a box to select points",
                _ => "Click a shape to select it · drag a box to select several · right-drag or Space+drag pans · wheel zooms",
            };
        }
    }

    /// <summary>The "+" handles of the selected part: where each sits, and the vertex it inserts.</summary>
    public IEnumerable<(VertexRef Insert, Coordinate At)> Midpoints()
    {
        if (SelectedPart < 0 || _drawPart >= 0 || _moving || _tool != SketchTool.Select) yield break;
        foreach (var insert in EdgesOf(SelectedPart))
            yield return (insert, MidpointOf(insert));
    }

    // ---- geometry helpers -------------------------------------------------------------------

    // Each edge of a part, as the VertexRef a vertex inserted on it would get (its end vertex's index;
    // the closing edge of a polygon ring has index = vertex count).
    private IEnumerable<VertexRef> EdgesOf(int partIndex)
    {
        var part = _sketch.Part(partIndex);
        if (part.Kind == PartKind.Point) yield break;
        for (var r = 0; r < part.RingCount; r++)
        {
            var n = part.VertexCount(r);
            if (n < 2) continue;
            var edges = part.Kind == PartKind.Polygon && n >= 3 ? n : n - 1;
            for (var i = 1; i <= edges; i++) yield return new VertexRef(partIndex, r, i);
        }
    }

    private Coordinate MidpointOf(VertexRef insert)
    {
        var ring = _sketch.Part(insert.Part).Ring(insert.Ring);
        var a = ring[insert.Index - 1];
        var b = ring[insert.Index % ring.Length];
        return new Coordinate((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    private VertexRef? NearestVertexIn(int partIndex, Coordinate at, double tolerance)
    {
        VertexRef? best = null;
        var bestDistance = tolerance;
        var part = _sketch.Part(partIndex);
        for (var r = 0; r < part.RingCount; r++)
        {
            var ring = part.Ring(r);
            for (var i = 0; i < ring.Length; i++)
            {
                var d = ring[i].Distance(at);
                if (d <= bestDistance) { best = new VertexRef(partIndex, r, i); bestDistance = d; }
            }
        }
        return best;
    }

    private VertexRef? NearestMidpointIn(int partIndex, Coordinate at, double tolerance)
    {
        VertexRef? best = null;
        var bestDistance = tolerance;
        foreach (var e in EdgesOf(partIndex))
        {
            var d = MidpointOf(e).Distance(at);
            if (d <= bestDistance) { best = e; bestDistance = d; }
        }
        return best;
    }

    private VertexRef? NearestEdgeIn(int partIndex, Coordinate at, double tolerance)
    {
        VertexRef? best = null;
        var bestDistance = tolerance;
        foreach (var e in EdgesOf(partIndex))
        {
            var ring = _sketch.Part(e.Part).Ring(e.Ring);
            var d = new LineSegment(ring[e.Index - 1], ring[e.Index % ring.Length]).Distance(at);
            if (d <= bestDistance) { best = e; bestDistance = d; }
        }
        return best;
    }

    // ---- drawing ----------------------------------------------------------------------------

    private SketchOutcome DrawDown(Coordinate at, Coordinate place, double tolerance)
    {
        if (_drawPart < 0)
        {
            var start = Snapped(at, place, tolerance);
            if (_tool == SketchTool.Hole)
            {
                var polygon = _holeTarget >= 0 && _holeTarget < _sketch.PartCount ? _holeTarget : _sketch.PolygonAt(at);
                var inside = polygon >= 0 && _sketch.Part(polygon).ToGeometry(Defaults.Factory) is Polygon p
                             && p.Contains(Defaults.Factory.CreatePoint(at.Copy()));
                if (!inside)
                {
                    Message = "Click inside the polygon to start the hole.";
                    return SketchOutcome.None;
                }
                _beforeDrawing = _sketch;
                _sketch = _sketch.AddHole(polygon, start);
                _drawPart = polygon;
                _drawRing = _sketch.Part(polygon).RingCount - 1;
            }
            else
            {
                _beforeDrawing = _sketch;
                _sketch = _sketch.AddPart(_tool == SketchTool.Line ? PartKind.Line : PartKind.Polygon, start);
                _drawPart = _sketch.PartCount - 1;
                _drawRing = 0;
            }
            SelectedPart = _drawPart;
            return SketchOutcome.Preview;
        }

        // Clicking the first vertex of a ring with three or more closes it.
        var ring = _sketch.Part(_drawPart).Ring(_drawRing);
        if (_tool != SketchTool.Line && ring.Length >= 3 && ring[0].Distance(at) <= tolerance)
            return Finish();

        _sketch = _sketch.Append(_drawPart, _drawRing, Snapped(at, place, tolerance));
        return SketchOutcome.Preview;
    }

    private SketchOutcome Finish()
    {
        if (_drawPart < 0) return SketchOutcome.None;
        var part = _sketch.Part(_drawPart);
        var count = part.VertexCount(_drawRing);
        var needed = part.Kind == PartKind.Line ? 2 : 3;
        var backToSelect = _tool is SketchTool.Line or SketchTool.Polygon or SketchTool.Hole;
        if (count < needed)
        {
            Message = part.Kind == PartKind.Line
                ? "A line needs 2 points - discarded."
                : _drawRing > 0 ? "A hole needs 3 points - discarded." : "A polygon needs 3 points - discarded.";
            _sketch = _beforeDrawing ?? _sketch;
            EndDrawing();
            if (_tool == SketchTool.Hole) _tool = SketchTool.Select;
            Validate();
            return SketchOutcome.Preview;
        }
        if (_drawRing > 0 && WhyNotHole(part) is { } holeProblem)
        {
            // Kept drawing, not discarded: Backspace takes points back until it fits.
            Message = holeProblem;
            return SketchOutcome.None;
        }
        var finished = _drawPart;
        EndDrawing();
        // The maintainer's choice: back to Select with the new part selected, ready to adjust.
        if (backToSelect) _tool = SketchTool.Select;
        SelectedPart = finished;
        SelectedVertex = null;
        return Committed();
    }

    // A hole must lie inside its shell and must not cross another hole (2026-11-14; until then the
    // editor accepted it and only IsValid downstream said so). Checked when the hole is finished.
    private string? WhyNotHole(SketchPart part)
    {
        var f = Defaults.Factory;
        Polygon RingPolygon(int r)
        {
            var c = part.Ring(r);
            return f.CreatePolygon(c.Append(c[0]).ToArray());
        }
        try
        {
            var hole = RingPolygon(_drawRing);
            if (!RingPolygon(0).Contains(hole))
                return "The hole must lie inside its polygon. Backspace takes points back, Esc cancels it.";
            for (var r = 1; r < part.RingCount; r++)
                if (r != _drawRing && RingPolygon(r).Intersects(hole))
                    return "The hole overlaps another hole. Backspace takes points back, Esc cancels it.";
        }
        catch (System.Exception)
        {
            return "The hole crosses itself. Backspace takes points back, Esc cancels it.";
        }
        return null;
    }

    // ---- rectangle and circle (2026-11-14) ---------------------------------------------------

    // A drag, not clicks: press at one corner (or the centre), release at the other (or the rim).
    // The part is rebuilt from the sketch before the press on every move, previewed, and committed
    // on release, so one shape is one undo step. Then back to Select with it selected, as after
    // Line and Polygon. The corners go where Snap puts them; nothing snaps to existing vertices,
    // because a box or circle is defined by its extent, not by sharing a corner.

    /// <summary>
    /// How much narrower a unit of x is drawn than a unit of y (SketchView.XScale): on longitude and
    /// latitude a circle is drawn round on screen, which in degrees is an ellipse.
    /// </summary>
    public double XScale { get; set; } = 1;

    private static Coordinate[] Open(Polygon p)
    {
        var c = p.ExteriorRing.Coordinates;
        return c.Take(c.Length - 1).ToArray(); // the sketch keeps rings unclosed
    }

    private SketchPart? ShapePart(Coordinate from, Coordinate to)
    {
        if (_tool == SketchTool.Rectangle)
        {
            var box = GeometryNodes.RectangleJoinPoints(from, to);
            return box.IsEmpty ? null : GeometrySketch.PolygonPart(Open(box));
        }
        var k = XScale > 0 ? XScale : 1;
        var dx = (to.X - from.X) * k;
        var dy = to.Y - from.Y;
        var r = System.Math.Sqrt(dx * dx + dy * dy); // in y units, as it looks on screen
        if (r <= 0) return null;
        var circle = GeometryNodes.Ellipse(from, 2 * r / k, 2 * r);
        return circle.IsEmpty ? null : GeometrySketch.PolygonPart(Open(circle));
    }

    private void EndDrawing()
    {
        _drawPart = -1;
        _drawRing = -1;
        _holeTarget = -1;
        _beforeDrawing = null;
    }

    private void DropTrailingDuplicates(double tolerance)
    {
        while (true)
        {
            var ring = _sketch.Part(_drawPart).Ring(_drawRing);
            if (ring.Length < 2 || ring[^1].Distance(ring[^2]) > tolerance) return;
            _sketch = _sketch.RemoveLast(_drawPart, _drawRing);
        }
    }

    // Lands a new vertex on an existing one near the mouse, so parts can share corners; otherwise
    // at the placed position (the grid point when Snap is on).
    private Coordinate Snapped(Coordinate at, Coordinate place, double tolerance)
        => _sketch.NearestVertex(at, tolerance) is { } v ? _sketch[v] : place.Copy();
}
