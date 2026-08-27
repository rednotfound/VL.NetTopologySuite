using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using VL.Core.Import;

// EXPERIMENTAL. Built for VL.Overworld's Tutorial 13 ("close does not mean reachable") and placed
// under NTS.Experimental on purpose: NetTopologySuite ships no shortest-path capability — its
// PlanarGraph is a framework for algorithm authors — so everything in this file is an algorithm of
// OURS, and an algorithm of ours is not "exposing NetTopologySuite". Whether it becomes a permanent
// public surface (NTS.Network, another package, or nothing) is decided AFTER the chapter exists and
// at least two more genuine consumers want the same abstraction. Until then: two nodes, one
// sentence of scope, and a long non-scope list in ROADMAP.md.
//
//   An undirected spatial network built from EXPLICITLY connected LineStrings in a local Cartesian
//   space, with geometric length as cost and Dijkstra as the path algorithm.
//
// If a change would stop that sentence being true, stop and discuss.
namespace VL.NTS.Experimental;

/// <summary>
/// A retained network: nodes at LineString endpoints, one undirected edge per LineString.
/// Experimental — see the note at the top of this file.
/// </summary>
/// <remarks>
/// Opaque on purpose. The node and edge records inside are an implementation detail while the
/// lesson is being validated, not public VL types.
/// </remarks>
public sealed class Network
{
    internal readonly List<Coordinate> Nodes = new();
    internal readonly Dictionary<(double X, double Y), int> NodeIndex = new();
    internal readonly List<Edge> Edges = new();
    internal readonly List<List<int>> Adjacency = new();

    internal readonly record struct Edge(int A, int B, LineString Geometry, double Length);

    internal int AddNode(Coordinate c)
    {
        var key = (c.X, c.Y);
        if (NodeIndex.TryGetValue(key, out var existing)) return existing;
        var index = Nodes.Count;
        Nodes.Add(c);
        NodeIndex.Add(key, index);
        Adjacency.Add(new List<int>());
        return index;
    }

    internal void AddEdge(LineString line)
    {
        // An edge's ENDPOINTS are nodes. Its interior vertices are shape, not junctions — a line
        // that merely passes through another line's endpoint does not connect to it. Split the
        // linework first if a junction is meant.
        var a = AddNode(line.GetCoordinateN(0));
        var b = AddNode(line.GetCoordinateN(line.NumPoints - 1));
        var index = Edges.Count;
        Edges.Add(new Edge(a, b, line, line.Length));
        Adjacency[a].Add(index);
        Adjacency[b].Add(index);
    }

    internal int Nearest(Coordinate c, out double distance)
    {
        var best = -1;
        distance = double.PositiveInfinity;
        for (var i = 0; i < Nodes.Count; i++)
        {
            var d = Nodes[i].Distance(c);
            if (d < distance) { distance = d; best = i; }
        }
        return best;
    }
}

/// <summary>
/// Builds a <see cref="Network"/> from LineStrings, once, and holds it. Experimental.
/// Category <c>NTS.Experimental.Network</c>.
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
[ProcessNode(Name = "BuildNetwork", Category = "NTS.Experimental.Network")]
public class BuildNetworkNode
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

