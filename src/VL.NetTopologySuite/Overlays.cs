using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Union;

namespace VL.NetTopologySuite;

/// <summary>
/// The overlays (Intersection, Union, Difference, SymmetricDifference, Union (Spread)) in a form that
/// never throws. Internal: none of this is a node.
/// </summary>
/// <remarks>
/// <para>Measured against NetTopologySuite 2.6.0 in the review of 2026-11-14, all reachable from a patch:</para>
/// <list type="bullet">
/// <item><description>A self-crossing polygon (a bowtie, a GeometryEditor stroke, a shape folded by
/// Scale) makes Intersection, Union and UnaryUnion throw <c>TopologyException: side location
/// conflict</c>. OverlayNGRobust throws the same. GeometryFixer first, then the overlay, gives the
/// answer.</description></item>
/// <item><description>A GeometryCollection of mixed parts (what Cons, then GeometryCollection makes,
/// as the help patches teach) makes Union, Difference and SymmetricDifference throw
/// <c>Operation does not support GeometryCollection arguments</c>. Intersection accepts it. UnaryUnion
/// accepts it, so Union goes through UnaryUnion, and Difference is taken part by part.</description></item>
/// </list>
/// <para>A node runs sixty times a second; an exception is a red node and no result every frame.
/// So: compute; on a topology failure repair both inputs and compute again; if that fails too, an
/// empty geometry - the same "no answer" FromWKT gives for text that does not parse.</para>
/// </remarks>
internal static class Overlays
{
    /// <summary>An empty geometry on the factory (and so SRID) of <paramref name="like"/>.</summary>
    internal static Geometry Nothing(Geometry? like = null)
    {
        var empty = (like?.Factory ?? Defaults.Factory).CreateGeometryCollection();
        if (like is not null) empty.SRID = like.SRID;
        return empty;
    }

    /// <summary>A GeometryCollection proper - not a MultiPoint/MultiLineString/MultiPolygon, which NTS overlays accept.</summary>
    private static bool IsMixed(Geometry g) => g.OgcGeometryType == OgcGeometryType.GeometryCollection;

    private static IEnumerable<Geometry> Parts(Geometry g)
    {
        for (var i = 0; i < g.NumGeometries; i++)
            yield return g.GetGeometryN(i);
    }

    internal static Geometry Intersection(Geometry a, Geometry b)
        => Robust(a, b, static (x, y) => x.Intersection(y));

    internal static Geometry Union(Geometry a, Geometry b)
        => Robust(a, b, UnionOf);

    internal static Geometry Difference(Geometry a, Geometry b)
        => Robust(a, b, DifferenceOf);

    internal static Geometry SymmetricDifference(Geometry a, Geometry b)
        => Robust(a, b, static (x, y) => UnionOf(DifferenceOf(x, y), DifferenceOf(y, x)));

    internal static Geometry UnaryUnion(IReadOnlyList<Geometry> parts)
    {
        try { return UnaryUnionOp.Union(parts); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { }
        try { return UnaryUnionOp.Union(parts.Select(GeometryFixer.Fix).ToList()); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { }
        return Nothing(parts[0]);
    }

    private static Geometry UnionOf(Geometry a, Geometry b)
        => IsMixed(a) || IsMixed(b)
            ? UnaryUnionOp.Union(new[] { a, b })
            : a.Union(b);

    // A minus B. A mixed collection on either side is taken apart: each part of A loses each part of
    // B in turn, and what is left of A's parts is merged again.
    private static Geometry DifferenceOf(Geometry a, Geometry b)
    {
        if (IsMixed(a))
        {
            var left = Parts(a).Select(p => DifferenceOf(p, b)).Where(p => !p.IsEmpty).ToList();
            return left.Count == 0 ? Nothing(a) : UnaryUnionOp.Union(left);
        }
        if (IsMixed(b))
        {
            var rest = a;
            foreach (var part in Parts(b))
                rest = DifferenceOf(rest, part);
            return rest;
        }
        return a.Difference(b);
    }

    private static Geometry Robust(Geometry a, Geometry b, Func<Geometry, Geometry, Geometry> op)
    {
        try { return op(a, b); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { }
        try { return op(GeometryFixer.Fix(a), GeometryFixer.Fix(b)); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { }
        return Nothing(a);
    }
}
