using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// One type per file, as vvvv's own libraries do (VL.CoreLib, VL.ImGui, VL.Skia; measured 2026-10-17):
// the process node lives in SpatialIndexNode.cs, the static Query holder here. Both are NetTopologySuite.Index.
//
// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.

/// <summary>
/// Asks a spatial index which geometries are near a search geometry.
/// </summary>
[Name("Index")]
public static class IndexNodes
{
    // A candidate is not a result. The index compares bounding boxes, because that is what makes it
    // fast; it never looks at the geometry itself. A diagonal line's box covers a whole square, so
    // the line comes back as a candidate for a corner of that square it passes nowhere near. Finish
    // the job with the exact predicate you meant - Intersects, Contains, Distance - on the
    // candidates. The index did not answer your question; it narrowed who gets asked.
    //
    // The search geometry's own envelope is used, so any geometry works as a query - a polygon, a
    // buffered point, a line. An Envelope is deliberately not a pin: this package does not expose
    // that type, and a patch already knows how to make a rectangle.
    //
    // No index, or no search geometry, or an empty one, gives no candidates rather than an error.

    /// <summary>
    /// Returns the geometries whose bounding boxes overlap the search geometry's bounding box.
    /// </summary>
    /// <remarks>
    /// These are candidates, not results: a diagonal line's box covers a whole square.
    /// Test the candidates with the exact question you meant, such as Intersects or Contains.
    /// </remarks>
    /// <param name="index">The index from SpatialIndex.</param>
    /// <param name="searchGeometry">Anything with an extent. Its bounding box is what is searched.</param>
    /// <param name="candidateCount">How many candidates came back, before any exact test.</param>
    public static IReadOnlyList<Geometry> Query(
        STRtree<Geometry>? index,
        Geometry? searchGeometry,
        out int candidateCount)
    {
        if (index is null || searchGeometry is null || searchGeometry.IsEmpty)
        {
            candidateCount = 0;
            return [];
        }

        var candidates = index.Query(searchGeometry.EnvelopeInternal);   // an IList; List<T> underneath
        candidateCount = candidates.Count;
        return candidates as IReadOnlyList<Geometry> ?? new List<Geometry>(candidates);
    }
}