/// <summary>Asking a network. Experimental. Category <c>NTS.Experimental.Network</c>.</summary>
[Name("Network")]
public static class NetworkNodes
{
    /// <summary>
    /// The shortest route through the network between two points, as the original edge geometry
    /// joined end to end. Uses Dijkstra over edge lengths.
    /// </summary>
    /// <remarks>
    /// <para><b>Snapping is visible, never silent.</b> From and To snap to the nearest network NODE
    /// and the two snap distances say how far that was. On a marked place they are 0; off the
    /// network they tell you the route did not start where you clicked.</para>
    /// <para><b>Found = false is an answer.</b> No route between the two nodes — the bridge is closed,
    /// the other bank is an island — gives an empty path, length 0 and <c>Found</c> false, never an
    /// exception and never a silent straight line.</para>
    /// <para>The path keeps each edge's original shape, reversed where the route walks an edge
    /// backwards, so a curved street stays curved. The graph decides which edges; the geometry
    /// decides what the path looks like. Length is the sum of the edge lengths, in the network's
    /// own coordinate units.</para>
    /// <para>From and To on the same node: found, empty path, length 0.</para>
    /// </remarks>
    /// <param name="network">From <c>BuildNetwork</c>.</param>
    /// <param name="from">Where the route starts. Snapped to the nearest node.</param>
    /// <param name="to">Where the route ends. Snapped to the nearest node.</param>
    /// <param name="length">Length of the route along the network, in coordinate units. 0 when not found.</param>
    /// <param name="found">Whether a route exists. False is a real result.</param>
    /// <param name="fromSnapDistance">How far From moved to reach its node.</param>
    /// <param name="toSnapDistance">How far To moved to reach its node.</param>
    public static LineString ShortestPath(
        Network? network,
        Point? from,
        Point? to,
        out double length,
        out bool found,
        out double fromSnapDistance,
        out double toSnapDistance)
    {
        length = 0;
        found = false;
        fromSnapDistance = toSnapDistance = 0;
        var empty = Defaults.Factory.CreateLineString();

        if (network is null || network.Nodes.Count == 0 || from is null || from.IsEmpty || to is null || to.IsEmpty)
            return empty;

        var start = network.Nearest(from.Coordinate, out fromSnapDistance);
        var goal = network.Nearest(to.Coordinate, out toSnapDistance);

        if (start == goal)
        {
            found = true;
            return empty;
        }

        // Dijkstra. The graph is small by contract (a hand-typed town), so a plain priority queue
        // and no heuristics; A* is on the non-scope list until a chapter needs it.
        var best = new double[network.Nodes.Count];
        Array.Fill(best, double.PositiveInfinity);
        var viaEdge = new int[network.Nodes.Count];
        Array.Fill(viaEdge, -1);
        var queue = new PriorityQueue<int, double>();
        best[start] = 0;
        queue.Enqueue(start, 0);

        while (queue.TryDequeue(out var node, out var cost))
        {
            if (cost > best[node]) continue;              // a stale entry
            if (node == goal) break;
            foreach (var edgeIndex in network.Adjacency[node])
            {
                var edge = network.Edges[edgeIndex];
                var next = edge.A == node ? edge.B : edge.A;
                var candidate = cost + edge.Length;
                if (candidate < best[next])
                {
                    best[next] = candidate;
                    viaEdge[next] = edgeIndex;
                    queue.Enqueue(next, candidate);
                }
            }
        }

        if (double.IsPositiveInfinity(best[goal]))
            return empty;

        // Walk back from the goal collecting edges, then lay their geometry out forwards — each
        // edge reversed if the route traverses it against the direction it was typed.
        var route = new List<Network.Edge>();
        var walk = new List<bool>();                      // true = traverse A -> B
        var at = goal;
        while (at != start)
        {
            var edge = network.Edges[viaEdge[at]];
            var forward = edge.B == at;                    // we arrived at B, so we walked A -> B
            route.Add(edge);
            walk.Add(forward);
            at = forward ? edge.A : edge.B;
        }
        route.Reverse();
        walk.Reverse();

        var coordinates = new List<Coordinate>();
        for (var i = 0; i < route.Count; i++)
        {
            var coords = route[i].Geometry.Coordinates;
            var n = coords.Length;
            for (var k = 0; k < n; k++)
            {
                var c = walk[i] ? coords[k] : coords[n - 1 - k];
                if (coordinates.Count > 0 && coordinates[^1].Equals2D(c)) continue;   // the shared junction
                coordinates.Add(c.Copy());
            }
        }

        length = best[goal];
        found = true;
        return Defaults.Factory.CreateLineString(coordinates.ToArray());
    }
}
