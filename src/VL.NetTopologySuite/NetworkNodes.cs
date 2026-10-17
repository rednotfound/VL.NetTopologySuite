using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using VL.Core.Import;

// The scope sentence this package promises for networks is in Network.cs, above the type that
// embodies it. One type per file, as vvvv's own libraries do (measured 2026-10-17).
namespace VL.NTS;

/// <summary>Asking a network. Category <c>NTS.Network</c>.</summary>
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
    /// <para><b>Max Snap Distance bounds the query, not the connectivity.</b> If either snap distance
    /// exceeds it, the answer is not found — empty path, length 0 — and the snap distances are still
    /// reported, because a refusal you cannot measure would break the rule above. The default is
    /// infinity: unbounded snapping, the behaviour every existing chapter was built against. Edges
    /// still connect only by exact shared endpoints; this is not tolerance snapping.</para>
    /// </remarks>
    /// <param name="network">From <c>Network</c>.</param>
    /// <param name="from">Where the route starts. Snapped to the nearest node.</param>
    /// <param name="to">Where the route ends. Snapped to the nearest node.</param>
    /// <param name="length">Length of the route along the network, in coordinate units. 0 when not found.</param>
    /// <param name="found">Whether a route exists. False is a real result.</param>
    /// <param name="fromSnapDistance">How far From moved to reach its node.</param>
    /// <param name="toSnapDistance">How far To moved to reach its node.</param>
    /// <param name="maxSnapDistance">Farthest either end may snap. Beyond it: not found, snap still reported.</param>
    public static LineString ShortestPath(
        Network? network,
        Point? from,
        Point? to,
        out double length,
        out bool found,
        out double fromSnapDistance,
        out double toSnapDistance,
        [Pin(Name = "Max Snap Distance")] double maxSnapDistance = double.PositiveInfinity)
    {
        length = 0;
        found = false;
        fromSnapDistance = toSnapDistance = 0;
        var empty = Defaults.Factory.CreateLineString();

        if (network is null || network.Nodes.Count == 0 || from is null || from.IsEmpty || to is null || to.IsEmpty)
            return empty;

        var start = network.Nearest(from.Coordinate, out fromSnapDistance);
        var goal = network.Nearest(to.Coordinate, out toSnapDistance);

        // The refusal is measurable: found stays false, the distances above stay reported.
        if (fromSnapDistance > maxSnapDistance || toSnapDistance > maxSnapDistance)
            return empty;

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
