using System.Collections.Immutable;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The feature boundary: geometry plus attributes in, and the same two back out.
/// </summary>
/// <remarks>
/// These tests moved here from VL.Mapsui on 2026-08-22, along with the nodes they cover. The
/// half of the old suite that needed a map — build a feature, draw it, pick it, read it — stayed
/// behind as a Pick test, and the cross-package version of that round trip is what
/// VL.Cartography's help-patch compile checks.
/// </remarks>
public class FeatureTests
{
    static ImmutableDictionary<string, object> Attributes(params (string key, object value)[] pairs)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var (key, value) in pairs) builder[key] = value;
        return builder.ToImmutable();
    }

    [Fact]
    public void Split_undoes_Feature()
    {
        var geometry = GeometryNodes.Point(GeometryNodes.Coordinate(135.7581, 34.9859));
        var feature = FeatureNodes.Feature(geometry, Attributes(("name", "Kyoto Station"), ("type", "station")));

        FeatureNodes.Split(feature, out var back, out var attributes);

        Assert.Equal(geometry, back);
        Assert.Equal("Kyoto Station", attributes["name"]);
        Assert.Equal("station", attributes["type"]);
        Assert.Equal(2, attributes.Count);
    }

    [Fact]
    public void A_feature_with_no_attributes_splits_into_an_empty_dictionary()
    {
        var feature = FeatureNodes.Feature(GeometryNodes.Point(GeometryNodes.Coordinate(0, 0)));

        FeatureNodes.Split(feature, out _, out var attributes);

        Assert.NotNull(attributes);
        Assert.Empty(attributes);
    }

    [Fact]
    public void Nothing_splits_into_nothing_rather_than_throwing()
    {
        FeatureNodes.Split(null, out var geometry, out var attributes);

        Assert.Null(geometry);
        Assert.Empty(attributes);
    }

    [Fact]
    public void The_geometry_is_carried_by_reference_not_copied()
    {
        // Creation nodes defensively copy coordinates on the way IN to a geometry; a feature is a
        // carrier, not a creator, and copying here would break the identity that VL.Mapsui's
        // FeatureLayer compares to decide whether to rebuild. Asserted so the difference stays a
        // decision rather than drifting into an accident.
        var geometry = GeometryNodes.Point(GeometryNodes.Coordinate(1, 2));

        var feature = FeatureNodes.Feature(geometry);

        Assert.Same(geometry, feature.Geometry);
    }

    // ------------------------------------------------------------ GetAttribute
    //
    // The node exists because IAttributesTable's indexer THROWS on a name it does not have, and a
    // node that throws sixty times a second while somebody types is not a diagnostic. Every test
    // below is really one test: does it report instead of raising.

    static global::NetTopologySuite.Features.Feature Station()
        => FeatureNodes.Feature(
            GeometryNodes.Point(GeometryNodes.Coordinate(135.7581, 34.9859)),
            Attributes(("name", "Kyoto Station"), ("platforms", 34L)));

    [Fact]
    public void GetAttribute_finds_an_attribute_and_reports_success()
    {
        var value = FeatureNodes.GetAttribute(Station(), "platforms", out var success);

        Assert.True(success);
        Assert.Equal(34L, value);
    }

    [Fact]
    public void GetAttribute_reports_a_missing_name_instead_of_throwing()
    {
        // The whole reason the node exists. Under the raw NTS indexer this line is an
        // ArgumentException; here it is a false on a pin.
        var value = FeatureNodes.GetAttribute(Station(), "no_such_attribute", out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void GetAttribute_treats_a_stored_null_as_not_found()
    {
        // Built on the raw table, because the Feature node's Dictionary is how a patch makes one
        // and a reader (VL.NetTopologySuite.GeoJSON, VL.Mapsui's Pick) is how a null arrives.
        var table = new global::NetTopologySuite.Features.AttributesTable { { "nickname", null } };
        var feature = new global::NetTopologySuite.Features.Feature(
            GeometryNodes.Point(GeometryNodes.Coordinate(0, 0)), table);

        var value = FeatureNodes.GetAttribute(feature, "nickname", out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetAttribute_survives_an_unconnected_or_empty_name(string? name)
    {
        // An IOBox somebody has not typed into yet is the normal starting state.
        var value = FeatureNodes.GetAttribute(Station(), name, out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void GetAttribute_survives_a_null_feature()
    {
        var value = FeatureNodes.GetAttribute(null, "name", out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void GetAttribute_agrees_with_Split_on_every_name()
    {
        // The two nodes answer different questions - take the lot, versus take one by name - but
        // they must never disagree about what is there. vvvv ships the same pair for XML
        // (XElement (Split) -> Attributes, and GetAttributeValue -> Value, Success).
        var station = Station();
        FeatureNodes.Split(station, out _, out var attributes);

        Assert.NotEmpty(attributes);
        foreach (var (key, expected) in attributes)
        {
            var value = FeatureNodes.GetAttribute(station, key, out var success);
            Assert.True(success, $"Split offered '{key}' but GetAttribute did not find it");
            Assert.Equal(expected, value);
        }
    }
}
