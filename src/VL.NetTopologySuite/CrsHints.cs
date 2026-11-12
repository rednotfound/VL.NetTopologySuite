using System;
using System.Collections.Concurrent;
using VL.Core;

namespace VL.NetTopologySuite;

// What GeometryEditor's window and the Geometry IOBox need to know about an SRID to draw it
// honestly: are its numbers degrees of longitude/latitude (then x is drawn shrunk by cos(latitude),
// so shapes have their true proportions) or metres, or something else, and what is it called.
//
// Two sources, the better first (2026-11-12, steps 1 and 2 of the SRID plan):
//   1. A registered ICrsCatalog - VL.PROJ's, PROJ's whole EPSG database - when one is loaded.
//      Asked once per code and catalogue; the answers are cached, a miss included.
//   2. A SHORT built-in list of the codes people meet (the CRS research's table, vl-proj/docs/
//      CRS-RESEARCH.md). Everything else is "unknown", said as such, never guessed.
// See ICrsCatalog.cs for why the catalogue is looked up in AppHost.Global.

/// <summary>What an EPSG code's coordinates are: from a registered catalogue, else a short built-in table.</summary>
internal static class CrsHints
{
    /// <summary>The unit of a CRS's coordinates.</summary>
    public enum Units { Unknown, Degrees, Metres, Other }

    /// <summary>What is known about one code.</summary>
    public readonly record struct Description(string? Name, Units Units, string UnitName);

    /// <summary>For tests: a catalogue used instead of the registered one.</summary>
    internal static ICrsCatalog? CatalogOverride { get; set; }

    private static readonly ConcurrentDictionary<(ICrsCatalog, int), Description?> FromCatalog = new();

    // The registered catalogue, or null. Outside vvvv (tests, an exported app without VL.PROJ)
    // there may be no global app host or no registration; that is not an error.
    private static ICrsCatalog? Catalog
    {
        get
        {
            if (CatalogOverride is { } c) return c;
            try { return AppHost.Global?.Services.GetService(typeof(ICrsCatalog)) as ICrsCatalog; }
            catch (Exception) { return null; }
        }
    }

    // Geographic (longitude/latitude in degrees): WGS 84 (2D, 3D), ETRS89, NAD27, NAD83, GDA94,
    // GDA2020, CGCS2000, JGD2000, JGD2011/2024, Amersfoort, OSGB36, ED50, RGF93, SIRGAS 2000, Xian 1980,
    // Beijing 1954, Tokyo.
    private static readonly int[] Geographic =
    {
        4326, 4979, 4258, 4267, 4269, 4283, 7844, 4490, 4612, 6668, 4289, 4277, 4230, 4171, 4674, 4610,
        4214, 4301,
    };

    /// <summary>What is known about <paramref name="srid"/>: the catalogue's answer, else the table's.</summary>
    public static Description Describe(int srid)
    {
        if (srid <= 0) return new(null, Units.Unknown, "");
        if (Catalog is { } catalog && FromCatalog.GetOrAdd((catalog, srid), Ask) is { } known)
            return known;
        if (Array.IndexOf(Geographic, srid) >= 0) return new(null, Units.Degrees, "degree");
        return IsProjectedInMetres(srid) ? new(null, Units.Metres, "metre") : new(null, Units.Unknown, "");
    }

    private static Description? Ask((ICrsCatalog Catalog, int Srid) key)
    {
        try
        {
            if (!key.Catalog.TryDescribe(key.Srid, out var name, out var geographic, out var unit)) return null;
            var units = geographic ? Units.Degrees
                : unit.StartsWith("metre", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("meter", StringComparison.OrdinalIgnoreCase) ? Units.Metres
                : unit.Length > 0 ? Units.Other
                : Units.Unknown;
            return new(string.IsNullOrWhiteSpace(name) ? null : name, units, unit);
        }
        catch (Exception)
        {
            return null; // a catalogue that throws knows nothing; the table answers
        }
    }

    public static Units UnitsOf(int srid) => Describe(srid).Units;

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

    /// <summary>
    /// A short label for the status bar: "EPSG:28992 Amersfoort / RD New · metres" with a catalogue,
    /// "EPSG:28992 · metres" from the table, "SRID unset".
    /// </summary>
    public static string Label(int srid, bool named = true)
    {
        if (srid <= 0) return "SRID unset";
        var d = Describe(srid);
        var name = d.Name is null || !named ? $"EPSG:{srid}" : $"EPSG:{srid} {d.Name}";
        return d.Units switch
        {
            Units.Degrees => $"{name} · degrees",
            Units.Metres => $"{name} · metres",
            Units.Other => $"{name} · {d.UnitName}",
            _ => $"{name} · units unknown",
        };
    }
}
