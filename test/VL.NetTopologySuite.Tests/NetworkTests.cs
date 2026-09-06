using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using VL.NTS;
using Xunit;

namespace VL.NTS.Tests;

/// <summary>
/// The network built for VL.Overworld's Tutorial 13 — "close does not mean reachable" — and
/// promoted to <c>NTS.Network</c> on 2026-08-28 (docs/NETWORK-SCOPE-PROPOSAL.md). Scope, in one
/// sentence and asserted here: an undirected spatial network built from EXPLICITLY connected
/// LineStrings in a local Cartesian space, with geometric length as cost and Dijkstra as the path
/// algorithm; queries snap to the nearest node within an optional maximum distance, and the snap
/// is always reported. Written before the code; the snap-bound tests before the pin.
/// </summary>
public class NetworkTests
{
    static Coordinate C(double x, double y) => GeometryNodes.Coordinate(x, y);
    static Point P(double x, double y) => GeometryNodes.Point(C(x, y));
    static LineString L(params (double x, double y)[] pts)
        => GeometryNodes.LineString(pts.Select(p => C(p.x, p.y)).ToArray());

    static Network Build(params Geometry?[] lines)
        => new NetworkNode().Update(lines, out _, out _, out _)!;

    // ---- topology: what counts as connected ------------------------------------------------

    [Fact]
    public void Edges_sharing_an_endpoint_are_connected()
    {
        var node = new NetworkNode();

        node.Update(new Geometry?[] { L((0, 0), (10, 0)), L((10, 0), (10, 10)) }, out var nodes, out var edges, out var built);

        Assert.Equal(3, nodes);
        Assert.Equal(2, edges);
        Assert.Equal(1, built);
    }

    [Fact]
    public void Lines_that_cross_without_a_shared_vertex_are_NOT_connected()
    {
        // The bridge over the river, the overpass over the road: crossing in XY says nothing.
        var net = Build(L((0, 5), (10, 5)), L((5, 0), (5, 10)));

        var path = NetworkNodes.ShortestPath(net, P(0, 5), P(5, 10), out _, out var found, out _, out _);

        Assert.False(found);
        Assert.True(path.IsEmpty);
    }

    [Fact]
    public void An_interior_vertex_is_shape_not_a_junction()
    {
        // A —— x —— B, and another edge ending exactly at x. Only endpoints are nodes, so x is not
        // a junction and the second edge dangles. Split the line first if you mean a junction.
        var node = new NetworkNode();
        var net = node.Update(new Geometry?[] { L((0, 0), (5, 0), (10, 0)), L((5, 0), (5, 10)) }, out var nodes, out _, out _);

        var path = NetworkNodes.ShortestPath(net, P(0, 0), P(5, 10), out _, out var found, out _, out _);

        Assert.Equal(4, nodes);                                     // (0,0) (10,0) (5,0) (5,10) — (5,0) only via edge 2
        Assert.False(found);
        Assert.True(path.IsEmpty);
    }

    [Fact]
    public void Endpoints_must_match_exactly_no_tolerance()
    {
        var net = Build(L((0, 0), (10, 0)), L((10, 0.000001), (10, 10)));

        NetworkNodes.ShortestPath(net, P(0, 0), P(10, 10), out _, out var found, out _, out _);

        Assert.False(found);
    }

    [Fact]
    public void Nulls_empties_and_non_lines_are_skipped_and_a_MultiLineString_contributes_each_part()
    {
        var multi = GeometryNodes.MultiLineString(new[] { L((0, 0), (1, 0)), L((1, 0), (2, 0)) });
        var polygon = GeometryNodes.Polygon(GeometryNodes.LinearRing(new[] { C(0, 0), C(1, 0), C(1, 1) }));

        new NetworkNode().Update(new Geometry?[] { multi, null, GeometryNodes.Point(null), polygon }, out var nodes, out var edges, out _);

        Assert.Equal(2, edges);
        Assert.Equal(3, nodes);
    }

    // ---- routing ------------------------------------------------------------------------------

    [Fact]
    public void Dijkstra_takes_two_short_edges_over_one_long_one()
    {
        // A—B direct is a 30-unit detour; A—C—B is 10 + 10.
        var net = Build(
            L((0, 0), (5, 8), (10, 0)),            // long: about 18.9
            L((0, 0), (5, 0)),                     // 5
            L((5, 0), (10, 0)));                   // 5

        var path = NetworkNodes.ShortestPath(net, P(0, 0), P(10, 0), out var length, out var found, out _, out _);

        Assert.True(found);
        Assert.Equal(10, length, 9);
        Assert.Equal(3, path.NumPoints);                            // (0,0) (5,0) (10,0)
    }

