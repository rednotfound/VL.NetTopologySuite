using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// What a drawn feature shows of its attributes (2026-11-15). The editor draws the rows of a
/// collection only when the whole view fits the IOBox, so a feature inside a collection has to be
/// short: a few lines of text beside the geometry. Alone, it lists every attribute under it.
/// </summary>
public class FeatureRowsTests
{
    private const int Alone = FeatureRowsNode.FirstNestedDepth - 1;
    private const int InCollection = FeatureRowsNode.FirstNestedDepth;

    private static ImmutableDictionary<string, object> Attributes(int count, bool withName = false)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        for (var i = 0; i < count; i++) builder[$"key{i}"] = $"v{i}";
        if (withName) builder["name"] = "Tokyo Station";
        return builder.ToImmutable();
    }

    private static string[] Lines(string summary) => summary.Split('\n');

    [Fact]
    public void Alone_a_feature_shows_every_attribute_as_a_row_and_no_summary()
    {
        var (rows, summary) = FeatureRowsNode.Choose(Attributes(12), Alone);
        Assert.Equal(12, rows.Count);
        Assert.Equal("", summary);
    }

    [Fact]
    public void In_a_collection_a_feature_shows_a_few_lines_and_counts_the_rest()
    {
        var (rows, summary) = FeatureRowsNode.Choose(Attributes(12), InCollection);
        Assert.Empty(rows);
        var lines = Lines(summary);
        Assert.Equal(FeatureRowsNode.MaxSummaryLines + 1, lines.Length);
        Assert.Equal("+ 9 more", lines[^1]);
    }

    [Fact]
    public void In_a_collection_a_feature_with_few_attributes_shows_them_all_and_says_nothing_more()
    {
        var (_, summary) = FeatureRowsNode.Choose(Attributes(2), InCollection);
        var lines = Lines(summary);
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("more"));
        Assert.Contains("key0: v0", lines);
    }

    [Fact]
    public void The_name_comes_first_alone_and_in_a_collection()
    {
        var attributes = Attributes(12, withName: true);
        Assert.Equal("name", FeatureRowsNode.Choose(attributes, Alone).Rows[0].Key);
        Assert.Equal("name: Tokyo Station", Lines(FeatureRowsNode.Choose(attributes, InCollection).Summary)[0]);
    }

    [Fact]
    public void A_long_or_multiline_value_stays_one_short_line()
    {
        var attributes = ImmutableDictionary<string, object>.Empty
            .Add("note", "first line\nsecond line " + new string('x', 200))
            .Add("height", 35);
        var lines = Lines(FeatureRowsNode.Choose(attributes, InCollection).Summary);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.True(l.Length <= FeatureRowsNode.MaxLineLength));
        Assert.Contains("height: 35", lines);
        Assert.EndsWith("…", lines.Single(l => l.StartsWith("note")));
    }

    [Theory]
    [InlineData(Alone)]
    [InlineData(InCollection)]
    public void No_attributes_give_no_rows_and_no_summary(int depth)
    {
        Assert.Equal((0, ""), (FeatureRowsNode.Choose(null, depth).Rows.Count, FeatureRowsNode.Choose(null, depth).Summary));
        var empty = FeatureRowsNode.Choose(ImmutableDictionary<string, object>.Empty, depth);
        Assert.Empty(empty.Rows);
        Assert.Equal("", empty.Summary);
    }

    [Fact]
    public void The_node_gives_the_same_rows_back_while_nothing_changes()
    {
        var node = new FeatureRowsNode();
        var attributes = Attributes(5);
        var first = node.Update(attributes, Alone, out _);
        var second = node.Update(attributes, Alone, out var summary);
        Assert.Same(first, second);
        Assert.Equal(5, second.Count);
        Assert.Equal("", summary);

        var nested = node.Update(attributes, InCollection, out summary);
        Assert.Empty(nested);
        Assert.Equal("+ 2 more", Lines(summary)[^1]);
    }
}
