using System;
using System.Linq;
using Stride.Core.Mathematics;
using NetTopologySuite.Geometries;
using Xunit;
using static VL.NetTopologySuite.Basemap;

namespace VL.NetTopologySuite.Tests;

/// <summary>Which basemap tiles the editor window draws, and where. No network, no Skia.</summary>
public class BasemapTests
{
    const double HalfWorld = Math.PI * 6378137;

    [Fact]
    public void Only_longitude_latitude_and_web_mercator_get_a_basemap()
    {
        Assert.Equal(Projection.Degrees, ProjectionOf(4326));
        Assert.Equal(Projection.Mercator, ProjectionOf(3857));
        Assert.Equal(Projection.Mercator, ProjectionOf(900913));
        Assert.Equal(Projection.None, ProjectionOf(0));
        Assert.Equal(Projection.None, ProjectionOf(28992));   // metres, but not Mercator's
        Assert.Equal(Projection.None, ProjectionOf(4612));    // degrees, but not WGS 84
    }

    [Fact]
    public void Tile_corners_are_the_slippy_map_ones()
    {
        var nw = Corner(0, 0, 0, Projection.Degrees);
        Assert.Equal(-180, nw.X, 9);
        Assert.Equal(85.0511287798, nw.Y, 9);
        var centre = Corner(1, 1, 1, Projection.Degrees);
        Assert.Equal(0, centre.X, 9);
        Assert.Equal(0, centre.Y, 9);
        var m = Corner(0, 0, 0, Projection.Mercator);
        Assert.Equal(-HalfWorld, m.X, 3);
        Assert.Equal(HalfWorld, m.Y, 3);
    }

    [Theory]
    [InlineData(139.7671, 35.6812, 16)]   // Tokyo Station
    [InlineData(-0.1276, 51.5072, 12)]    // London, west of Greenwich
    [InlineData(-58.3816, -34.6037, 9)]   // Buenos Aires, south
    public void A_place_falls_inside_the_tile_it_is_in(double lon, double lat, int zoom)
    {
        var (fx, fy) = ToTile(new Coordinate(lon, lat), Projection.Degrees, zoom);
        int x = (int)Math.Floor(fx), y = (int)Math.Floor(fy);
        var nw = Corner(x, y, zoom, Projection.Degrees);
        var se = Corner(x + 1, y + 1, zoom, Projection.Degrees);
        Assert.InRange(lon, nw.X, se.X);
        Assert.InRange(lat, se.Y, nw.Y);   // y grows south

        // The same place in Web Mercator metres lands in the same tile.
        var mx = lon * Math.PI / 180 * 6378137;
        var my = Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360)) * 6378137;
        var (gx, gy) = ToTile(new Coordinate(mx, my), Projection.Mercator, zoom);
        Assert.Equal(fx, gx, 6);
        Assert.Equal(fy, gy, 6);
    }

    [Fact]
    public void The_zoom_draws_tiles_about_256_pixels_wide()
    {
        // 2^16 tiles across 360 degrees, each 256 px: 46603 px per degree of longitude.
        var view = new SketchView(139.7671, 35.6812, 46603.0 / Math.Cos(35.6812 * Math.PI / 180), Math.Cos(35.6812 * Math.PI / 180));
        Assert.Equal(16, ChooseZoom(view, Projection.Degrees, 256, 19));
        Assert.Equal(15, ChooseZoom(view, Projection.Degrees, 512, 19));   // a 2x screen
        Assert.Equal(12, ChooseZoom(view, Projection.Degrees, 256, 12));   // the source's limit
        var world = new SketchView(0, 0, 1e-9);
        Assert.Equal(0, ChooseZoom(world, Projection.Mercator, 256, 19));
    }

    [Fact]
    public void Visible_tiles_cover_the_window_edge_to_edge()
    {
        var size = new Vector2(800, 600);
        var cos = Math.Cos(35.6812 * Math.PI / 180);
        var view = new SketchView(139.7671, 35.6812, 46603.0 / cos, cos);
        var spots = Visible(view, size, Projection.Degrees, 16);
        Assert.InRange(spots.Count, 12, MaxTiles);
        Assert.All(spots, s => Assert.Equal(16, s.Zoom));
        Assert.True(spots.Min(s => s.TopLeft.X) <= 0 && spots.Max(s => s.BottomRight.X) >= size.X);
        Assert.True(spots.Min(s => s.TopLeft.Y) <= 0 && spots.Max(s => s.BottomRight.Y) >= size.Y);
        // Neighbours share an edge: no gap, no overlap.
        var a = spots.First(s => spots.Any(t => t.X == s.X + 1 && t.Y == s.Y));
        var b = spots.First(t => t.X == a.X + 1 && t.Y == a.Y);
        Assert.Equal(a.BottomRight.X, b.TopLeft.X, 3);
        // About 256 px wide.
        Assert.InRange(a.BottomRight.X - a.TopLeft.X, 250, 262);
    }

    [Fact]
    public void Too_many_tiles_falls_back_to_a_coarser_zoom()
    {
        var size = new Vector2(4000, 3000);
        var view = new SketchView(0, 0, 46603.0);
        var spots = Visible(view, size, Projection.Degrees, 18);
        Assert.InRange(spots.Count, 1, MaxTiles);
        Assert.True(spots[0].Zoom < 18);
    }

    [Fact]
    public void Nothing_without_a_projection_or_a_window()
    {
        var view = new SketchView(0, 0, 100);
        Assert.Empty(Visible(view, new Vector2(800, 600), Projection.None, 5));
        Assert.Empty(Visible(view, Vector2.Zero, Projection.Degrees, 5));
    }

    sealed class FakeMap : IBasemap
    {
        public string Attribution => "© test";
        public int MaxZoom => 19;
        public byte[]? TryGetTile(int zoom, int x, int y) => null;
    }

    [Fact]
    public void The_provider_is_whatever_is_registered_and_none_by_default()
    {
        Assert.Null(Provider);   // no VL.Mapsui in a test run
        var fake = new FakeMap();
        ProviderOverride = fake;
        try { Assert.Same(fake, Provider); }
        finally { ProviderOverride = null; }
    }
}
