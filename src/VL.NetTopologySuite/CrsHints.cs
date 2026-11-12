namespace VL.NetTopologySuite;

// What GeometryEditor's window needs to know about an SRID to draw it honestly: are its numbers
// degrees of longitude/latitude (then x is drawn shrunk by cos(latitude), so shapes have their true
// proportions) or metres, or unknown. 2026-11-12, step 1 of the SRID plan agreed with the maintainer.
//
// This package references nothing of ours and carries no CRS database (an SRID is a label here;
// transformation and the EPSG catalogue are VL.PROJ's). So this is a SHORT list of the codes people
// meet - the CRS research's table, vl-proj/docs/CRS-RESEARCH.md - and everything else is "unknown",
// said as such, never guessed. Step 2 lets VL.PROJ, when installed, answer for every code through a
// service it registers (the AssemblyInitializer pattern of VL.IO.Redis, VL.Skia, VL.Stride).

/// <summary>What a common EPSG code's coordinates are, without a CRS database.</summary>
internal static class CrsHints
{
    /// <summary>The unit of a CRS's coordinates.</summary>
    public enum Units { Unknown, Degrees, Metres }

    // Geographic (longitude/latitude in degrees): WGS 84 (2D, 3D), ETRS89, NAD27, NAD83, GDA94,
    // GDA2020, CGCS2000, JGD2000, JGD2011/2024, Amersfoort, OSGB36, ED50, RGF93, SIRGAS 2000, Xian 1980,
    // Beijing 1954, Tokyo.
    private static readonly int[] Geographic =
    {
        4326, 4979, 4258, 4267, 4269, 4283, 7844, 4490, 4612, 6668, 4289, 4277, 4230, 4171, 4674, 4610,
        4214, 4301,
    };

    public static Units UnitsOf(int srid)
    {
        if (srid <= 0) return Units.Unknown;
        if (System.Array.IndexOf(Geographic, srid) >= 0) return Units.Degrees;
        return IsProjectedInMetres(srid) ? Units.Metres : Units.Unknown;
    }

    // Projected, in metres: Web Mercator (and its old 900913), World Mercator, RD New (+ NAP),
    // Lambert-93, British National Grid, LAEA/LCC Europe, ETRS89 and WGS 84 UTM zones, Japan Plane
    // Rectangular I-XIX, CGCS2000 Gauss-Krüger 3-degree zones, DHDN Gauss-Krüger.
    private static bool IsProjectedInMetres(int srid) => srid switch
    {
        3857 or 900913 or 3395 or 28992 or 7415 or 2154 or 27700 or 3035 or 3034 => true,
        >= 25828 and <= 25838 => true,   // ETRS89 / UTM zones 28N-38N
        >= 32601 and <= 32660 => true,   // WGS 84 / UTM north
        >= 32701 and <= 32760 => true,   // WGS 84 / UTM south
        >= 6669 and <= 6687 => true,     // JGD2011 / Japan Plane Rectangular CS I-XIX
        >= 4513 and <= 4554 => true,     // CGCS2000 / 3-degree Gauss-Kruger
        >= 31466 and <= 31469 => true,   // DHDN / 3-degree Gauss-Kruger
        _ => false,
    };

    /// <summary>A short label for the status bar: "EPSG:28992 · metres", "SRID unset".</summary>
    public static string Label(int srid) => srid <= 0
        ? "SRID unset"
        : UnitsOf(srid) switch
        {
            Units.Degrees => $"EPSG:{srid} · degrees",
            Units.Metres => $"EPSG:{srid} · metres",
            _ => $"EPSG:{srid} · units unknown",
        };
}
