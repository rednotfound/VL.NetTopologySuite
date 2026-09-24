using System.Collections.Immutable;
using NetTopologySuite.Geometries;
using VL.Core.Import;

using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;

namespace VL.NTS;

/// <summary>
/// Geometry with attributes: the unit a spatial dataset is made of. Category <c>NTS.Feature</c>.
/// </summary>
/// <remarks>
/// <para><b>A feature is a data-model concept, not a rendering one.</b> ISO 19109 defines it as an
/// abstraction of a real-world phenomenon; RFC 7946 (GeoJSON) spells the same thing as geometry +
/// properties (+ id). Every geometry core in the field — JTS, GEOS/Shapely, NetTopologySuite —
/// keeps features one level <i>above</i> the geometry types, and every renderer consumes or wraps
/// them: Mapsui converts into its own <c>GeometryFeature</c> at its provider boundary, exactly as
/// VL.Mapsui's <c>FeatureLayer</c> does.</para>
/// <para><b>A feature has to be constructible without a map engine</b>, which is why these
/// nodes live here and not in a map package: VL.GeoJSON writes them, VL.Mapsui draws
/// and picks them, and a patch can make one by hand — multiple producers and consumers, so the
/// shared type belongs below all of them. That is also how the NTS team packages it upstream:
/// <c>NetTopologySuite.Features</c> is their own companion package, sitting directly on the
/// geometry core.</para>
/// <para><b>The type is NetTopologySuite's <c>Feature</c>, not a wrapper and not one of ours.</b>
/// Geometry plus an attributes table, nothing about styles, layers or renderers — so a feature made
/// here can be produced by something that has never heard of a map and consumed by something that
/// draws one.</para>
/// </remarks>
[Name("Feature")]
public static class FeatureNodes
{
    /// <summary>
    /// One feature: a geometry, plus whatever attributes describe it.
    /// Uses NetTopologySuite.Features.
    /// </summary>
    /// <remarks>
    /// <para>Attributes are a plain Dictionary of string to object — VL's own Dictionary, which is
    /// an ImmutableDictionary underneath. Leaving the pin unconnected gives a feature with no
    /// attributes, which is all a shape needs to be drawn; attributes are what lets a click, a
    /// label or a file say something <i>about</i> what it hit.</para>
    /// <para>The coordinates stay in whatever space the geometry is in. A feature does not make
    /// them geographic — that is the geometry's SRID's claim to make, and this node does not touch
    /// it.</para>
    /// <para>A static operation, because a feature holds no resource and its identity is not
    /// compared downstream the way a style's is — a layer compares the <i>set</i> of features.</para>
    /// </remarks>
    public static NtsFeature Feature(
        Geometry? geometry = null,
        ImmutableDictionary<string, object>? attributes = null)
    {
        var table = new AttributesTable();
        if (attributes is not null)
            foreach (var pair in attributes)
                table.Add(pair.Key, pair.Value);

        return new NtsFeature(geometry!, table);
    }

    /// <summary>
    /// A feature taken apart again: its geometry, and its attributes as a Dictionary.
    /// Uses NetTopologySuite.Features.
    /// </summary>
    /// <remarks>
    /// <para><b>Every opaque value needs a reader node.</b> NTS keeps attributes in an
    /// <c>IAttributesTable</c>, which VL has no nodes for — so a feature arriving from a file
    /// reader or from VL.Mapsui's <c>Pick</c> would carry the very thing that makes it interesting
    /// and no way to read it. This converts to VL's own Dictionary, where <c>TryGetValue</c> is
    /// waiting.</para>
    /// <para>The exact inverse of <see cref="Feature"/>, and named <c>Split</c> because that is
    /// what the ecosystem calls taking a value apart — this package splits a <c>Coordinate</c> the
    /// same way.</para>
    /// <para>An empty dictionary rather than nothing when a feature has no attributes: absence
    /// would have to be checked before every lookup, and there is nothing to distinguish — a
    /// feature with no attributes and a feature with an empty table say the same thing. An
    /// attribute whose stored value is null is dropped for the same reason VL.GeoJSON's
    /// <c>GetProperty</c> reports one as absent: a Dictionary slot holding null answers every
    /// question worse than no slot at all.</para>
    /// </remarks>
    public static void Split(
        NtsFeature? feature,
        out Geometry? geometry,
        out ImmutableDictionary<string, object> attributes)
    {
        geometry = feature?.Geometry;

        var builder = ImmutableDictionary.CreateBuilder<string, object>();

        if (feature?.Attributes is { } table)
            foreach (var name in table.GetNames())
                if (table[name] is { } value)
                    builder[name] = value;

        attributes = builder.ToImmutable();
    }
}
