using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using VL.Core;
using VL.Core.Import;
using VL.Lang.PublicAPI;
using VL.Model;

namespace VL.NetTopologySuite;

// A geometry drawn by hand in a window of the editor (2026-11-11), the maintainer's idea: nobody
// should have to type WKT. The pattern is VL.Kairos's (a node in the patch, an editor-extension
// window that edits the selected one) plus the one VL.Stride's RenderWindow uses to remember its
// bounds: the node writes its own input pin back into the document,
//
//     IDevSession.Current.CurrentSolution.SetPinValue(NodeContext.Path.Stack, "WKT", wkt)
//                                         .Confirm(SolutionUpdateKind.DontCompile)
//
// all public in VL.Core (VL.Lang.PublicAPI), measured by reflection on 2026-11-11. DontCompile is
// every flag except AffectCompilation, so the edit lands in the undo history; RenderWindow uses the
// same value. The data is therefore WKT text in the .vl: saved with the patch, readable, and typed
// by hand still works. In an exported app there is no session (Current is null); the node simply
// outputs the geometry its WKT pin says.
//
// The WKT pin must stay UNCONNECTED for the window to edit it: SetPinValue writes the pin's own
// value, and a link overrides that. (Not detectable from here.)
//
// While a vertex is being dragged the node shows the sketch at once (Preview) and writes back only
// on release (Commit), so a drag is one undo step and the document is not rewritten every frame.
// A value arriving on the pin that equals what this node last wrote is its own echo and is not
// re-read, so a polygon in progress written as POINT keeps its kind.
//
// Live instances are listed in a static registry keyed by the node's UniqueId, which is how the
// window (GeometryEditorWindowNode) finds the one selected in the patch editor.

/// <summary>
// The shortcut, Ctrl+Shift+W (W for WKT), was the third try on 2026-11-11: Alt+G opens Windows 11's
// AI assistant before vvvv sees it, and Ctrl+Shift+G is vvvv's own "group nodes to a process node".
// Checked against the Gray Book's reference/hde/keyboard-shortcuts.md (github vvvv/The-Gray-Book),
// which lists Ctrl+Shift+ O P S E N D L G X T K F C and not W.

/// A geometry you draw in the editor: select the node and press Ctrl+Shift+W.
/// </summary>
/// <remarks>
/// The drawing is kept as WKT on the WKT pin and saved with the patch. Leave that pin unconnected.
/// The window draws points, lines, polygons and holes; several shapes make a Multi geometry or a collection.
/// </remarks>
[ProcessNode(Name = "GeometryEditor", Category = "NetTopologySuite.Geometry")]
public sealed class GeometryEditorNode : IDisposable
{
    internal const string WktPinName = "WKT";

    private static readonly object Gate = new();
    private static readonly List<GeometryEditorNode> Live = new();

    private readonly NodeContext _context;
    private string? _seenWkt;
    private string? _written;
    private bool _seenAny;
    private GeometrySketch _sketch = GeometrySketch.Empty;
    private Geometry _geometry = GeometrySketch.Empty.ToGeometry();
    private bool _editable = true;

    /// <summary>Makes the editor node.</summary>
    public GeometryEditorNode(NodeContext nodeContext)
    {
        _context = nodeContext;
        Id = nodeContext.Path.Stack.IsEmpty ? default : nodeContext.Path.Stack.Peek();
        lock (Gate) Live.Add(this);
    }

    internal UniqueId Id { get; }

    /// <summary>
    /// Outputs the geometry drawn in the editor window.
    /// </summary>
    /// <param name="wkt">The drawing as WKT. The editor window writes it; you may type it too.</param>
    /// <param name="isEditable">False when the WKT holds something the window cannot edit, such as a polygon with holes.</param>
    /// <returns>The geometry.</returns>
    public Geometry Update([Pin(Name = WktPinName)] string? wkt, out bool isEditable)
    {
        lock (Gate)
        {
            if (!_seenAny || wkt != _seenWkt)
            {
                _seenAny = true;
                _seenWkt = wkt;
                if (_written is null || wkt != _written)
                    Read(wkt);
                _written = null;
            }
            isEditable = _editable;
            return _geometry;
        }
    }

    private void Read(string? wkt)
    {
        if (GeometrySketch.TryFromWKT(wkt, out var sketch))
        {
            _editable = true;
            _sketch = sketch;
            _geometry = sketch.ToGeometry();
        }
        else
        {
            _editable = false;
            _sketch = GeometrySketch.Empty;
            _geometry = IONodes.FromWKT(wkt, null, out _);
        }
    }

    internal bool IsEditable { get { lock (Gate) return _editable; } }

    internal GeometrySketch Sketch { get { lock (Gate) return _sketch; } }

    internal Geometry Geometry { get { lock (Gate) return _geometry; } }

    /// <summary>Shows a sketch at once without writing it into the document (while dragging).</summary>
    internal void Preview(GeometrySketch sketch)
    {
        lock (Gate)
        {
            if (!_editable) return;
            _sketch = sketch;
            _geometry = sketch.ToGeometry();
        }
    }

    /// <summary>Shows a sketch and writes its WKT into the node's pin in the document.</summary>
    internal void Commit(GeometrySketch sketch)
    {
        string wkt;
        lock (Gate)
        {
            if (!_editable) return;
            _sketch = sketch;
            _geometry = sketch.ToGeometry();
            wkt = sketch.ToWKT();
            if (wkt == _seenWkt) return;
            _written = wkt;
        }
        WriteBack(wkt);
    }

    private void WriteBack(string wkt)
    {
        var session = IDevSession.Current;
        if (session is null || _context.Path.Stack.IsEmpty) return;
        try
        {
            session.CurrentSolution
                .SetPinValue(_context.Path.Stack, WktPinName, wkt)
                .Confirm(SolutionUpdateKind.DontCompile);
        }
        catch (Exception e)
        {
            session.ReportException(e);
        }
    }

    /// <summary>The live editor whose node has this id, the last made if several (a patch used twice).</summary>
    internal static GeometryEditorNode? Find(UniqueId id)
    {
        lock (Gate) return Live.LastOrDefault(e => e.Id == id);
    }

    /// <summary>The live editor made last, or null.</summary>
    internal static GeometryEditorNode? Latest
    {
        get { lock (Gate) return Live.LastOrDefault(); }
    }

    internal bool IsAlive
    {
        get { lock (Gate) return Live.Contains(this); }
    }

    /// <summary>Removes the editor.</summary>
    public void Dispose()
    {
        lock (Gate) Live.Remove(this);
    }
}
