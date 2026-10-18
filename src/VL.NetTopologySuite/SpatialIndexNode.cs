using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Category NetTopologySuite.Index; the index is NetTopologySuite's STRtree.
//
// This is the package's first process node, and it is one for the reason RULES.md rule 8 gives: an
// index is a resource with a lifetime. Building one over 100,000 geometries costs real time; a
// static method would do it sixty times a second from the moment the document opened. So the tree
// is held, and rebuilt only when the input actually changes.
//
// What counts as a change - the contract. VL's only "has this changed" signal is object identity:
// Spread<T> has no Equals, and a static node upstream hands out a fresh spread every frame even
// when every geometry inside it is the same object. So this node does not compare the collection;
// it compares its ELEMENTS, BY REFERENCE - the count, and whether each position still holds the
// same object. A hundred thousand pointer comparisons cost tens of microseconds; the structural
// comparison FeatureLayer uses in VL.Mapsui (EqualsExact, walking every coordinate) would cost more
// than the index saves, and is deliberately not done here.
//
// Mutating a geometry already in the index is unsupported and undetectable. It is also unreachable
// through this package: every creation node copies its coordinates on the way in (see
// MutationTests), so the only way to move an indexed geometry is a raw NTS handle obtained
// elsewhere. To change geometry, supply a new collection; the node rebuilds once.
//
// Built explicitly. NTS will build an STRtree lazily on the first query. This node calls Build()
// itself after the last insert, so Indexes Built means exactly what it says and the cost lands
// where the patch can see it, not inside the first Query.
//
// Watch Indexes Built. It should reach 1 and stay. If it climbs every frame, the geometries
// upstream are being re-created every frame - build them once, in a Create fragment or a Cache
// region - and the index is doing nothing.
//
// Nulls and empty geometries in the collection are skipped and not counted; an empty geometry has
// no envelope to index. A null collection yields no index at all, so a Query can be wired up before
// there is anything to search.

/// <summary>
/// Builds a spatial index over geometries, once, and holds it for Query.
/// </summary>
/// <remarks>
/// It rebuilds when a different set of geometries arrives.
/// If Indexes Built climbs every frame, the geometries are made every frame: build them once, for example in a Cache region.
/// </remarks>
[ProcessNode(Name = "SpatialIndex", Category = "NetTopologySuite.Index")]
public class SpatialIndexNode
{
    STRtree<Geometry>? _tree;
    Geometry?[] _indexed = [];
    bool _hasInput;
    int _count;

    /// <summary>Indexes built by this node. It should settle at 1 and stay there.</summary>
    internal int IndexesBuilt { get; private set; }

    /// <summary>
    /// Builds the index when the geometries change.
    /// </summary>
    /// <returns>The index, for Query.</returns>
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
