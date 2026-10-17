using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using VL.Core.Import;

// The scope sentence this package promises for networks is in Network.cs, above the type that
// embodies it. One type per file, as vvvv's own libraries do (measured 2026-10-17).
namespace VL.NTS;

/// <summary>
/// Builds a <see cref="Network"/> from LineStrings, once, and holds it.
/// Category <c>NTS.Network</c>.
/// </summary>
/// <remarks>
/// <para><b>Connectivity is explicit and exact.</b> Two LineStrings are connected when they share an
/// endpoint with identical coordinates. Not "close": no tolerance. Not "crossing": a line over
/// another line in XY is a bridge or an overpass until the data says otherwise, so crossings are not
/// junctions. Not "passing through": only a LineString's first and last coordinates are nodes; its
/// interior vertices are shape. If a junction is meant where lines cross, node the linework first
/// (<c>Union</c> does it — and that step is a claim about the world, not a geometric fact).</para>
/// <para><b>Rebuilt only when the set of LineString references changes</b> — same contract as
/// <c>SpatialIndex</c>, same words: count, or any position holding a different object. Mutating a
/// line in place is unsupported and undetectable. Closing a bridge is removing its line from the
/// collection: a new collection, one rebuild, <c>Networks Built</c> ticks up. That is the honest
/// shape of "the topology changed".</para>
/// <para>LineStrings become edges; each part of a MultiLineString becomes an edge; nulls, empties
/// and anything else are skipped. <c>Edge Count</c> tells you how many survived.</para>
/// </remarks>
[ProcessNode(Name = "Network", Category = "NTS.Network")]
public class NetworkNode
{
    Network? _network;
    Geometry?[] _lines = [];
    bool _hasInput;
    int _nodeCount, _edgeCount;

    internal int NetworksBuilt { get; private set; }

    /// <param name="lineStrings">The linework. Endpoints that coincide exactly are one node.</param>
    /// <param name="nodeCount">Distinct endpoints.</param>
    /// <param name="edgeCount">LineStrings (and MultiLineString parts) that became edges.</param>
    /// <param name="networksBuilt">How many times this node has built a network. Should stay at 1 until the topology changes.</param>
    public Network? Update(
        IEnumerable<Geometry?>? lineStrings,
        out int nodeCount,
        out int edgeCount,
        out int networksBuilt)
    {
        if (lineStrings is null)
        {
            _network = null;
            _lines = [];
            _hasInput = false;
            nodeCount = edgeCount = 0;
            networksBuilt = NetworksBuilt;
            return null;
        }

        var incoming = InputSets.ToArray(lineStrings);
        if (!_hasInput || !InputSets.SameReferences(incoming, _lines))
        {
            _network = Build(incoming);
            _lines = incoming;
            _hasInput = true;
            _nodeCount = _network.Nodes.Count;
            _edgeCount = _network.Edges.Count;
            NetworksBuilt++;
        }

        nodeCount = _nodeCount;
        edgeCount = _edgeCount;
        networksBuilt = NetworksBuilt;
        return _network;
    }

    static Network Build(Geometry?[] lines)
    {
        var network = new Network();
        foreach (var geometry in lines)
        {
            switch (geometry)
            {
                case LineString line when !line.IsEmpty:
                    network.AddEdge(line);
                    break;
                case MultiLineString multi:
                    for (var i = 0; i < multi.NumGeometries; i++)
                        if (multi.GetGeometryN(i) is LineString part && !part.IsEmpty)
                            network.AddEdge(part);
                    break;
            }
        }
        return network;
    }
}