    [Fact]
    public void Undirected_the_route_back_is_the_route_there_reversed()
    {
        var net = Build(L((0, 0), (5, 0)), L((5, 0), (5, 5)));

        var there = NetworkNodes.ShortestPath(net, P(0, 0), P(5, 5), out var l1, out _, out _, out _);
        var back = NetworkNodes.ShortestPath(net, P(5, 5), P(0, 0), out var l2, out _, out _, out _);

        Assert.Equal(l1, l2, 9);
        Assert.Equal(there.Coordinates.Reverse().ToArray(), back.Coordinates);
    }

    [Fact]
    public void The_path_keeps_the_original_edge_geometry()
    {
        // A curved edge stays curved in the answer. The graph decides WHICH edges; the geometry
        // decides what the path looks like.
        var curve = L((0, 0), (2, 3), (4, -3), (6, 0));
        var net = Build(curve, L((6, 0), (10, 0)));

        var path = NetworkNodes.ShortestPath(net, P(0, 0), P(10, 0), out var length, out _, out _, out _);

        Assert.Equal(5, path.NumPoints);                            // 4 from the curve + 1 new endpoint
        Assert.Equal(curve.Length + 4, length, 9);
        Assert.Equal(path.Length, length, 9);
    }

    [Fact]
    public void An_edge_walked_backwards_is_reversed_so_the_path_is_continuous()
    {
        // Edge typed B -> A, route A -> B: coordinates must come out A -> B, not B -> A.
        var net = Build(L((10, 0), (5, 2), (0, 0)));

        var path = NetworkNodes.ShortestPath(net, P(0, 0), P(10, 0), out _, out _, out _, out _);

        Assert.Equal(new[] { C(0, 0), C(5, 2), C(10, 0) }, path.Coordinates);
    }

    [Fact]
    public void Between_parallel_edges_the_shorter_wins()
    {
        var net = Build(L((0, 0), (10, 0)), L((0, 0), (5, 5), (10, 0)));

        NetworkNodes.ShortestPath(net, P(0, 0), P(10, 0), out var length, out _, out _, out _);

        Assert.Equal(10, length, 9);
    }

    [Fact]
    public void Same_node_for_from_and_to_is_found_with_an_empty_path_of_length_zero()
    {
        var net = Build(L((0, 0), (10, 0)));

        var path = NetworkNodes.ShortestPath(net, P(0, 0), P(0, 0), out var length, out var found, out _, out _);

        Assert.True(found);
        Assert.True(path.IsEmpty);
        Assert.Equal(0, length);
    }

    // ---- snapping: visible, never silent ----------------------------------------------------

    [Fact]
    public void From_and_to_snap_to_the_nearest_node_and_say_how_far()
    {
        var net = Build(L((0, 0), (10, 0)));

        var path = NetworkNodes.ShortestPath(net, P(1, 1), P(10, 3), out _, out var found, out var fromSnap, out var toSnap);

        Assert.True(found);
        Assert.Equal(System.Math.Sqrt(2), fromSnap, 9);
        Assert.Equal(3, toSnap, 9);
        Assert.Equal(C(0, 0), path.Coordinates.First());            // the path starts at the NODE, not the click
    }

    [Fact]
    public void A_point_on_a_node_has_snap_distance_zero()
    {
        var net = Build(L((0, 0), (10, 0)));

        NetworkNodes.ShortestPath(net, P(0, 0), P(10, 0), out _, out _, out var fromSnap, out var toSnap);

        Assert.Equal(0, fromSnap);
        Assert.Equal(0, toSnap);
    }

    // ---- lifecycle: same contract as SpatialIndex -----------------------------------------

    [Fact]
    public void Null_input_builds_nothing_and_ShortestPath_tolerates_it()
    {
        var node = new NetworkNode();

        var net = node.Update(null, out var nodes, out var edges, out var built);
        var path = NetworkNodes.ShortestPath(net, P(0, 0), P(1, 1), out var length, out var found, out _, out _);

        Assert.Null(net);
        Assert.Equal(0, nodes + edges + built);
        Assert.False(found);
        Assert.True(path.IsEmpty);
        Assert.Equal(0, length);
        Assert.False(NetworkNodes.ShortestPath(Build(L((0, 0), (1, 0))), null, P(1, 0), out _, out var f2, out _, out _).IsEmpty && f2);
    }

