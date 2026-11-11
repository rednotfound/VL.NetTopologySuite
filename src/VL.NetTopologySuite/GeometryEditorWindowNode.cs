using System;
using System.Collections.Generic;
using VL.Core;
using VL.Core.Import;
using VL.Skia;

namespace VL.NetTopologySuite;

// The content of the "Geometry Editor" window that VL.NetTopologySuite.HDE.vl opens (an editor
// extension: Command -> WindowFactory -> SkiaWindow, the shape of VL.HDE's Template.HDE.vl and of
// VL.Kairos's keyframe editor). Hidden: only the HDE document uses it.
//
// Which editor it shows: the editor's selection is HDE.API's CurrentSelection, a Spread<Object> of
// live elements (VL.Lang's ILiveElement, an editor assembly this runtime package does not
// reference). Each has an Element (VL.Model.Element) with a UniqueId, and a GeometryEditor knows its
// own UniqueId from NodeContext.Path - so the match is made by reading those two properties by
// name. Measured on 2026-11-11 by reflection over VL.Lang.dll; if a later vvvv renames them, the
// window falls back to the editor shown last, then to the one made last, and still works.

/// <summary>The canvas of the Geometry Editor window. Used by the editor extension.</summary>
[ProcessNode(Name = "GeometryEditorWindow", Category = "NetTopologySuite.Viewer")]
[Smell(SymbolSmell.Hidden)]
public sealed class GeometryEditorWindowNode
{
    private readonly GeometryEditorLayer _layer = new();

    /// <summary>
    /// Shows the selected GeometryEditor, or the last one shown.
    /// </summary>
    /// <param name="selection">The editor's current selection.</param>
    /// <param name="title">A title for the window.</param>
    /// <returns>The layer to draw in the window.</returns>
    public ILayer Update(IEnumerable<object>? selection, out string title)
    {
        var target = Selected(selection);
        if (target is null && _layer.Target is { IsAlive: true } shown) target = shown;
        target ??= GeometryEditorNode.Latest;
        _layer.Target = target;
        title = target is null ? "Geometry Editor" : "Geometry Editor · " + target.Sketch.ToWKT() switch
        {
            var w when w.Length > 40 => w.Substring(0, 37) + "...",
            var w => w,
        };
        return _layer;
    }

    private static GeometryEditorNode? Selected(IEnumerable<object>? selection)
    {
        if (selection is null) return null;
        foreach (var item in selection)
        {
            if (item is null) continue;
            try
            {
                var element = item.GetType().GetProperty("Element")?.GetValue(item);
                if (element?.GetType().GetProperty("UniqueId")?.GetValue(element) is UniqueId id
                    && GeometryEditorNode.Find(id) is { } editor)
                    return editor;
            }
            catch (Exception)
            {
                // A selection item of a shape we do not know: not an editor.
            }
        }
        return null;
    }
}
