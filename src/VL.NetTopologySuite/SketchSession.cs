using NetTopologySuite.Geometries;

namespace VL.NetTopologySuite;

// The editing rules of GeometryEditor's window as a state machine, in geometry coordinates, with no
// Skia and no vvvv - so every gesture is a test (SketchSessionTests). GeometryEditorLayer turns
// pixels into coordinates, calls this, and passes the outcome to the node: Preview shows a sketch
// at once, Commit also writes it into the document (one undo step).
//
// The tools (2026-11-11, the maintainer: every geometry the package has must be drawable, with a
// small toolbar):
//
//   Select   drag a vertex; double-click an edge to insert one; drag a part's body to move it;
//            right-click a vertex to delete it; Delete removes the selected part
//   Point    every click adds a point part
//   Line     click, click, ... ; double-click, Enter or right-click finishes; Esc cancels;
//            Backspace takes the last vertex back
//   Polygon  the same, and clicking the first vertex again closes it
//   Hole     click inside a polygon, then draw the hole like a polygon
//
// A part being drawn is only previewed; it is committed when finished, so a whole line is one undo
// step and a half-drawn polygon never reaches the document. A finished part that is not yet its
// kind (a line of one vertex, a polygon or hole of two) is discarded with a hint.
//
// Deleting a vertex that would leave a ring too small to be its kind takes the ring along (a hole
// of two vertices, a polygon of two), or the part: otherwise the written type would change under
// the user (a two-vertex polygon is written as a LINESTRING).
//
// In the drawing tools a click near an existing vertex lands exactly on it, so parts can share
// corners. Grid snapping is the layer's (it knows the grid).

/// <summary>A tool of the geometry editor window.</summary>
internal enum SketchTool
{
    Select,
    Point,
    Line,
    Polygon,
    Hole,
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

/// <summary>The editing state of the geometry editor window.</summary>
internal sealed class SketchSession
{
    private GeometrySketch _sketch = GeometrySketch.Empty;
    private SketchTool _tool = SketchTool.Select;

    // A part or hole being drawn: which part, which ring, and the sketch before it began (Esc).
    private int _drawPart = -1;
    private int _drawRing = -1;
    private GeometrySketch? _beforeDrawing;

    // A drag in the Select tool: a vertex, or a whole part; and the sketch it started from.
    private VertexRef? _dragVertex;
    private int _dragPart = -1;
    private Coordinate? _dragLast;
    private GeometrySketch? _beforeDrag;

    public GeometrySketch Sketch => _sketch;

    public SketchTool Tool => _tool;

    /// <summary>The selected part, -1 when none.</summary>
    public int SelectedPart { get; private set; } = -1;

    /// <summary>A short message for the status line, or null.</summary>
    public string? Hint { get; private set; }

    /// <summary>True while a part is being drawn or something is being dragged.</summary>
    public bool IsBusy => _drawPart >= 0 || _dragVertex is not null || _dragPart >= 0;

    public bool IsDrawing => _drawPart >= 0;

    /// <summary>The ring being drawn, for the rubber band; null when not drawing.</summary>
    public (int Part, int Ring)? DrawingRing => _drawPart >= 0 ? (_drawPart, _drawRing) : null;

    /// <summary>The vertex being dragged, null when none.</summary>
    public VertexRef? DraggedVertex => _dragVertex;

    /// <summary>Takes the sketch the node holds now, unless a gesture is in progress.</summary>
    public void Sync(GeometrySketch sketch)
    {
        if (IsBusy || ReferenceEquals(sketch, _sketch)) return;
        _sketch = sketch;
        if (SelectedPart >= sketch.PartCount) SelectedPart = -1;
    }

    /// <summary>Changes the tool, finishing a part being drawn.</summary>
    public SketchOutcome SetTool(SketchTool tool)
    {
        var outcome = _drawPart >= 0 ? Finish() : SketchOutcome.None;
        _tool = tool;
        Hint = null;
        return outcome;
    }

    // ---- pointer ---------------------------------------------------------------------------

    // Two positions per press (2026-11-11, the maintainer: with Snap on nothing could be selected):
    // AT is where the mouse really is, and decides what is hit; PLACE is where a new or moved vertex
    // goes - the same point, or rounded to the grid when the window's Snap is on. Hit-testing with
    // the snapped point missed everything a few pixels away from a grid dot.

    /// <summary>A left press at <paramref name="at"/>; a vertex it creates goes to <paramref name="place"/> (default: at).</summary>
    public SketchOutcome LeftDown(Coordinate at, double tolerance, Coordinate? place = null)
    {
        Hint = null;
        place ??= at;
        switch (_tool)
        {
            case SketchTool.Select:
                return SelectDown(at, place, tolerance);

            case SketchTool.Point:
                _sketch = _sketch.AddPart(PartKind.Point, Snapped(at, place, tolerance));
                SelectedPart = _sketch.PartCount - 1;
                return SketchOutcome.Commit;

            case SketchTool.Line:
            case SketchTool.Polygon:
            case SketchTool.Hole:
                return DrawDown(at, place, tolerance);
        }
        return SketchOutcome.None;
    }

    public SketchOutcome Move(Coordinate at)
    {
        if (_dragVertex is { } v)
        {
            _sketch = _sketch.Move(v, at);
            return SketchOutcome.Preview;
        }
        if (_dragPart >= 0 && _dragLast is not null)
        {
            _sketch = _sketch.MovePart(_dragPart, at.X - _dragLast.X, at.Y - _dragLast.Y);
            _dragLast = at.Copy();
            return SketchOutcome.Preview;
        }
        return SketchOutcome.None;
    }

