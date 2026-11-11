using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetTopologySuite.Geometries;

namespace VL.NetTopologySuite;

// What a geometry drawn by hand is while it is being drawn: a list of PARTS, each a point, a line or
// a polygon (a shell and any number of holes). The model behind GeometryEditor's window, kept apart
// from Skia and from vvvv so the editing rules are plain arithmetic, tested by GeometrySketchTests.
//
// First version 2026-11-11 morning: one part, no holes. The maintainer asked the same day for every
// geometry type the package has, so a sketch is now parts and the written type follows them:
//
//   one part                      POINT / LINESTRING / POLYGON
//   several parts of one kind     MULTIPOINT / MULTILINESTRING / MULTIPOLYGON
//   parts of different kinds      GEOMETRYCOLLECTION
//
// and what was READ decides the container when that is not the automatic one - a MULTIPOLYGON
// with one part stays MULTIPOLYGON, a GEOMETRYCOLLECTION of two points stays a collection - so
// every editable WKT round-trips exactly (tests).
//
// Immutable on purpose: every edit returns a new sketch. VL's change detection is object identity
// (memory: vl-change-detection-is-object-identity), so a new object per edit is exactly what makes
// downstream Cache / Changed see the edit, and an unchanged sketch costs nothing per frame.
//
// A part still being drawn may be degenerate: a line with one vertex, a polygon with two, a hole
// with two. ToGeometry writes such a part as the lower dimension it is (POINT, LINESTRING) and
// drops a degenerate hole, because NTS refuses a one-point LineString and a two-point ring. The
// window only writes a part back into the document when it is finished, and discards a finished
// part that is still degenerate, so this only shows while drawing.
//
// Editable: POINT, LINESTRING, POLYGON (with holes), MULTIPOINT, MULTILINESTRING, MULTIPOLYGON,
// GEOMETRYCOLLECTION of points, lines and polygons, and their EMPTY forms - all in two dimensions.
// Not editable, shown read-only so nothing is lost: geometry with Z (the window draws in 2D and
// the writer would drop it), a collection that contains a multi-geometry or another collection
// (flattening would change its structure), and LINEARRING (an implementation type; a user's ring
// is a polygon's shell).

/// <summary>What a part of a sketch is.</summary>
internal enum PartKind
{
    Point,
    Line,
    Polygon,
}

/// <summary>How the parts are wrapped when written.</summary>
internal enum SketchContainer
{
    /// <summary>One part alone, several of a kind as Multi, mixed kinds as a collection.</summary>
    Auto,

    /// <summary>Multi even with one part, while all parts are of one kind.</summary>
    Multi,

    /// <summary>Always a GeometryCollection.</summary>
    Collection,
}

/// <summary>A vertex of a sketch: which part, which ring of it (0 for points and lines, 0 = shell for polygons), which vertex.</summary>
internal readonly record struct VertexRef(int Part, int Ring, int Index);

/// <summary>
/// One part of a sketch: a point (one ring of one vertex), a line (one ring, the path) or a
/// polygon (ring 0 the shell, the rest holes; the closing vertex of every ring is implicit).
/// </summary>
internal sealed class SketchPart
{
    internal SketchPart(PartKind kind, ImmutableArray<ImmutableArray<Coordinate>> rings)
    {
        Kind = kind;
        Rings = rings;
    }

    public PartKind Kind { get; }

    internal ImmutableArray<ImmutableArray<Coordinate>> Rings { get; }

    public int RingCount => Rings.Length;

    public int VertexCount(int ring) => ring >= 0 && ring < Rings.Length ? Rings[ring].Length : 0;

    /// <summary>Copies of the vertices of one ring.</summary>
    public Coordinate[] Ring(int ring) => Defaults.CopyOf(Rings[ring]);

