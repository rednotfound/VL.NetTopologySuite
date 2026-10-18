using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using VL.Core.Import;

// Promoted from NetTopologySuite.Experimental.Network on 2026-08-28 by the Network Package Scope Proposal
// (docs/NETWORK-SCOPE-PROPOSAL.md), after the evidence the 2026-08-23 review asked for existed:
// the chapter (VL.Overworld Tutorial 13), the abstraction that emerged, and two further consumers
// wanting the same surface (Prompt Which door, Prompt Grow a town), all rung-4 verified. This is
// an algorithm of OURS over NTS types - NetTopologySuite ships no shortest-path capability - and
// ARCHITECTURE.md says so plainly; a second algorithm of ours wanting a home reopens the location
// question.
//
//   An undirected spatial network built from EXPLICITLY connected LineStrings in a local Cartesian
//   space, with geometric length as cost and Dijkstra as the path algorithm; queries snap to the
//   nearest node within an optional maximum distance, and the snap is always reported.
//
// If a change would stop that sentence being true, stop and discuss.
namespace VL.NetTopologySuite;

//
// The /// text below is what vvvv shows on hover - user-facing, short, one sentence per line. Opaque
// on purpose: the node and edge records inside are an implementation detail while the lesson is
// being validated, not public VL types.

/// <summary>
/// A network built by the Network node: nodes at line endpoints, one two-way edge per line.
/// </summary>
/// <remarks>
/// Connect it to ShortestPath.
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
