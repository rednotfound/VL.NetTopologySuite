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

    // ── Attributes (Join) / Attributes (Split): the table-shaped way in and out ──────────────

    [Fact]
    public void Join_pairs_names_with_values_and_infers_their_types()
    {
        var attributes = FeatureNodes.AttributesJoin(
            new[] { "name", "platforms", "opened", "terminal", "height" },
            new[] { "Tokyo Station", "28", "1914", "true", "3.5" });

        Assert.Equal(5, attributes.Count);
        Assert.Equal("Tokyo Station", attributes["name"]);
        Assert.Equal(28, attributes["platforms"]);
        Assert.Equal(1914, attributes["opened"]);
        Assert.Equal(true, attributes["terminal"]);
        Assert.Equal(3.5, attributes["height"]);
    }

    [Fact]
    public void Join_without_inference_keeps_every_value_as_text()
    {
        var attributes = FeatureNodes.AttributesJoin(new[] { "platforms", "terminal" }, new[] { "28", "true" }, inferTypes: false);

        Assert.Equal("28", attributes["platforms"]);
        Assert.Equal("true", attributes["terminal"]);
    }

    [Fact]
    public void Join_reads_numbers_in_the_invariant_culture_and_leaves_what_is_not_a_number_as_text()
    {
        var attributes = FeatureNodes.AttributesJoin(
            new[] { "big", "thousands", "nan", "infinity", "exponent", "plus", "blank", "word" },
            new[] { "3000000000", "1,000", "NaN", "Infinity", "1e3", "+28", "", "yes" });

        Assert.Equal(3000000000L, attributes["big"]);
        Assert.Equal("1,000", attributes["thousands"]);
        Assert.Equal("NaN", attributes["nan"]);
        Assert.Equal("Infinity", attributes["infinity"]);
        Assert.Equal(1000.0, attributes["exponent"]);
        Assert.Equal(28, attributes["plus"]);
        Assert.Equal("", attributes["blank"]);
        Assert.Equal("yes", attributes["word"]);
    }

    [Fact]
    public void Join_pairs_rows_up_to_the_shorter_column_drops_empty_names_and_lets_a_later_duplicate_win()
    {
        var attributes = FeatureNodes.AttributesJoin(
            new[] { "a", "", "b", "a", "d" },
            new[] { "1", "2", "3", "4" });

        Assert.Equal(2, attributes.Count);
        Assert.Equal(4, attributes["a"]);
        Assert.Equal(3, attributes["b"]);
        Assert.False(attributes.ContainsKey("d"));
    }

    [Fact]
    public void Join_with_nothing_connected_is_empty()
    {
        Assert.Empty(FeatureNodes.AttributesJoin(null, null));
        Assert.Empty(FeatureNodes.AttributesJoin(new[] { "a" }, null));
    }

    [Fact]
    public void Split_lists_the_rows_sorted_by_name_as_text()
    {
        FeatureNodes.AttributesSplit(
            Attributes(("terminal", true), ("name", "Tokyo Station"), ("platforms", 28), ("height", 3.5)),
            out var keys, out var values);

        Assert.Equal(new[] { "height", "name", "platforms", "terminal" }, keys);
        Assert.Equal(new[] { "3.5", "Tokyo Station", "28", "True" }, values);
    }

    [Fact]
    public void Split_of_nothing_is_two_empty_columns()
    {
        FeatureNodes.AttributesSplit(null, out var keys, out var values);

        Assert.Empty(keys);
        Assert.Empty(values);
    }

    [Fact]
    public void Join_undoes_Split()
    {
        var original = Attributes(("name", "Tokyo Station"), ("platforms", 28), ("terminal", true), ("height", 3.5));

        FeatureNodes.AttributesSplit(original, out var keys, out var values);
        var back = FeatureNodes.AttributesJoin(keys, values);

        Assert.Equal(original, back);
    }

    [Fact]
    public void A_table_becomes_a_feature_whose_attribute_reads_back_typed()
    {
        var attributes = FeatureNodes.AttributesJoin(new[] { "name", "platforms" }, new[] { "Tokyo Station", "28" });
        var feature = FeatureNodes.Feature(GeometryNodes.Point(GeometryNodes.Coordinate(139.7671, 35.6812)), attributes);

        var platforms = FeatureNodes.GetAttribute(feature, "platforms", out var success);

        Assert.True(success);
        Assert.Equal(28, platforms);
    }
}