    public SketchOutcome LeftUp()
    {
        if (_dragVertex is null && _dragPart < 0) return SketchOutcome.None;
        var changed = !ReferenceEquals(_sketch, _beforeDrag);
        _dragVertex = null;
        _dragPart = -1;
        _dragLast = null;
        _beforeDrag = null;
        return changed ? SketchOutcome.Commit : SketchOutcome.None;
    }

    /// <summary>
    /// A double-click: finishes a part being drawn; in Select, on an edge, inserts a vertex there.
    /// </summary>
    public SketchOutcome DoubleClick(Coordinate at, double tolerance, Coordinate? place = null)
    {
        if (_drawPart >= 0)
        {
            // The double-click's second press already added a vertex on top of the previous one.
            DropTrailingDuplicates(tolerance);
            return Finish();
        }
        // Inserting is a double-click, not a click (2026-11-11, the maintainer: "in Select I can
        // still draw") - a single click on an edge selects, as everywhere else.
        if (_tool != SketchTool.Select || IsBusy) return SketchOutcome.None;
        if (_sketch.NearestVertex(at, tolerance) is not null) return SketchOutcome.None;
        if (_sketch.NearestEdge(at, tolerance) is not { } e) return SketchOutcome.None;
        _sketch = _sketch.Insert(e, place ?? at);
        SelectedPart = e.Part;
        return SketchOutcome.Commit;
    }

    /// <summary>A right-click (without a drag): finishes drawing, or in Select deletes the vertex under it.</summary>
    public SketchOutcome RightClick(Coordinate at, double tolerance)
    {
        if (_drawPart >= 0) return Finish();
        if (_tool != SketchTool.Select) return SketchOutcome.None;
        if (_sketch.NearestVertex(at, tolerance) is not { } v) return SketchOutcome.None;
        _sketch = RemoveVertexKeepingKind(_sketch, v);
        if (SelectedPart >= _sketch.PartCount) SelectedPart = -1;
        return SketchOutcome.Commit;
    }

    // ---- keys ------------------------------------------------------------------------------

    /// <summary>Enter: finishes drawing.</summary>
    public SketchOutcome Enter() => _drawPart >= 0 ? Finish() : SketchOutcome.None;

    /// <summary>Escape: drops the part being drawn, or the selection.</summary>
    public SketchOutcome Escape()
    {
        Hint = null;
        if (_drawPart >= 0)
        {
            _sketch = _beforeDrawing ?? _sketch;
            EndDrawing();
            return SketchOutcome.Preview;
        }
        SelectedPart = -1;
        return SketchOutcome.None;
    }

    /// <summary>Backspace: takes the last vertex of the part being drawn back.</summary>
    public SketchOutcome Backspace()
    {
        if (_drawPart >= 0)
        {
            var count = _sketch.Part(_drawPart).VertexCount(_drawRing);
            if (count <= 1) return Escape();
            _sketch = _sketch.RemoveLast(_drawPart, _drawRing);
            return SketchOutcome.Preview;
        }
        return Delete();
    }

    /// <summary>Delete: removes the selected part.</summary>
    public SketchOutcome Delete()
    {
        if (_drawPart >= 0 || SelectedPart < 0) return SketchOutcome.None;
        _sketch = _sketch.RemovePart(SelectedPart);
        SelectedPart = -1;
        return SketchOutcome.Commit;
    }

    // ---- the tools -------------------------------------------------------------------------

    // Select never adds geometry: a press picks a vertex to drag, or a shape to select and drag.
    private SketchOutcome SelectDown(Coordinate at, Coordinate place, double tolerance)
    {
        _beforeDrag = _sketch;
        if (_sketch.NearestVertex(at, tolerance) is { } v)
        {
            _dragVertex = v;
            SelectedPart = v.Part;
            return SketchOutcome.None;
        }
        var part = _sketch.PartAt(at, tolerance);
        SelectedPart = part;
        if (part >= 0)
        {
            _dragPart = part;
            // Deltas are taken between PLACED positions, so a shape dragged with Snap moves in grid steps.
            _dragLast = place.Copy();
        }
        else
        {
            _beforeDrag = null;
        }
        return SketchOutcome.None;
    }

    private SketchOutcome DrawDown(Coordinate at, Coordinate place, double tolerance)
    {
        if (_drawPart < 0)
        {
            var start = Snapped(at, place, tolerance);
            if (_tool == SketchTool.Hole)
            {
                var polygon = _sketch.PolygonAt(at);
                if (polygon < 0)
                {
                    Hint = "Click inside a polygon to start a hole.";
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
        if (count < needed)
        {
            Hint = part.Kind == PartKind.Line
                ? "A line needs 2 points - discarded."
                : _drawRing > 0 ? "A hole needs 3 points - discarded." : "A polygon needs 3 points - discarded.";
            _sketch = _beforeDrawing ?? _sketch;
            EndDrawing();
            if (SelectedPart >= _sketch.PartCount) SelectedPart = -1;
            return SketchOutcome.Preview;
        }
        var finished = _drawPart;
        EndDrawing();
        SelectedPart = finished;
        return SketchOutcome.Commit;
    }

    private void EndDrawing()
    {
        _drawPart = -1;
        _drawRing = -1;
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

    internal static GeometrySketch RemoveVertexKeepingKind(GeometrySketch sketch, VertexRef v)
    {
        var part = sketch.Part(v.Part);
        var count = part.VertexCount(v.Ring);
        var minimum = part.Kind switch { PartKind.Point => 1, PartKind.Line => 2, _ => 3 };
        // Removing would leave the ring below its kind: the ring (a hole) or the part goes instead.
        return count - 1 < minimum && count - 1 > 0
            ? sketch.RemoveRing(v.Part, v.Ring)
            : sketch.Remove(v);
    }
}