    /// <summary>The part on its own, degenerate as the lower dimension it is, or null when it has no vertex.</summary>
    public Geometry? ToGeometry(GeometryFactory f)
    {
        if (Rings.Length == 0 || Rings[0].Length == 0) return null;
        var shell = Defaults.CopyOf(Rings[0]);
        switch (Kind)
        {
            case PartKind.Point:
                return f.CreatePoint(shell[0]);
            case PartKind.Line:
                return shell.Length == 1 ? f.CreatePoint(shell[0]) : f.CreateLineString(shell);
            default:
                if (shell.Length == 1) return f.CreatePoint(shell[0]);
                if (shell.Length == 2) return f.CreateLineString(shell);
                var holes = Rings.Skip(1)
                    .Where(h => h.Length >= 3)
                    .Select(h => f.CreateLinearRing(Defaults.CopyAndClose(h)))
                    .ToArray();
                return f.CreatePolygon(f.CreateLinearRing(Defaults.CopyAndClose(shell)), holes);
        }
    }

    /// <summary>A finished part is one that is its kind: a line of 2 vertices, a polygon of 3, holes of 3.</summary>
    public bool IsComplete => Kind switch
    {
        PartKind.Point => VertexCount(0) == 1,
        PartKind.Line => VertexCount(0) >= 2,
        _ => VertexCount(0) >= 3 && Rings.Skip(1).All(h => h.Length >= 3),
    };
}

/// <summary>
/// A geometry being drawn by hand: its parts, in geometry coordinates (y north), and how they are wrapped.
/// </summary>
internal sealed class GeometrySketch
{
    public static readonly GeometrySketch Empty = new(ImmutableArray<SketchPart>.Empty, SketchContainer.Auto, "GEOMETRYCOLLECTION");

    private GeometrySketch(ImmutableArray<SketchPart> parts, SketchContainer container, string emptyType)
    {
        _parts = parts;
        Container = container;
        EmptyType = emptyType;
    }

    private readonly ImmutableArray<SketchPart> _parts;

    public SketchContainer Container { get; }

    /// <summary>The WKT type written when there are no parts ("POLYGON" for "POLYGON EMPTY").</summary>
    public string EmptyType { get; }

    public int PartCount => _parts.Length;

    public SketchPart Part(int index) => _parts[index];

    public IEnumerable<SketchPart> Parts => _parts;

    public int VertexCount => _parts.Sum(p => p.Rings.Sum(r => r.Length));

    /// <summary>A copy of one vertex.</summary>
    public Coordinate this[VertexRef v] => _parts[v.Part].Rings[v.Ring][v.Index].Copy();

    private GeometrySketch With(ImmutableArray<SketchPart> parts) => new(parts, Container, EmptyType);

    private static ImmutableArray<ImmutableArray<Coordinate>> Rings(params IEnumerable<Coordinate>[] rings)
        => rings.Select(r => r.Select(c => c.Copy()).ToImmutableArray()).ToImmutableArray();

    // ---- building ---------------------------------------------------------------------------

    public static GeometrySketch Create(IEnumerable<SketchPart>? parts = null,
        SketchContainer container = SketchContainer.Auto, string emptyType = "GEOMETRYCOLLECTION")
        => new(parts?.ToImmutableArray() ?? ImmutableArray<SketchPart>.Empty, container, emptyType);

    public static SketchPart PointPart(Coordinate c) => new(PartKind.Point, Rings(new[] { c }));

    public static SketchPart LinePart(IEnumerable<Coordinate> path) => new(PartKind.Line, Rings(path));

    public static SketchPart PolygonPart(IEnumerable<Coordinate> shell, params IEnumerable<Coordinate>[] holes)
        => new(PartKind.Polygon, Rings(new[] { shell }.Concat(holes).ToArray()));

    /// <summary>Adds a part of <paramref name="kind"/> with its first vertex; the new part is the last one.</summary>
    public GeometrySketch AddPart(PartKind kind, Coordinate first)
        => With(_parts.Add(new SketchPart(kind, Rings(new[] { first }))));

    public GeometrySketch AddPart(SketchPart part) => With(_parts.Add(part));

