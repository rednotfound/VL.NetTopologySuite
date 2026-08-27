using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using VL.Core.Import;

namespace VL.NTS;

/// <summary>
/// A spatial index over a collection of geometries. Built once, retained, queried many times.
/// Category <c>NTS.Index</c>. Uses NetTopologySuite's <c>STRtree</c>.
/// </summary>
/// <remarks>
/// <para><b>This is the package's first process node, and it is one for the reason RULES.md rule 8
/// gives:</b> an index is a resource with a lifetime. Building one over 100,000 geometries costs
/// real time; a static method would do it sixty times a second from the moment the document
/// opened. So the tree is held, and rebuilt only when the input actually changes.</para>
///
/// <para><b>What counts as a change — the contract.</b> VL's only "has this changed" signal is
/// object identity: <c>Spread&lt;T&gt;</c> has no <c>Equals</c>, and a static node upstream hands
/// out a fresh spread every frame even when every geometry inside it is the same object. So this
/// node does not compare the collection; it compares its <b>elements, by reference</b> — the count,
/// and whether each position still holds the same object. A hundred thousand pointer comparisons
/// cost tens of microseconds; the structural comparison <c>FeatureLayer</c> uses in VL.Mapsui
/// (<c>EqualsExact</c>, walking every coordinate) would cost more than the index saves, and is
/// deliberately not done here.</para>
///
/// <para><b>Mutating a geometry already in the index is unsupported and undetectable.</b> It is
/// also unreachable through this package: every creation node copies its coordinates on the way in
/// (see <c>MutationTests</c>), so the only way to move an indexed geometry is a raw NTS handle
/// obtained elsewhere. To change geometry, supply a new collection; the node rebuilds once.</para>
///
/// <para><b>Built explicitly.</b> NTS will build an STRtree lazily on the first query. This node
/// calls <c>Build()</c> itself after the last insert, so <c>Indexes Built</c> means exactly what it
/// says and the cost lands where the patch can see it, not inside the first <c>Query</c>.</para>
///
/// <para><b>Watch Indexes Built.</b> It should reach 1 and stay. If it climbs every frame, the
/// geometries upstream are being re-created every frame — build them once, in a Create fragment or
/// a Cache region — and the index is doing nothing for you.</para>
///
/// <para>Nulls and empty geometries in the collection are skipped and not counted; an empty
/// geometry has no envelope to index. A null collection yields no index at all, so a Query can be
/// wired up before there is anything to search.</para>
/// </remarks>
[ProcessNode(Name = "SpatialIndex", Category = "NTS.Index")]
public class SpatialIndexNode
{
    STRtree<Geometry>? _tree;
    Geometry?[] _indexed = [];
    bool _hasInput;
    int _count;

    /// <summary>Indexes built by this node. It should settle at 1 and stay there.</summary>
    internal int IndexesBuilt { get; private set; }

    /// <summary>
    /// The index over the given geometries — the raw NTS <c>STRtree</c>, ready for <c>Query</c>.
    /// </summary>
    /// <param name="geometries">The geometries to index. Rebuilt when the set of references changes.</param>
    /// <param name="count">How many geometries are in the index (nulls and empties excluded).</param>
    /// <param name="indexesBuilt">How many times this node has built an index. Should stay at 1.</param>
    public STRtree<Geometry>? Update(
        IEnumerable<Geometry?>? geometries,
        out int count,
        out int indexesBuilt)
    {
        if (geometries is null)
        {
            _tree = null;
            _indexed = [];
            _hasInput = false;
            count = 0;
            indexesBuilt = IndexesBuilt;
            return null;
        }

        var incoming = InputSets.ToArray(geometries);

        if (!_hasInput || !InputSets.SameReferences(incoming, _indexed))
        {
            _tree = Build(incoming, out var indexed);
            _indexed = incoming;
            _hasInput = true;
            IndexesBuilt++;
            _count = indexed;
        }

        count = _count;
        indexesBuilt = IndexesBuilt;
        return _tree;
    }

    static STRtree<Geometry> Build(Geometry?[] geometries, out int indexed)
    {
        var tree = new STRtree<Geometry>();
        indexed = 0;
        foreach (var geometry in geometries)
        {
            if (geometry is null || geometry.IsEmpty) continue;
            tree.Insert(geometry.EnvelopeInternal, geometry);
            indexed++;
        }
        // Explicit, so the work happens here and not inside the first Query. NTS permits this
        // exactly once per tree, which is fine: a rebuild is a new tree.
        tree.Build();
        return tree;
    }

    // Change detection lives in InputSets — one rule, shared with BuildNetwork, same words.
}

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
