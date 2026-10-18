using System.Collections.Immutable;
using NetTopologySuite.Geometries;
using VL.Core.Import;

using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;

namespace VL.NetTopologySuite;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Category NetTopologySuite.Feature.
//
// A feature is a data-model concept, not a rendering one. ISO 19109 defines it as an abstraction of
// a real-world phenomenon; RFC 7946 (GeoJSON) spells the same thing as geometry + properties (+ id).
// Every geometry core in the field - JTS, GEOS/Shapely, NetTopologySuite - keeps features one level
// ABOVE the geometry types, and every renderer consumes or wraps them: Mapsui converts into its own
// GeometryFeature at its provider boundary, exactly as VL.Mapsui's FeatureLayer does.
//
// A feature has to be constructible without a map engine, which is why these nodes live here and
// not in a map package: VL.NetTopologySuite.GeoJSON reads and writes them, VL.Mapsui draws and
// picks them, and a patch can make one by hand - multiple producers and consumers, so the shared
// type belongs below all of them. That is also how the NTS team packages it upstream:
// NetTopologySuite.Features is their own companion package, sitting directly on the geometry core.
//
// The type is NetTopologySuite's Feature, not a wrapper and not one of ours. Geometry plus an
// attributes table, nothing about styles, layers or renderers - so a feature made here can be
// produced by something that has never heard of a map and consumed by something that draws one.

/// <summary>
/// Builds features from geometry and attributes, and opens them again.
/// </summary>
[Name("Feature")]
public static class FeatureNodes
{
    // Attributes are a plain Dictionary of string to object - VL's own Dictionary, which is an
    // ImmutableDictionary underneath. Unconnected gives a feature with no attributes, which is all
    // a shape needs to be drawn; attributes are what lets a click, a label or a file say something
    // ABOUT what it hit.
    //
    // The coordinates stay in whatever space the geometry is in. A feature does not make them
    // geographic - that is the geometry's SRID's claim to make, and this node does not touch it.
    //
    // A static operation, because a feature holds no resource and its identity is not compared
    // downstream the way a style's is - a layer compares the SET of features.

    /// <summary>
    /// Makes a feature: a geometry plus the attributes that describe it.
    /// </summary>
    /// <param name="geometry">The shape.</param>
    /// <param name="attributes">Names and values, for example "name" and "Tokyo". Unconnected gives a feature with no attributes.</param>
    /// <returns>The feature.</returns>
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

    // Every opaque value needs a reader node. NTS keeps attributes in an IAttributesTable, which VL
    // has no nodes for - so a feature arriving from a file reader or from VL.Mapsui's Pick would
    // carry the very thing that makes it interesting and no way to read it. This converts to VL's
    // own Dictionary, where TryGetValue is waiting.
    //
    // The exact inverse of Feature, and named Split because that is what the ecosystem calls taking
    // a value apart - this package splits a Coordinate the same way.
    //
    // An empty dictionary rather than nothing when a feature has no attributes: absence would have
    // to be checked before every lookup, and there is nothing to distinguish - a feature with no
    // attributes and a feature with an empty table say the same thing. An attribute whose stored
    // value is null is dropped for the same reason GetAttribute reports one as absent: a Dictionary
    // slot holding null answers every question worse than no slot at all.

    /// <summary>
    /// Takes a feature apart into its geometry and its attributes.
    /// </summary>
    /// <remarks>
    /// Attributes come out as a Dictionary, so TryGetValue works on them.
    /// Use GetAttribute to read just one.
    /// </remarks>
    /// <param name="feature">The feature to open.</param>
    /// <param name="geometry">The feature's shape.</param>
    /// <param name="attributes">All its attributes. Empty when it has none; attributes whose value is null are left out.</param>
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

    // Success is the whole point of this node. NTS's IAttributesTable indexer THROWS
    // ArgumentException for a name it does not have (measured) - and a node that throws sixty times
    // a second while somebody types a name into an IOBox is not a diagnostic, it is a broken patch.
    // So a missing name is reported, not raised, and the result is nothing.
    //
    // vvvv's own convention rather than an invention here: VL.Xml's GetAttributeValue, GetValue and
    // XPathGetValue all take an input plus a name and return a value plus Success. A Get... node
    // carries a Success pin and never throws.
    //
    // Split for ALL the attributes and this for ONE; the XML library ships the same pair,
    // XElement (Split) beside GetAttributeValue. The result is an Object because an attribute
    // genuinely can be anything - a feature read by VL.NetTopologySuite.GeoJSON carries String,
    // Int64, Float64 or Boolean - and which one is a fact about the data rather than about the node.
    //
    // An attribute whose stored value is null reports Success false, the same as one that is
    // absent, for the reason Split drops it: a patch cannot act on a null differently from a
    // missing value.

    /// <summary>
    /// Reads one attribute of a feature by name.
    /// </summary>
    /// <remarks>
    /// Success is false when the feature has no attribute of that name, or its value is null.
    /// Use Split to get all the attributes at once.
    /// </remarks>
    /// <param name="feature">The feature to read from.</param>
    /// <param name="name">The attribute's name, for example "name".</param>
    /// <param name="success">True when the attribute was found.</param>
    /// <returns>The attribute's value, as whatever type it was stored as. Nothing when not found.</returns>
    public static object? GetAttribute(NtsFeature? feature, string? name, out bool success)
    {
        // Exists() first, deliberately: the indexer is what throws, and asking before reading is
        // the only way to answer "is it there?" without catching an exception per frame.
        if (feature?.Attributes is { } table &&
            !string.IsNullOrEmpty(name) &&
            table.Exists(name) &&
            table[name] is { } value)
        {
            success = true;
            return value;
        }

        success = false;
        return null;
    }
}
