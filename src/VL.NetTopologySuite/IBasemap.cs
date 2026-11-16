using VL.Core;
using VL.Core.Import;

namespace VL.NetTopologySuite;

// Step 3 of the SRID plan (2026-11-16, asked for by the maintainer after using the editor on real
// data): a map under the drawing in GeometryEditor's window. This package fetches nothing and knows
// no tile server (it is about geometry, not maps), and it references nothing of ours. So, exactly as
// ICrsCatalog does for VL.PROJ, the window asks for a basemap through this interface and VL.Mapsui,
// when it is loaded, registers one in AppHost.Global's services: its tile source, its User-Agent and
// its disk cache, the same tiles its OpenStreetMap layer draws. Without VL.Mapsui the window keeps
// its dot grid. The maintainer's words for it: "按照 VL.PROJ 的做法 ... 不能打破依赖关系".
//
// Tiles are Web Mercator in the XYZ scheme every slippy map uses (x east from 180°W, y SOUTH from
// 85.05°N), handed over encoded (PNG or JPEG bytes) so neither side depends on the other's image
// type. TryGetTile never waits: the window draws every frame and a tile that is not at hand yet is
// requested and shows up a few frames later.
//
// Hidden smell: a type for package authors, not a node or a type to patch with.

/// <summary>A basemap's tiles, registered by a package that has a tile source (VL.Mapsui).</summary>
[Smell(SymbolSmell.Hidden)]
public interface IBasemap
{
    /// <summary>The credit the tiles must carry wherever they are shown.</summary>
    string Attribution { get; }

    /// <summary>The deepest zoom level the tile source has.</summary>
    int MaxZoom { get; }

    /// <summary>A tile's encoded image when it is at hand; otherwise asks for it and returns null. Never waits.</summary>
    /// <param name="zoom">The zoom level, 0 for the whole world in one tile.</param>
    /// <param name="x">The column, from 180 degrees west.</param>
    /// <param name="y">The row, from the north.</param>
    byte[]? TryGetTile(int zoom, int x, int y);
}
