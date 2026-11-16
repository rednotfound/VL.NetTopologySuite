using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using NetTopologySuite.Geometries;
using VL.Core;

namespace VL.NetTopologySuite;

// Which basemap tiles GeometryEditor's window draws, and where (2026-11-16, step 3 of the SRID
// plan; see IBasemap.cs for where the tiles come from). No Skia and no network in here, so all of it
// is tested.
//
// Tiles are Web Mercator. On SRID 3857 the geometry's numbers ARE Web Mercator metres and a tile
// lands exactly. On 4326 the window draws longitude and latitude with x narrowed by cos(latitude)
// (SketchView): each tile is placed by its own corners, so it sits exactly at its edges and is off
// inside by the difference between Mercator and that local scale - under a pixel at street and
// district scale, visibly off only across many degrees of latitude, where a basemap under a
// hand-drawn shape stops meaning much anyway. Other SRIDs get no basemap: their numbers would need
// reprojecting first, and a map in the wrong place is worse than none.

/// <summary>Basemap tiles for the editor window: which, at what zoom, and where on screen.</summary>
internal static class Basemap
{
    /// <summary>How a geometry's numbers relate to Web Mercator tiles.</summary>
    public enum Projection { None, Degrees, Mercator }

    /// <summary>A tile to draw: its index and its corners in window pixels.</summary>
    public readonly record struct Spot(int Zoom, int X, int Y, Vector2 TopLeft, Vector2 BottomRight);

    /// <summary>For tests: a basemap used instead of the registered one.</summary>
    internal static IBasemap? ProviderOverride { get; set; }

    /// <summary>The registered basemap (VL.Mapsui's), or null. Looked up in AppHost.Global, as ICrsCatalog is.</summary>
    public static IBasemap? Provider
    {
        get
        {
            if (ProviderOverride is { } p) return p;
            try { return AppHost.Global?.Services.GetService(typeof(IBasemap)) as IBasemap; }
            catch (Exception) { return null; }
        }
    }

    private const double Radius = 6378137;
    private const double HalfWorld = Math.PI * Radius;   // Web Mercator metres from the centre to an edge
    private const double MaxLatitude = 85.0511287798066;  // where Web Mercator's square world ends
    public const int MaxTiles = 64;                       // a view never asks for more at once

    /// <summary>4326 (and its 3D 4979) as degrees, 3857 and its old codes as Mercator, everything else none.</summary>
    public static Projection ProjectionOf(int srid) => srid switch
    {
        4326 or 4979 => Projection.Degrees,
        3857 or 900913 or 3785 or 102100 or 102113 => Projection.Mercator,
        _ => Projection.None,
    };

    /// <summary>The zoom whose tiles are drawn about <paramref name="tilePixels"/> wide.</summary>
    public static int ChooseZoom(SketchView view, Projection projection, float tilePixels, int maxZoom)
    {
        var world = projection == Projection.Degrees ? 360.0 : 2 * HalfWorld;
        var z = Math.Log2(view.PixelsPerUnitX * world / Math.Max(1, tilePixels));
        return double.IsFinite(z) ? Math.Clamp((int)Math.Round(z), 0, Math.Max(0, maxZoom)) : 0;
    }

    /// <summary>The tiles covering the window, each with its corners in pixels; a coarser zoom when there would be too many.</summary>
    public static List<Spot> Visible(SketchView view, Vector2 size, Projection projection, int zoom)
    {
        var spots = new List<Spot>();
        if (projection == Projection.None || size.X <= 0 || size.Y <= 0) return spots;

        var a = view.ToGeometry(Vector2.Zero, size);   // north-west
        var b = view.ToGeometry(size, size);           // south-east
        for (; zoom >= 0; zoom--)
        {
            var n = 1 << zoom;
            var (fx0, fy0) = ToTile(a, projection, zoom);
            var (fx1, fy1) = ToTile(b, projection, zoom);
            var x0 = Math.Clamp((int)Math.Floor(Math.Min(fx0, fx1)), 0, n - 1);
            var x1 = Math.Clamp((int)Math.Floor(Math.Max(fx0, fx1)), 0, n - 1);
            var y0 = Math.Clamp((int)Math.Floor(Math.Min(fy0, fy1)), 0, n - 1);
            var y1 = Math.Clamp((int)Math.Floor(Math.Max(fy0, fy1)), 0, n - 1);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > MaxTiles && zoom > 0) continue;

            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    spots.Add(new Spot(zoom, x, y,
                        view.ToPixels(Corner(x, y, zoom, projection), size),
                        view.ToPixels(Corner(x + 1, y + 1, zoom, projection), size)));
            break;
        }
        return spots;
    }

    /// <summary>A coordinate as a fractional tile position at a zoom.</summary>
    public static (double X, double Y) ToTile(Coordinate c, Projection projection, int zoom)
    {
        double n = 1 << zoom;
        if (projection == Projection.Degrees)
        {
            var lat = Math.Clamp(c.Y, -MaxLatitude, MaxLatitude) * Math.PI / 180;
            return ((c.X + 180) / 360 * n, (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * n);
        }
        return ((c.X + HalfWorld) / (2 * HalfWorld) * n, (HalfWorld - c.Y) / (2 * HalfWorld) * n);
    }

    /// <summary>A tile's north-west corner in the geometry's numbers.</summary>
    public static Coordinate Corner(int x, int y, int zoom, Projection projection)
    {
        double n = 1 << zoom;
        if (projection == Projection.Degrees)
            return new Coordinate(x / n * 360 - 180, Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n))) * 180 / Math.PI);
        return new Coordinate(x / n * 2 * HalfWorld - HalfWorld, HalfWorld - y / n * 2 * HalfWorld);
    }
}
