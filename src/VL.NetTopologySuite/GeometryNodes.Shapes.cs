using System;
using NetTopologySuite.Geometries;
using NetTopologySuite.Utilities;
using VL.Core.Import;
using VL.Lib.Mathematics;

namespace VL.NetTopologySuite;

// Shapes by position and size, the way VL.Skia describes them (Rectangle: Position, Size, Anchor;
// Circle: Position, Radius) - the maintainer's rule of 2026-11-08, ARCHITECTURE "Nodes in VL's
// shape, coordinates in the map's". Before these, a box was four coordinates through LinearRing and
// Polygon, which is NTS's way of asking and not a patch author's.
//
// Two halves of that rule meet here:
//
// - The ORGANISATION is VL's: the node names, the pin names, VL's own RectangleAnchor enum, angles
//   in cycles (0 to 1 is a full turn, as in VL's FromPolar and Skia's Segment).
// - The COORDINATES are the map's: x east, y NORTH. So an anchor named after a direction means that
//   direction on a map - TopLeft is the north-west corner, the smallest x and the LARGEST y, where
//   in Skia it would be the smallest y. Angles run counter-clockwise from east, the mathematical
//   convention, which on a y-north map is the way a person reads it.
//
// Positions are Coordinate (double), not VL's Vector2: Vector2 is Float32, and a Float32 map
// coordinate is about a metre coarse (measured 2026-11-08: a step of 1.5e-5 degrees at Tokyo's
// longitude, 1.4 m; a step of 1 m at a Web Mercator easting of 15,550,000). The maintainer chose
// Coordinate that day; Vector2 gets conversion nodes of its own.
//
// Every shell is counter-clockwise, the orientation OGC and RFC 7946 (GeoJSON) ask for, and starts
// in the east (circle, ellipse, polygon) or the south-west corner (rectangle). Tests pin both.
//
// A size of zero, a negative radius or no position gives an empty polygon - empty in, empty out, as
// everywhere in this package - rather than a degenerate one that IsValid would reject. A negative
// width or height is taken as its size: a box has no inside-out.
//
// Circle, Ellipse, Arc and Sector are NTS's GeometricShapeFactory behind VL's pins; Rectangle and
// RegularPolygon are a few lines of our own (GeometricShapeFactory spreads its NumPoints over a
// rectangle's sides, and has no regular polygon).

public static partial class GeometryNodes
{
    // ── Shapes ────────────────────────────────────────────────────────────────

    // Pin order and names follow VL.Skia's Rectangle (Position, Size, Anchor), with Size as Width
    // and Height because Size would be a Vector2. Default Center, as in Skia, and 1 by 1, so an
    // unconnected node still shows something.

    /// <summary>
    /// Makes a rectangle from a position, a width and a height.
    /// </summary>
    /// <remarks>
    /// Anchor says which point of the rectangle Position is, read on a map: TopLeft is the north-west corner.
    /// Width and Height are in the units of the coordinates.
    /// </remarks>
    /// <param name="position">Where the rectangle is, at its anchor point.</param>
    /// <param name="width">The size from west to east.</param>
    /// <param name="height">The size from south to north.</param>
    /// <param name="anchor">Which point of the rectangle Position is. Center by default.</param>
    /// <param name="factory">Optional. The factory, for SRID and precision.</param>
    /// <returns>The rectangle, or an empty polygon when a size is zero.</returns>
    public static Polygon Rectangle(
        Coordinate? position = null,
        double width = 1,
        double height = 1,
        RectangleAnchor anchor = RectangleAnchor.Center,
        GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        width = Math.Abs(width);
        height = Math.Abs(height);
        if (position is null || width == 0 || height == 0)
            return f.CreatePolygon();

        var (east, north) = AnchorFactors(anchor);
        var minX = position.X - east * width;
        var minY = position.Y - north * height;
        return Box(f, minX, minY, minX + width, minY + height);
    }

    // Skia's Rectangle (Join Points) takes two corners; so does this, in either order and any two
    // opposite corners, which is the box a user drags out with a mouse.

    /// <summary>
    /// Makes a rectangle between two opposite corners.
    /// </summary>
    /// <remarks>
    /// The corners can be given in any order.
    /// </remarks>
    /// <param name="cornerA">One corner.</param>
    /// <param name="cornerB">The opposite corner.</param>
    /// <param name="factory">Optional. The factory, for SRID and precision.</param>
    /// <returns>The rectangle, or an empty polygon when the corners share an X or a Y.</returns>
    [Name("Rectangle (Join Points)")]
    public static Polygon RectangleJoinPoints(
        [Pin(Name = "Corner A")] Coordinate? cornerA = null,
        [Pin(Name = "Corner B")] Coordinate? cornerB = null,
        GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        if (cornerA is null || cornerB is null)
            return f.CreatePolygon();

        var minX = Math.Min(cornerA.X, cornerB.X);
        var maxX = Math.Max(cornerA.X, cornerB.X);
        var minY = Math.Min(cornerA.Y, cornerB.Y);
        var maxY = Math.Max(cornerA.Y, cornerB.Y);
        return minX == maxX || minY == maxY ? f.CreatePolygon() : Box(f, minX, minY, maxX, maxY);
    }

