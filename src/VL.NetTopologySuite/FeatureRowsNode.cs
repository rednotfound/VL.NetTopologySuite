using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using VL.Core;
using VL.Core.Import;
using VL.Lib.Collections;

namespace VL.NetTopologySuite;

// What the HDE FeatureViewer shows of a feature's attributes (2026-11-15).
//
// The maintainer asked why a Spread of Geometry in an IOBox draws every item while a Spread of
// Feature shows only "Spread<Feature>" and "[89]". Measured in vvvv 8.0 with nine probes: the
// editor's collection viewer draws its rows ONLY when the whole view fits the IOBox, and otherwise
// swaps, silently, to its compact ObjectViewer (type name and count). The same two-feature spread
// drew in a box 540 px high and fell back in one 480 px high. A Geometry row is about 120 px, so
// the default three rows fit; a Feature row was the drawing PLUS about 22 px per attribute, and
// three OpenStreetMap buildings with twelve attributes each need about 1200 px.
//
// So inside a collection a feature is drawn short: the geometry, and beside it a few lines of
// text, "name: Tekko Building", then how many more attributes there are. Alone in an IOBox or a
// tooltip it is drawn as before, every attribute as a typed row under the geometry - the
// maintainer asked for every row there (2026-11-07) and that stands.
//
// Text lines and not the typed rows beside the geometry: tried first the same day, and in an
// IOBox 300 px wide the geometry (160 px) and the key column (70 px) left no room for a single
// value. A line of text clips; a row of cells does not.
//
// "Inside a collection" is ViewerContext.Depth: the editor's AdaptiveViewer hands a viewer its
// context one level down, and the collection viewer does the same again for each row. Seen in
// vvvv 8.0 with this very node: a viewer gets FirstNestedDepth - 1 for the IOBox's own value and
// FirstNestedDepth in a row of a collection.

/// <summary>
/// Chooses what a drawn feature shows of its attributes: every row, or a short summary inside a collection. Used by the editor's viewer.
/// </summary>
[ProcessNode(Name = "FeatureRows", Category = "NetTopologySuite.Viewer")]
[Smell(SymbolSmell.Hidden)]
public sealed class FeatureRowsNode
{
    internal const int MaxSummaryLines = 3;
    internal const int MaxLineLength = 40;
    internal const int FirstNestedDepth = 2;

    private ImmutableDictionary<string, object>? _attributes;
    private int _depth = -1;
    private Spread<KeyValuePair<string, object>> _rows = Spread<KeyValuePair<string, object>>.Empty;
    private string _summary = "";

    /// <summary>
    /// Gives the attribute rows to draw for a feature, or a summary of them.
    /// </summary>
    /// <param name="attributes">The feature's attributes.</param>
    /// <param name="depth">The viewer context's depth: greater inside a collection.</param>
    /// <param name="summary">A few lines of text for a feature inside a collection, otherwise nothing.</param>
    /// <returns>The rows to draw under the geometry: every attribute, or none inside a collection.</returns>
    [return: Pin(Name = "Rows")]
    public Spread<KeyValuePair<string, object>> Update(
        ImmutableDictionary<string, object>? attributes,
        int depth,
        out string summary)
    {
        // The editor calls this every frame for every drawn feature; a feature in a patch is
        // usually the same object from frame to frame.
        if (!ReferenceEquals(attributes, _attributes) || depth != _depth)
        {
            _attributes = attributes;
            _depth = depth;
            (_rows, _summary) = Choose(attributes, depth);
        }

        summary = _summary;
        return _rows;
    }

    internal static (Spread<KeyValuePair<string, object>> Rows, string Summary) Choose(
        ImmutableDictionary<string, object>? attributes, int depth)
    {
        if (attributes is null || attributes.Count == 0)
            return (Spread<KeyValuePair<string, object>>.Empty, "");

        // "name" first: it is what tells one feature from the next in a list. The rest keep the
        // dictionary's own order, as the viewer has always shown them.
        var ordered = attributes
            .OrderBy(pair => string.Equals(pair.Key, "name", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        if (depth < FirstNestedDepth)
            return (ordered.ToSpread(), "");

        var lines = ordered.Take(MaxSummaryLines).Select(Line).ToList();
        var hidden = ordered.Count - lines.Count;
        if (hidden > 0) lines.Add($"+ {hidden} more");
        return (Spread<KeyValuePair<string, object>>.Empty, string.Join("\n", lines));
    }

    private static string Line(KeyValuePair<string, object> pair)
    {
        var value = Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? "";
        // One attribute, one line: a value with a line break of its own would push the rest out.
        var line = $"{pair.Key}: {value}".Replace("\r", " ").Replace("\n", " ");
        return line.Length <= MaxLineLength ? line : line[..(MaxLineLength - 1)] + "…";
    }
}