    [Fact]
    public void Same_references_never_rebuild_and_a_new_wrapper_does_not_either()
    {
        var node = new NetworkNode();
        var lines = new List<Geometry?> { L((0, 0), (10, 0)), L((10, 0), (10, 10)) };

        var first = node.Update(lines, out _, out _, out _);
        for (var i = 0; i < 50; i++) node.Update(lines, out _, out _, out _);
        var again = node.Update(new List<Geometry?>(lines), out _, out _, out var built);

        Assert.Same(first, again);
        Assert.Equal(1, built);
    }

    [Fact]
    public void Closing_the_bridge_is_a_new_collection_so_the_network_rebuilds_once_and_the_route_is_gone()
    {
        // Two banks joined by one bridge. Remove the bridge: Networks Built goes to 2 and B is no
        // longer reachable from A. Absence of a route is an answer, not an error.
        var west = L((0, 0), (0, 10));
        var east = L((20, 0), (20, 10));
        var bridge = L((0, 10), (20, 10));
        var node = new NetworkNode();

        var open = node.Update(new Geometry?[] { west, east, bridge }, out _, out _, out var built1);
        NetworkNodes.ShortestPath(open, P(0, 0), P(20, 0), out var lengthOpen, out var foundOpen, out _, out _);

        var closed = node.Update(new Geometry?[] { west, east }, out _, out _, out var built2);
        var path = NetworkNodes.ShortestPath(closed, P(0, 0), P(20, 0), out var lengthClosed, out var foundClosed, out _, out _);

        Assert.True(foundOpen);
        Assert.Equal(40, lengthOpen, 9);                            // up 10, across 20, down 10 — for 20 as the crow flies
        Assert.Equal(2, built2 - built1 + 1);
        Assert.False(foundClosed);
        Assert.True(path.IsEmpty);
        Assert.Equal(0, lengthClosed);
    }

    [Fact]
    public void Mutating_a_line_in_place_is_NOT_detected_which_is_the_documented_limit()
    {
        var raw = new GeometryFactory().CreateLineString(new[] { new Coordinate(0, 0), new Coordinate(10, 0) });
        var node = new NetworkNode();
        node.Update(new Geometry?[] { raw }, out _, out _, out _);

        raw.Coordinates[1].X = 500;                                 // the line moves under the network
        node.Update(new Geometry?[] { raw }, out _, out _, out var built);

        Assert.Equal(1, built);                                     // and the network does not know
    }
    // ---- Max Snap Distance: bounds the query, never the connectivity ------------------------

    [Fact]
    public void A_from_snap_beyond_the_maximum_is_not_found_and_the_distance_is_still_reported()
    {
        var net = Build(L((0, 0), (10, 0)));

        var path = NetworkNodes.ShortestPath(net, P(0, 100), P(10, 0), out var length, out var found,
            out var fromSnap, out _, maxSnapDistance: 50);

        Assert.False(found);
        Assert.True(path.IsEmpty);
        Assert.Equal(0, length);
        Assert.Equal(100, fromSnap);                                // the refusal is measurable
    }

    [Fact]
    public void A_to_snap_beyond_the_maximum_is_not_found_either()
    {
        var net = Build(L((0, 0), (10, 0)));

        NetworkNodes.ShortestPath(net, P(0, 0), P(10, 100), out _, out var found, out _, out var toSnap,
            maxSnapDistance: 50);

        Assert.False(found);
        Assert.Equal(100, toSnap);
    }

    [Fact]
    public void A_snap_exactly_at_the_maximum_is_inside()
    {
        var net = Build(L((0, 0), (10, 0)));

        NetworkNodes.ShortestPath(net, P(0, 5), P(10, 0), out _, out var found, out var fromSnap, out _,
            maxSnapDistance: 5);

        Assert.True(found);
        Assert.Equal(5, fromSnap);
    }

    [Fact]
    public void The_default_is_unbounded_snapping()
    {
        // Every chapter built before the pin existed relies on this.
        var net = Build(L((0, 0), (10, 0)));

        NetworkNodes.ShortestPath(net, P(0, 1_000_000), P(10, 0), out _, out var found, out _, out _);

        Assert.True(found);
    }

}