    /// <summary>Adds a hole to polygon part <paramref name="part"/> with its first vertex; the new hole is its last ring.</summary>
    public GeometrySketch AddHole(int part, Coordinate first)
    {
        if (!IsPart(part) || _parts[part].Kind != PartKind.Polygon) return this;
        var p = _parts[part];
        return With(_parts.SetItem(part, new SketchPart(p.Kind, p.Rings.Add(ImmutableArray.Create(first.Copy())))));
    }

    public GeometrySketch RemovePart(int part) => IsPart(part) ? With(_parts.RemoveAt(part)) : this;

    public GeometrySketch MovePart(int part, double dx, double dy)
    {
        if (!IsPart(part)) return this;
        var p = _parts[part];
        var rings = p.Rings.Select(r => r.Select(c => Shifted(c, dx, dy)).ToImmutableArray()).ToImmutableArray();
        return With(_parts.SetItem(part, new SketchPart(p.Kind, rings)));
    }

    private static Coordinate Shifted(Coordinate c, double dx, double dy)
    {
        var s = c.Copy();
        s.X += dx;
        s.Y += dy;
        return s;
    }

    // ---- vertices ---------------------------------------------------------------------------

    /// <summary>Appends a vertex to the end of a ring (a point part takes no second vertex).</summary>
    public GeometrySketch Append(int part, int ring, Coordinate c)
    {
        if (!IsRing(part, ring) || _parts[part].Kind == PartKind.Point) return this;
        return SetRing(part, ring, _parts[part].Rings[ring].Add(c.Copy()));
    }

    /// <summary>Inserts a vertex before <paramref name="at"/>.Index (the ring's length appends).</summary>
    public GeometrySketch Insert(VertexRef at, Coordinate c)
    {
        if (!IsRing(at.Part, at.Ring) || _parts[at.Part].Kind == PartKind.Point) return this;
        var ring = _parts[at.Part].Rings[at.Ring];
        if (at.Index < 0 || at.Index > ring.Length) return this;
        return SetRing(at.Part, at.Ring, ring.Insert(at.Index, c.Copy()));
    }

    public GeometrySketch Move(VertexRef v, Coordinate to)
        => IsVertex(v) ? SetRing(v.Part, v.Ring, _parts[v.Part].Rings[v.Ring].SetItem(v.Index, to.Copy())) : this;

    /// <summary>
    /// Removes a vertex. A ring left empty goes with it - a hole is removed, a shell or a path or a
    /// point takes its whole part along.
    /// </summary>
    public GeometrySketch Remove(VertexRef v)
    {
        if (!IsVertex(v)) return this;
        var ring = _parts[v.Part].Rings[v.Ring].RemoveAt(v.Index);
        if (ring.Length > 0) return SetRing(v.Part, v.Ring, ring);
        if (v.Ring == 0) return RemovePart(v.Part);
        var p = _parts[v.Part];
        return With(_parts.SetItem(v.Part, new SketchPart(p.Kind, p.Rings.RemoveAt(v.Ring))));
    }

    /// <summary>Removes a whole ring: a hole goes alone, a shell (ring 0) takes its part along.</summary>
    public GeometrySketch RemoveRing(int part, int ring)
    {
        if (!IsRing(part, ring)) return this;
        if (ring == 0) return RemovePart(part);
        var p = _parts[part];
        return With(_parts.SetItem(part, new SketchPart(p.Kind, p.Rings.RemoveAt(ring))));
    }

    /// <summary>Removes the last vertex of a ring (Backspace while drawing).</summary>
    public GeometrySketch RemoveLast(int part, int ring)
        => IsRing(part, ring) && _parts[part].Rings[ring].Length > 0
            ? Remove(new VertexRef(part, ring, _parts[part].Rings[ring].Length - 1))
            : this;

    private GeometrySketch SetRing(int part, int ring, ImmutableArray<Coordinate> vertices)
    {
        var p = _parts[part];
        return With(_parts.SetItem(part, new SketchPart(p.Kind, p.Rings.SetItem(ring, vertices))));
    }

