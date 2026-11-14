using System.Collections.Generic;
using NetTopologySuite.Geometries;
using SkiaSharp;
using Stride.Core.Mathematics;
using VL.Core;
using VL.Core.Import;
using VL.Skia;

namespace VL.NetTopologySuite;

// The fill of a drawn Geometry IOBox, one polygon at a time (2026-11-14).
//
// Until that day the HDE GeometryViewer folded EVERY ring of a geometry into one Skia path and
// filled it EvenOdd. That cuts a polygon's holes, which is what it was for - but it also cuts the
// overlap of two separate polygons, because a point inside both is inside two rings too. The
// maintainer saw it in the tier-2 help: A and B of a GeometryCollection drawn together looked as if
// a boolean operation had removed their intersection, and asked whether that was not misleading. It
// was: the drawing showed a SymmetricDifference the data did not contain.
//
// A GIS (QGIS, Leaflet, Mapsui) draws each polygon on its own: EvenOdd inside one polygon cuts its
// holes, and two polygons never cut each other - where they overlap the half-transparent fill lands
// twice and reads darker, which is exactly "both cover this". This node does that. The outline is
// still the viewer's one stroked path; only the fill moved here.
//
// Coordinates go in as the viewer's other parts take them: x, and y NEGATED (geometry is y-up,
// Skia is y-down), in the geometry's own units; the viewer's TransformSRT maps them onto the widget.
// The colour comes in as an RGBA, not a paint: VL.Skia's Fill outputs its own SkiaPaint, which is// not SkiaSharp's SKPaint (vvvvc: "SkiaPaint is no SKPaint!", 2026-11-14), so the layer makes// its own. The paths are rebuilt only when the geometry reference changes (the editor keeps one viewer
// instance per IOBox).

/// <summary>
/// Fills every polygon of a geometry on its own, so overlaps read darker and holes stay holes. Used by the editor's viewer.
/// </summary>
[ProcessNode(Name = "GeometryFill", Category = "NetTopologySuite.Viewer")]
[Smell(SymbolSmell.Hidden)]
public sealed class GeometryFillNode
{
    private readonly GeometryFillLayer _layer = new();

    /// <summary>
    /// Fills the polygons of a geometry, each one separately.
    /// </summary>
    /// <param name="geometry">The geometry to fill, as the viewer draws it.</param>
    /// <param name="color">The fill colour; the viewer gives it a quarter opacity.</param>
    /// <returns>A layer that draws the fills.</returns>
    public ILayer Update(Geometry? geometry, Color4 color)
    {
        _layer.Set(geometry, color);
        return _layer;
    }

    /// <summary>
    /// Every polygon of a geometry as its rings, shell first, in the viewer's space (y negated).
    /// Points and lines are not filled and do not appear; collections are walked to the bottom.
    /// </summary>
    internal static List<Vector2[][]> PolygonRings(Geometry? geometry)
    {
        var result = new List<Vector2[][]>();
        Collect(geometry, result);
        return result;
    }

    private static void Collect(Geometry? geometry, List<Vector2[][]> into)
    {
        switch (geometry)
        {
            case null:
                return;
            case Polygon polygon when !polygon.IsEmpty:
                var rings = new Vector2[1 + polygon.NumInteriorRings][];
                rings[0] = Points(polygon.Shell);
                for (var i = 0; i < polygon.NumInteriorRings; i++) rings[i + 1] = Points(polygon.GetInteriorRingN(i));
                into.Add(rings);
                return;
            case GeometryCollection collection:
                for (var i = 0; i < collection.NumGeometries; i++) Collect(collection.GetGeometryN(i), into);
                return;
        }
    }

    private static Vector2[] Points(LineString ring)
    {
        var sequence = ring.CoordinateSequence;
        var points = new Vector2[sequence.Count];
        for (var i = 0; i < points.Length; i++) points[i] = new Vector2((float)sequence.GetX(i), (float)-sequence.GetY(i));
        return points;
    }
}
