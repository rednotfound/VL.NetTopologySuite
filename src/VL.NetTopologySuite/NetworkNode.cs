using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using VL.Core.Import;

// The scope sentence this package promises for networks is in Network.cs, above the type that
// embodies it. One type per file, as vvvv's own libraries do (measured 2026-10-17).
namespace VL.NetTopologySuite;

//
// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Category NetTopologySuite.Network. A process node: the network is built once and held.
//
// Connectivity is explicit and exact. Two LineStrings are connected when they share an endpoint
// with identical coordinates. Not "close": no tolerance. Not "crossing": a line over another line in
// XY is a bridge or an overpass until the data says otherwise, so crossings are not junctions. Not
// "passing through": only a LineString's first and last coordinates are nodes; its interior
// vertices are shape. If a junction is meant where lines cross, node the linework first (Union does
// it - and that step is a claim about the world, not a geometric fact).
//
// Rebuilt only when the set of LineString references changes - same contract as SpatialIndex, same
// words: count, or any position holding a different object. Mutating a line in place is unsupported
// and undetectable. Closing a bridge is removing its line from the collection: a new collection, one
// rebuild, Networks Built ticks up. That is the honest shape of "the topology changed".
//
// LineStrings become edges; each part of a MultiLineString becomes an edge; nulls, empties and
// anything else are skipped. Edge Count tells how many survived.

/// <summary>
/// Builds a network from lines, once, and holds it.
/// </summary>
/// <remarks>
/// Lines connect only where their endpoints are exactly equal; lines that cross are not connected.
/// It rebuilds when a different set of lines arrives, so build the lines once rather than every frame.
/// </remarks>
[ProcessNode(Name = "Network", Category = "NetTopologySuite.Network")]
public class NetworkNode
{
    Network? _network;
    Geometry?[] _lines = [];
    bool _hasInput;
    int _nodeCount, _edgeCount;

    internal int NetworksBuilt { get; private set; }

    /// <summary>
    /// Builds the network when the lines change.
    /// </summary>
    /// <param name="lineStrings">The lines. Endpoints that coincide exactly are one node.</param>
    /// <param name="nodeCount">Distinct endpoints.</param>
    /// <param name="edgeCount">Lines (and MultiLineString parts) that became edges.</param>
    /// <param name="networksBuilt">How many times this node has built a network. Should stay at 1 until the lines change.</param>
    /// <returns>The network, for ShortestPath.</returns>
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
