using System;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace VL.NetTopologySuite;

// Tier 2, 2026-11-14: linear referencing - a place on a line said as how far along it is, the way a
// road's kilometre posts or "500 m ahead" say it. NTS's LengthIndexedLine, three nodes:
// Interpolate (a point at a place), Locate (the place of a point) and Substring (a piece between
// two places). PostGIS names them ST_LineInterpolatePoint, ST_LineLocatePoint, ST_LineSubstring;
// Shapely interpolate, project, substring. "Project" is not used here: in a family with VL.PROJ,
// project means a map projection, and Locate is what PostGIS calls it.
//
// The place is a FRACTION, 0 at the start and 1 at the end, as PostGIS takes it - and as an LFO, a
// slider or any vvvv animation gives it. A length in the geometry's units would be degrees on
// longitude and latitude, and would need multiplying by Length before any animation. The maintainer
// chose to try fractions first (2026-11-14); a Normalized switch can be added later without breaking
// a patch, while removing one would. The fraction is clamped to 0..1: NTS itself takes a negative
// length as counted from the end and stops at the end past it (measured), which in a patch is a
// surprise either way.
//
// Measured against NetTopologySuite 2.6.0 (scratch probe, 2026-11-14):
// - Only lineal geometry is accepted; a polygon throws "Lineal geometry is required", a point an
//   InvalidCastException, an empty line IndexOutOfRange. So a polygon is walked along its outline
//   (Boundary: the shell, then each hole), which is what "a dot running round a circle" needs, and
//   anything else gives an empty result rather than a throw sixty times a second.
// - A MultiLineString is walked part after part, jumping from one end to the next start.
// - ExtractLine with start > end returns the piece reversed; equal ends give a two-point line of
//   length 0. Z is interpolated. The SRID is kept.
// - ExtractPoint at the very end returns the input's own Coordinate instance, and ExtractLine shares
//   coordinates with the input. Both are copied here (Defaults: geometry is treated as immutable).

public static partial class OperationNodes
{
    /// <summary>
    /// Gives the point at a fraction of the way along a line.
    /// </summary>
    /// <remarks>
    /// 0 is the start, 0.5 halfway, 1 the end. A polygon is walked along its outline.
    /// Feed it an LFO to move a point along the line.
    /// </remarks>
    /// <param name="line">The line to walk along: a LineString, a MultiLineString or a polygon's outline.</param>
    /// <param name="position">How far along, from 0 at the start to 1 at the end.</param>
    public static Point Interpolate(Geometry? line, double position = 0.5)
    {
        var walk = Walkable(line);
        if (walk is null)
            return EmptyPointLike(line);

        var length = walk.Length;
        var c = new LengthIndexedLine(walk).ExtractPoint(Clamp01(position) * length).Copy();
        var point = walk.Factory.CreatePoint(c);
        point.SRID = line!.SRID;
        return point;
    }

    /// <summary>
    /// Gives how far along a line the nearest place to a point is, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// The point need not be on the line. Interpolate with the result gives the nearest point on the line.
    /// NaN when there is no line or no point.
    /// </remarks>
    /// <param name="line">The line to measure along.</param>
    /// <param name="point">The point to look from. Anything other than a Point is taken at its centroid.</param>
    public static double Locate(Geometry? line, Geometry? point)
    {
        // NaN when there is nothing to measure along or from, as Distance gives it (review
        // 2026-11-14; was 0, which reads as "at the start"). Interpolate takes NaN as 0.
        var walk = Walkable(line);
        if (walk is null || point is null || point.IsEmpty)
            return double.NaN;

        var length = walk.Length;
        if (length <= 0)
            return double.NaN;

        var c = point is Point p ? p.Coordinate : point.Centroid.Coordinate;
        return Clamp01(new LengthIndexedLine(walk).Project(c) / length);
    }

    /// <summary>
    /// Gives the piece of a line between two fractions of the way along it.
    /// </summary>
    /// <remarks>
    /// 0 is the start and 1 the end. Start after End gives the piece reversed.
    /// Animate End from 0 to 1 and the line draws itself.
    /// </remarks>
    /// <param name="line">The line to cut: a LineString, a MultiLineString or a polygon's outline.</param>
    /// <param name="start">Where the piece begins, from 0 to 1.</param>
    /// <param name="end">Where the piece ends, from 0 to 1.</param>
    public static Geometry Substring(Geometry? line, double start = 0, double end = 1)
    {
        if (line is null)
            return Defaults.Factory.CreateLineString();

        var walk = Walkable(line);
        if (walk is null)
            return line.Factory.CreateLineString();

        var length = walk.Length;
        var piece = new LengthIndexedLine(walk).ExtractLine(Clamp01(start) * length, Clamp01(end) * length).Copy();
        piece.SRID = line.SRID;
        return piece;
    }

    // The lineal geometry to walk along, or null when there is nothing to walk.
    private static Geometry? Walkable(Geometry? line)
    {
        if (line is null || line.IsEmpty)
            return null;
        if (line is ILineal)
            return line;
        if (line is IPolygonal)
            return line.Boundary;
        return null;
    }

    private static Point EmptyPointLike(Geometry? line)
    {
        var factory = line?.Factory ?? Defaults.Factory;
        var point = factory.CreatePoint();
        if (line is not null)
            point.SRID = line.SRID;
        return point;
    }

    private static double Clamp01(double t) => double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1);
}