    private bool IsPart(int part) => part >= 0 && part < _parts.Length;

    private bool IsRing(int part, int ring) => IsPart(part) && ring >= 0 && ring < _parts[part].Rings.Length;

    private bool IsVertex(VertexRef v) => IsRing(v.Part, v.Ring) && v.Index >= 0 && v.Index < _parts[v.Part].Rings[v.Ring].Length;

    // ---- finding things under the mouse ------------------------------------------------------

    /// <summary>The vertex nearest to <paramref name="at"/> within <paramref name="tolerance"/>, or null.</summary>
    public VertexRef? NearestVertex(Coordinate at, double tolerance)
    {
        VertexRef? best = null;
        var bestDistance = tolerance;
        for (var p = 0; p < _parts.Length; p++)
            for (var r = 0; r < _parts[p].Rings.Length; r++)
                for (var i = 0; i < _parts[p].Rings[r].Length; i++)
                {
                    var d = _parts[p].Rings[r][i].Distance(at);
                    if (d <= bestDistance)
                    {
                        best = new VertexRef(p, r, i);
                        bestDistance = d;
                    }
                }
        return best;
    }

    /// <summary>
    /// The edge nearest to <paramref name="at"/> within <paramref name="tolerance"/>, as where to
    /// insert a vertex on it (the edge's end), or null. A polygon ring's closing edge counts.
    /// </summary>
    public VertexRef? NearestEdge(Coordinate at, double tolerance)
    {
        VertexRef? best = null;
        var bestDistance = tolerance;
        for (var p = 0; p < _parts.Length; p++)
        {
            var part = _parts[p];
            if (part.Kind == PartKind.Point) continue;
            for (var r = 0; r < part.Rings.Length; r++)
            {
                var ring = part.Rings[r];
                if (ring.Length < 2) continue;
                var edges = part.Kind == PartKind.Polygon && ring.Length >= 3 ? ring.Length : ring.Length - 1;
                for (var i = 0; i < edges; i++)
                {
                    var d = new LineSegment(ring[i], ring[(i + 1) % ring.Length]).Distance(at);
                    if (d <= bestDistance)
                    {
                        best = new VertexRef(p, r, i + 1);
                        bestDistance = d;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>
    /// The part under <paramref name="at"/>: a vertex or edge within tolerance, or the inside of a
    /// polygon (not in a hole). The topmost - the last drawn - wins. -1 when none.
    /// </summary>
    public int PartAt(Coordinate at, double tolerance)
    {
        var f = Defaults.Factory;
        var point = f.CreatePoint(at.Copy());
        for (var p = _parts.Length - 1; p >= 0; p--)
        {
            var g = _parts[p].ToGeometry(f);
            if (g is null) continue;
            if (g.Distance(point) <= tolerance) return p;
        }
        return -1;
    }

    /// <summary>The polygon part whose inside (holes excluded) holds <paramref name="at"/>, last drawn first; -1 when none.</summary>
    public int PolygonAt(Coordinate at)
    {
        var point = Defaults.Factory.CreatePoint(at.Copy());
        for (var p = _parts.Length - 1; p >= 0; p--)
            if (_parts[p].Kind == PartKind.Polygon && _parts[p].ToGeometry(Defaults.Factory) is Polygon poly && poly.Contains(point))
                return p;
        return -1;
    }

    // ---- writing ----------------------------------------------------------------------------

    /// <summary>The geometry the sketch makes now. See the class comment for the container rule.</summary>
    public Geometry ToGeometry(GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        var geometries = _parts.Select(p => p.ToGeometry(f)).Where(g => g is not null).Select(g => g!).ToArray();

        if (geometries.Length == 0) return EmptyOf(EmptyType, f);

        if (Container == SketchContainer.Collection)
            return f.CreateGeometryCollection(geometries);

        var single = Container == SketchContainer.Auto && geometries.Length == 1;
        if (single) return geometries[0];

        if (geometries.All(g => g is Point)) return f.CreateMultiPoint(geometries.Cast<Point>().ToArray());
        if (geometries.All(g => g is LineString)) return f.CreateMultiLineString(geometries.Cast<LineString>().ToArray());
        if (geometries.All(g => g is Polygon)) return f.CreateMultiPolygon(geometries.Cast<Polygon>().ToArray());
        return f.CreateGeometryCollection(geometries);
    }

    public string ToWKT() => IONodes.ToWKT(ToGeometry());

    private static Geometry EmptyOf(string type, GeometryFactory f) => type switch
    {
        "POINT" => f.CreatePoint(),
        "LINESTRING" => f.CreateLineString(),
        "POLYGON" => f.CreatePolygon(),
        "MULTIPOINT" => f.CreateMultiPoint(),
        "MULTILINESTRING" => f.CreateMultiLineString(),
        "MULTIPOLYGON" => f.CreateMultiPolygon(),
        _ => f.CreateGeometryCollection(),
    };

    // ---- reading ----------------------------------------------------------------------------

    /// <summary>
    /// Reads a sketch from WKT. Null or blank is an empty sketch. False when the WKT does not parse
    /// or is a geometry this editor cannot edit without losing something.
    /// </summary>
    public static bool TryFromWKT(string? wkt, out GeometrySketch sketch)
    {
        sketch = Empty;
        if (string.IsNullOrWhiteSpace(wkt)) return true;
        var geometry = IONodes.FromWKT(wkt, null, out var success);
        return success && TryFromGeometry(geometry, out sketch);
    }

    public static bool TryFromGeometry(Geometry? geometry, out GeometrySketch sketch)
    {
        sketch = Empty;
        if (geometry is null) return true;
        if (HasZ(geometry) || geometry is LinearRing) return false;

        var emptyType = geometry.GeometryType.ToUpperInvariant();
        switch (geometry)
        {
            case Point or LineString or Polygon:
                {
                    var part = PartOf(geometry);
                    sketch = new(part is null ? ImmutableArray<SketchPart>.Empty : ImmutableArray.Create(part), SketchContainer.Auto, emptyType);
                    return true;
                }
            case MultiPoint or MultiLineString or MultiPolygon:
                {
                    var parts = Children(geometry).Select(PartOf).Where(p => p is not null).Select(p => p!).ToImmutableArray();
                    // A Multi stays Multi, also when parts are deleted down to one: a patch downstream
                    // may expect the Multi type (SketchSessionTests, Delete_removes_the_selected_shape).
                    sketch = new(parts, SketchContainer.Multi, emptyType);
                    return true;
                }
            case GeometryCollection gc:
                {
                    var children = Children(gc).ToArray();
                    if (children.Any(c => c is GeometryCollection or LinearRing)) return false; // nested: flattening would change it
                    var parts = children.Select(PartOf).Where(p => p is not null).Select(p => p!).ToImmutableArray();
                    sketch = new(parts, SketchContainer.Collection, emptyType);
                    return true;
                }
            default:
                return false;
        }
    }

    private static IEnumerable<Geometry> Children(Geometry g)
    {
        for (var i = 0; i < g.NumGeometries; i++) yield return g.GetGeometryN(i);
    }

    private static SketchPart? PartOf(Geometry g)
    {
        if (g.IsEmpty) return null;
        return g switch
        {
            Point p => PointPart(p.Coordinate),
            LineString l => LinePart(l.Coordinates),
            Polygon poly => PolygonPart(Open(poly.Shell.Coordinates), poly.Holes.Select(h => Open(h.Coordinates)).ToArray()),
            _ => null,
        };
    }

    private static bool HasZ(Geometry g) => g.Coordinates.Any(c => !double.IsNaN(c.Z));

    // A closed ring's last coordinate repeats its first; the sketch keeps the closing vertex implicit.
    private static IEnumerable<Coordinate> Open(Coordinate[] ring)
        => ring.Length > 1 && ring[0].Equals2D(ring[^1]) ? ring.Take(ring.Length - 1) : ring;
}