    // A circle in geometry is a polygon, so it needs a vertex count that Skia's Circle does not.
    // 32 by default, the same order as Buffer's 8 segments per quarter. Radius 0.5 so that the
    // unconnected Circle fits the unconnected Rectangle.

    /// <summary>
    /// Makes a circle around a position, as a polygon.
    /// </summary>
    /// <remarks>
    /// Resolution is the number of corners; more is rounder and heavier.
    /// Radius is in the units of the coordinates: on longitude and latitude it is degrees.
    /// </remarks>
    /// <param name="position">The centre.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="resolution">The number of corners, at least 3.</param>
    /// <param name="factory">Optional. The factory, for SRID and precision.</param>
    /// <returns>The circle, or an empty polygon when the radius is zero.</returns>
    public static Polygon Circle(
        Coordinate? position = null,
        double radius = 0.5,
        int resolution = 32,
        GeometryFactory? factory = null)
        => position is null || radius <= 0
            ? Defaults.Or(factory).CreatePolygon()
            : Ellipse(position, 2 * radius, 2 * radius, resolution, factory);

    /// <summary>
    /// Makes an ellipse around a position, as a polygon.
    /// </summary>
    /// <remarks>
    /// Width runs west to east and Height south to north, in the units of the coordinates.
    /// </remarks>
    /// <param name="position">The centre.</param>
    /// <param name="width">The size from west to east.</param>
    /// <param name="height">The size from south to north.</param>
    /// <param name="resolution">The number of corners, at least 3.</param>
    /// <param name="factory">Optional. The factory, for SRID and precision.</param>
    /// <returns>The ellipse, or an empty polygon when a size is zero.</returns>
    public static Polygon Ellipse(
        Coordinate? position = null,
        double width = 1,
        double height = 1,
        int resolution = 32,
        GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        width = Math.Abs(width);
        height = Math.Abs(height);
        if (position is null || width == 0 || height == 0)
            return f.CreatePolygon();

        var shapes = ShapesAround(f, position, width, height, resolution);
        return shapes.CreateEllipse();
    }

    // Grasshopper's Polygon (Radius, Segments), with VL's angle: Rotation in cycles turns the first
    // corner counter-clockwise from east. Radius is to the corners, the circumscribed circle, which
    // is the reading Grasshopper's own forum settles on.

    /// <summary>
    /// Makes a regular polygon around a position: a triangle, a square, a hexagon.
    /// </summary>
    /// <remarks>
    /// Radius is the distance to the corners.
    /// The first corner points east; Rotation turns it counter-clockwise, in cycles (0.25 is a quarter turn).
    /// </remarks>
    /// <param name="position">The centre.</param>
    /// <param name="radius">The distance from the centre to each corner.</param>
    /// <param name="sides">The number of sides, at least 3.</param>
    /// <param name="rotation">A turn counter-clockwise, in cycles.</param>
    /// <param name="factory">Optional. The factory, for SRID and precision.</param>
    /// <returns>The polygon, or an empty polygon when the radius is zero.</returns>
    [Name("RegularPolygon")]
    public static Polygon RegularPolygon(
        Coordinate? position = null,
        double radius = 0.5,
        int sides = 6,
        double rotation = 0,
        GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        if (position is null || radius <= 0)
            return f.CreatePolygon();

        sides = Math.Max(3, sides);
        var ring = new Coordinate[sides + 1];
        for (var i = 0; i < sides; i++)
        {
            var angle = 2 * Math.PI * (rotation + (double)i / sides);
            ring[i] = new Coordinate(position.X + radius * Math.Cos(angle), position.Y + radius * Math.Sin(angle));
        }
        ring[sides] = ring[0].Copy();
        return f.CreatePolygon(ring);
    }

    // Start and Sweep in cycles, counter-clockwise from east; a negative Sweep runs clockwise. The
    // resolution is per full turn, so a quarter arc of resolution 32 has 8 segments and an arc does
    // not get coarser or finer as it is opened.

