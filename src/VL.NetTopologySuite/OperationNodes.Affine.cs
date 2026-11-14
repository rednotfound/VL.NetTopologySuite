using System;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace VL.NetTopologySuite;

// Tier 2, 2026-11-14: moving, turning and resizing a geometry - NTS's AffineTransformation, the most
// vvvv-shaped thing in the library. Named and ordered as VL's own matrix nodes are (CoreLib's
// Translate, Rotate, Scale), with the rules of ARCHITECTURE "Nodes in VL's shape, coordinates in
// the map's":
//
// - Angles in cycles, counter-clockwise from east, as the shape nodes take them: 0.25 is a quarter
//   turn to the left on a y-north map.
// - X and Y as two numbers, not a Vector2 pin, for the reason the shape nodes take Width and Height:
//   Vector2 is Float32, and the package keeps map numbers in double.
// - Rotate and Scale work around a Center. Unconnected, it is the centre of the geometry's bounding
//   box, so a shape turns and grows in place - Shapely's default (origin="center"). PostGIS turns
//   around (0, 0) instead, which on map coordinates swings the shape far away; nobody wants that by
//   default.
//
// AffineTransformation.Transform copies the geometry before moving it, so the input never changes
// and the result's coordinates are its own. The SRID is kept: these move numbers inside one
// coordinate system. On longitude and latitude they move degrees, which is not a rigid motion on
// the ground (a degree of longitude narrows towards the poles), and the tooltips say so.

public static partial class OperationNodes
{
    /// <summary>
    /// Moves a geometry by an offset.
    /// </summary>
    /// <remarks>
    /// X and Y are in the geometry's own units: degrees on longitude and latitude.
    /// </remarks>
    /// <param name="geometry">The geometry to move.</param>
    /// <param name="x">How far east to move it. Negative moves west.</param>
    /// <param name="y">How far north to move it. Negative moves south.</param>
    public static Geometry? Translate(Geometry? geometry, double x = 0, double y = 0)
        => geometry is null ? null : AffineTransformation.TranslationInstance(x, y).Transform(geometry);

    /// <summary>
    /// Turns a geometry around a center.
    /// </summary>
    /// <remarks>
    /// The rotation is in cycles: 0.25 is a quarter turn counter-clockwise.
    /// Without a Center it turns around the middle of its bounding box.
    /// </remarks>
    /// <param name="geometry">The geometry to turn.</param>
    /// <param name="rotation">How far to turn, in cycles, counter-clockwise.</param>
    /// <param name="center">The point to turn around. Leave it unconnected to turn in place.</param>
    public static Geometry? Rotate(Geometry? geometry, double rotation = 0, Coordinate? center = null)
    {
        if (geometry is null) return null;
        var c = center ?? CenterOf(geometry);
        if (c is null) return geometry.Copy();
        return AffineTransformation.RotationInstance(rotation * 2 * Math.PI, c.X, c.Y).Transform(geometry);
    }

    /// <summary>
    /// Resizes a geometry around a center.
    /// </summary>
    /// <remarks>
    /// 1 keeps the size, 2 doubles it, -1 mirrors it.
    /// Without a Center it grows and shrinks in place, around the middle of its bounding box.
    /// </remarks>
    /// <param name="geometry">The geometry to resize.</param>
    /// <param name="x">The factor east-west.</param>
    /// <param name="y">The factor north-south.</param>
    /// <param name="center">The point that stays where it is. Leave it unconnected to resize in place.</param>
    public static Geometry? Scale(Geometry? geometry, double x = 1, double y = 1, Coordinate? center = null)
    {
        if (geometry is null) return null;
        var c = center ?? CenterOf(geometry);
        if (c is null) return geometry.Copy();
        return AffineTransformation.ScaleInstance(x, y, c.X, c.Y).Transform(geometry);
    }

    // The middle of the bounding box; null for an empty geometry, which then comes back as a copy.
    private static Coordinate? CenterOf(Geometry geometry)
        => geometry.IsEmpty ? null : geometry.EnvelopeInternal.Centre;
}
