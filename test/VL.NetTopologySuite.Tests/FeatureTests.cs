using System.Collections.Immutable;
using Xunit;

namespace VL.NTS.Tests;

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
}
