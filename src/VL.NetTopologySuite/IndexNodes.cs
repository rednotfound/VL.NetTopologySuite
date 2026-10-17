using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using VL.Core.Import;

namespace VL.NTS;

// One type per file, as vvvv's own libraries do (VL.CoreLib, VL.ImGui, VL.Skia; measured 2026-10-17):
// the process node lives in SpatialIndexNode.cs, the static Query holder here. Both are NTS.Index.

/// <summary>
/// Asking a spatial index. Category <c>NTS.Index</c>. Uses NetTopologySuite.
/// </summary>
[Name("Index")]
public static class IndexNodes
{
    /// <summary>
    /// The geometries whose <b>bounds</b> intersect the bounds of the search geometry. Candidates,
    /// not results. Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para><b>A candidate is not a result.</b> The index compares bounding boxes, because that is
    /// what makes it fast; it never looks at the geometry itself. A diagonal line's box covers a
    /// whole square, so the line comes back as a candidate for a corner of that square it passes
    /// nowhere near. Finish the job with the exact predicate you meant — <c>Intersects</c>,
    /// <c>Contains</c>, <c>Distance</c> — on the candidates. The index did not answer your
    /// question; it narrowed who gets asked.</para>
    /// <para>The search geometry's own envelope is used, so any geometry works as a query — a
    /// polygon, a buffered point, a line. An <c>Envelope</c> is deliberately not a pin: this
    /// package does not expose that type, and a patch already knows how to make a rectangle.</para>
    /// <para>No index, or no search geometry, or an empty one, gives no candidates rather than an
    /// error.</para>
    /// </remarks>
    /// <param name="index">The index from <c>SpatialIndex</c>.</param>
    /// <param name="searchGeometry">Anything with an extent. Its bounding box is what is searched.</param>
    /// <param name="candidateCount">How many candidates came back — before any exact test.</param>
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