    /// <summary>
    /// Makes an arc of a circle, as a line.
    /// </summary>
    /// <remarks>
    /// Start and Sweep are in cycles, counter-clockwise from east: Start 0, Sweep 0.25 is the north-east quarter.
    /// Resolution is per full turn.
    /// </remarks>
    /// <param name="position">The centre of the circle.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="start">Where the arc begins, in cycles from east.</param>
    /// <param name="sweep">How far it runs, in cycles; negative runs clockwise.</param>
    /// <param name="resolution">Corners per full turn.</param>
    /// <param name="factory">Optional. The factory, for SRID and precision.</param>
    /// <returns>The arc, or an empty line when the radius or the sweep is zero.</returns>
    public static LineString Arc(
        Coordinate? position = null,
        double radius = 0.5,
        double start = 0,
        double sweep = 0.25,
        int resolution = 32,
        GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        if (position is null || radius <= 0 || sweep == 0)
            return f.CreateLineString();

        var (from, extent) = Turn(start, sweep);
        var shapes = ShapesAround(f, position, 2 * radius, 2 * radius, PointsFor(sweep, resolution));
        var arc = shapes.CreateArc(from, extent);
        // Turn made the extent positive; a line has a direction, so a clockwise sweep is reversed
        // back to run from Start the way it was asked.
        return sweep < 0 ? (LineString)arc.Reverse() : arc;
    }

    /// <summary>
    /// Makes a slice of a circle, as a polygon: a pie piece.
    /// </summary>
    /// <remarks>
    /// Start and Sweep are in cycles, counter-clockwise from east, as in Arc.
    /// </remarks>
    /// <param name="position">The centre of the circle, the tip of the slice.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="start">Where the slice begins, in cycles from east.</param>
    /// <param name="sweep">How far it runs, in cycles; negative runs clockwise.</param>
    /// <param name="resolution">Corners per full turn.</param>
    /// <param name="factory">Optional. The factory, for SRID and precision.</param>
    /// <returns>The slice, or an empty polygon when the radius or the sweep is zero.</returns>
    public static Polygon Sector(
        Coordinate? position = null,
        double radius = 0.5,
        double start = 0,
        double sweep = 0.25,
        int resolution = 32,
        GeometryFactory? factory = null)
    {
        var f = Defaults.Or(factory);
        if (position is null || radius <= 0 || sweep == 0)
            return f.CreatePolygon();

        var (from, extent) = Turn(start, sweep);
        var shapes = ShapesAround(f, position, 2 * radius, 2 * radius, PointsFor(sweep, resolution));
        var polygon = shapes.CreateArcPolygon(from, extent);
        // CreateArcPolygon follows the sweep's direction; a clockwise sweep gives a clockwise
        // shell, and every shell here is counter-clockwise.
        return polygon.Shell.IsCCW ? polygon : (Polygon)polygon.Reverse();
    }

    // ── Shape helpers ─────────────────────────────────────────────────────────

    // How far west and how far south of the anchor the box begins, as fractions of its size. On a
    // map Top is north, so a Top anchor sits on the box's maximum Y.
    private static (double East, double North) AnchorFactors(RectangleAnchor anchor) => anchor switch
    {
        RectangleAnchor.TopLeft => (0, 1),
        RectangleAnchor.TopCenter => (0.5, 1),
        RectangleAnchor.TopRight => (1, 1),
        RectangleAnchor.MiddleLeft => (0, 0.5),
        RectangleAnchor.MiddleRight => (1, 0.5),
        RectangleAnchor.BottomLeft => (0, 0),
        RectangleAnchor.BottomCenter => (0.5, 0),
        RectangleAnchor.BottomRight => (1, 0),
        _ => (0.5, 0.5),
    };

    // South-west first, counter-clockwise.
    private static Polygon Box(GeometryFactory f, double minX, double minY, double maxX, double maxY)
        => f.CreatePolygon(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        });

    // A fresh factory each call: GeometricShapeFactory is a mutable builder, and one shared between
    // nodes evaluating in the same frame would be a race. It is a few fields; the geometry it makes
    // is the allocation that matters.
    private static GeometricShapeFactory ShapesAround(
        GeometryFactory f, Coordinate centre, double width, double height, int points)
        => new(f)
        {
            Centre = new Coordinate(centre.X, centre.Y),
            Width = width,
            Height = height,
            NumPoints = Math.Max(3, points),
        };

    // Cycles to radians; a negative sweep becomes the same arc started at its other end, because
    // GeometricShapeFactory takes an extent and its sign is not a direction we rely on.
    private static (double From, double Extent) Turn(double start, double sweep)
    {
        sweep = Math.Clamp(sweep, -1, 1);
        if (sweep < 0) { start += sweep; sweep = -sweep; }
        return (2 * Math.PI * start, 2 * Math.PI * sweep);
    }

    private static int PointsFor(double sweep, int resolution)
        => Math.Max(2, (int)Math.Ceiling(Math.Max(3, resolution) * Math.Min(1, Math.Abs(sweep)))) + 1;
}
