using System;
using NetTopologySuite.Densify;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace VL.NetTopologySuite;

// Tier 2, 2026-11-14: two nodes that change a geometry's vertices without changing what it covers -
// Densify adds vertices, MakeValid repairs. Measured against NetTopologySuite 2.6.0 (scratch probe):
//
// - Densifier.Densify throws "Tolerance must be positive" at 0. Here 0 (the default, as Buffer and
//   Simplify have it) or less means "no change": the input is returned as a copy. It inserts evenly:
//   LINESTRING (0 0, 10 0) at 3 gives 2.5-long segments, never longer than asked.
// - A densify of a long line with a tiny spacing is millions of points sixty times a second, and an
//   IOBox somebody is typing 0.001 into passes through such values. So the spacing is raised until
//   the result has at most Defaults.MaxVertices new vertices, and the remark says so.
// - GeometryFixer.Fix turns a bowtie POLYGON ((0 0, 2 2, 2 0, 0 2, 0 0)) into a MultiPolygon of two
//   triangles. It also rewrites a polygon that was ALREADY valid: POLYGON ((0 0, 1 0, 1 1, 0 0))
//   comes back as ((0 0, 1 1, 1 0, 0 0)), its ring reversed (a test caught it). Shapely and PostGIS
//   return valid input unchanged, so a valid geometry is copied here without the fixer. A collapsed line
//   (LINESTRING (1 1, 1 1)) becomes empty (KeepCollapsed off, as Shapely's make_valid drops it too).
//   PostGIS and Shapely both call it make_valid.

public static partial class OperationNodes
{
    /// <summary>
    /// Adds vertices so that no segment is longer than a spacing.
    /// </summary>
    /// <remarks>
    /// The spacing is in the geometry's own units. 0 leaves the geometry as it is.
    /// At most 100000 vertices are added; a smaller spacing is raised to keep to that.
    /// </remarks>
    /// <param name="geometry">The geometry to fill in.</param>
    /// <param name="spacing">The longest a segment may be. 0 changes nothing.</param>
    public static Geometry Densify(Geometry? geometry, double spacing = 0)
    {
        if (geometry is null) return Overlays.Nothing();
        if (!(spacing > 0) || geometry.IsEmpty) return geometry.Copy();

        var floor = geometry.Length / Defaults.MaxVertices;
        return Densifier.Densify(geometry, Math.Max(spacing, floor));
    }

    /// <summary>
    /// Repairs an invalid geometry, keeping as much of it as possible.
    /// </summary>
    /// <remarks>
    /// A self-crossing polygon becomes the pieces it encloses. A valid geometry comes back unchanged.
    /// IsValid tells why a geometry is invalid.
    /// </remarks>
    /// <param name="geometry">The geometry to repair.</param>
    public static Geometry MakeValid(Geometry? geometry)
        => geometry is null ? Overlays.Nothing()
            : geometry.IsValid ? geometry.Copy()
            : GeometryFixer.Fix(geometry);
}
