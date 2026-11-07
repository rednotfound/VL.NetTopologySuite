using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetTopologySuite.Geometries;
using VL.Core;
using VL.Lib.Collections;
using Xunit;

namespace VL.NetTopologySuite.Tests;

/// <summary>
/// The record bridge: a user's own record becomes a feature's attributes and comes back out of one.
/// </summary>
/// <remarks>
/// A patched record cannot be built without the vvvv runtime (AppHost is null here), so the IVLObject
/// path runs against a double shaped the way the EDITOR shapes a record: values in a private state,
/// no public CLR member per property, everything reachable only through IVLObject.Type.Properties.
/// The double is copied from vl-mapsui's ToFeaturesTests (2026-08-15), where a double with real
/// public fields had let CLR reflection pass a test while the patch drew nothing. It covers our
/// key selection and conversion, not VL's reflection; the latter only a vvvv round can prove.
/// </remarks>
public class FeatureRecordTests
{
    static Point Tokyo() => GeometryNodes.Point(GeometryNodes.Coordinate(139.7671, 35.6812));

    // ── Feature (FromRecord) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_patched_record_becomes_attributes_keyed_by_the_names_the_patch_shows()
    {
        var record = new FakeRecord("Tokyo Station", 28, true);

        var feature = FeatureNodes.FeatureFromRecord(Tokyo(), record, out var status);

        FeatureNodes.Split(feature, out var geometry, out var attributes);
        Assert.Equal(Tokyo(), geometry);
        Assert.Equal("Tokyo Station", attributes["Name"]);
        Assert.Equal(28, attributes["Platforms"]);
        Assert.Equal(true, attributes["Is Terminal"]);   // "Is Terminal" is OriginalName, Is_Terminal the C# name
        Assert.Equal(3, attributes.Count);
        Assert.Equal("3 attributes from FakeRecord", status);
    }

    [Fact]
    public void A_managed_property_is_not_an_attribute()
    {
        var record = new FakeRecord("Tokyo Station", 28, true, withManaged: true);

        var feature = FeatureNodes.FeatureFromRecord(Tokyo(), record, out _);

        Assert.False(feature.Attributes.Exists("__Program__"));
    }

    [Fact]
    public void A_plain_dotnet_object_is_read_by_reflection_with_VL_names_applied()
    {
        var feature = FeatureNodes.FeatureFromRecord(Tokyo(), new PlainRecord { Name = "Shinjuku", Platforms = 16 }, out _);

        Assert.Equal("Shinjuku", feature.Attributes["Name"]);
        Assert.Equal(16, feature.Attributes["Platform Count"]);
    }

    [Fact]
    public void No_record_gives_a_feature_without_attributes()
    {
        var feature = FeatureNodes.FeatureFromRecord<FakeRecord>(Tokyo(), null, out var status);

        Assert.Equal(0, feature.Attributes.Count);
        Assert.Equal("no record connected", status);
    }

    // ── Record (FromFeature) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_feature_fills_a_template_record_field_by_field()
    {
        var feature = FeatureNodes.Feature(Tokyo(), Attributes(("Name", "Tokyo Station"), ("Platforms", 28), ("Is Terminal", true)));

        var record = FeatureNodes.RecordFromFeature(feature, new FakeRecord("", 0, false), out var geometry, out var success, out var status);

        Assert.True(success);
        Assert.Equal(Tokyo(), geometry);
        Assert.Equal(("Tokyo Station", 28, true), record.State);
        Assert.Equal("3 of 3 fields filled from FakeRecord", status);
    }

    [Fact]
    public void Filling_never_mutates_the_template()
    {
        var template = new FakeRecord("", 0, false);
        var feature = FeatureNodes.Feature(Tokyo(), Attributes(("Name", "Tokyo Station")));

        FeatureNodes.RecordFromFeature(feature, template, out _, out _, out _);

        Assert.Equal(("", 0, false), template.State);
    }

    [Fact]
    public void An_Int64_attribute_fills_an_Integer32_field_as_the_GeoJSON_reader_stores_integers()
    {
        var feature = FeatureNodes.Feature(Tokyo(), Attributes(("Name", "Ueno"), ("Platforms", 17L), ("Is Terminal", false)));

        var record = FeatureNodes.RecordFromFeature(feature, new FakeRecord("", 0, false), out _, out var success, out _);

        Assert.True(success);
        Assert.Equal(17, record.State.Platforms);
    }

    [Fact]
    public void A_missing_attribute_keeps_the_template_value_and_is_named_in_Status()
    {
        var feature = FeatureNodes.Feature(Tokyo(), Attributes(("Name", "Ueno")));

        var record = FeatureNodes.RecordFromFeature(feature, new FakeRecord("", 7, true), out _, out var success, out var status);

        Assert.False(success);
        Assert.Equal(("Ueno", 7, true), record.State);
        Assert.Equal("1 of 3 fields filled from FakeRecord; missing: Platforms, Is Terminal", status);
    }

    [Fact]
    public void A_value_that_cannot_be_converted_keeps_the_template_value_and_is_named_in_Status()
    {
        var feature = FeatureNodes.Feature(Tokyo(), Attributes(("Name", "Ueno"), ("Platforms", "many"), ("Is Terminal", false)));

        var record = FeatureNodes.RecordFromFeature(feature, new FakeRecord("", 7, true), out _, out var success, out var status);

        Assert.False(success);
        Assert.Equal(7, record.State.Platforms);
        Assert.Contains("not converted: Platforms (String to Int32)", status);
    }

    [Fact]
    public void A_plain_dotnet_template_is_filled_on_a_copy()
    {
        var template = new PlainRecord { Name = "", Platforms = 0 };
        var feature = FeatureNodes.Feature(Tokyo(), Attributes(("Name", "Shinjuku"), ("Platform Count", 16)));

        var record = FeatureNodes.RecordFromFeature(feature, template, out _, out var success, out _);

        Assert.True(success);
        Assert.Equal("Shinjuku", record.Name);
        Assert.Equal(16, record.Platforms);
        Assert.Equal("", template.Name);
    }

    [Fact]
    public void Record_undoes_Feature()
    {
        var original = new FakeRecord("Tokyo Station", 28, true);

        var feature = FeatureNodes.FeatureFromRecord(Tokyo(), original, out _);
        var back = FeatureNodes.RecordFromFeature(feature, new FakeRecord("", 0, false), out _, out var success, out _);

        Assert.True(success);
        Assert.Equal(original.State, back.State);
    }

    static ImmutableDictionary<string, object> Attributes(params (string key, object value)[] pairs)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var (key, value) in pairs) builder[key] = value;
        return builder.ToImmutable();
    }

    /// <summary>An imported .NET type, as the reflection fallback sees it, with VL's own [Name] on one member.</summary>
    public sealed class PlainRecord
    {
        public string Name = "";
        [VL.Core.Import.Name("Platform Count")]
        public int Platforms { get; set; }
    }

    /// <summary>
    /// A record shaped the way the vvvv EDITOR shapes one: its properties are invisible to CLR
    /// reflection, reachable only through IVLObject.Type.Properties. WithValue returns a new instance.
    /// </summary>
    sealed class FakeRecord : IVLObject
    {
        readonly bool _withManaged;
        public (string Name, int Platforms, bool Terminal) State { get; }

        public FakeRecord(string name, int platforms, bool terminal, bool withManaged = false)
        {
            State = (name, platforms, terminal);
            _withManaged = withManaged;
        }

        FakeRecord With((string Name, int Platforms, bool Terminal) state) => new(state.Name, state.Platforms, state.Terminal, _withManaged);

        // The only public CLR members, mirroring what the editor actually exposes.
        public object __State => State;
        public uint Identity => 1;

        public IVLTypeInfo Type
        {
            get
            {
                var properties = new List<IVLPropertyInfo>
                {
                    new FakeProperty("Name", "Name", typeof(string), o => ((FakeRecord)o).State.Name,
                        (o, v) => ((FakeRecord)o).With(((FakeRecord)o).State with { Name = (string)v! })),
                    new FakeProperty("Platforms", "Platforms", typeof(int), o => ((FakeRecord)o).State.Platforms,
                        (o, v) => ((FakeRecord)o).With(((FakeRecord)o).State with { Platforms = (int)v! })),
                    // What a patch property called "Is Terminal" compiles to.
                    new FakeProperty("Is_Terminal", "Is Terminal", typeof(bool), o => ((FakeRecord)o).State.Terminal,
                        (o, v) => ((FakeRecord)o).With(((FakeRecord)o).State with { Terminal = (bool)v! })),
                };
                if (_withManaged)
                    properties.Add(new FakeProperty("__Program__", "__Program__", typeof(object), _ => null, (o, _) => o, managed: true));
                return new FakeType(properties.ToArray());
            }
        }

        public ServiceRegistry Services => throw new NotSupportedException();
        public AppHost AppHost => throw new NotSupportedException();
        public NodeContext Context => throw new NotSupportedException();
        public IVLObject With(IReadOnlyDictionary<string, object> values) => throw new NotSupportedException();
        public object ReadProperty(string key) => throw new NotSupportedException();
    }

    sealed class FakeProperty : IVLPropertyInfo
    {
        readonly Func<object, object?> _read;
        readonly Func<object, object?, object> _write;

        public FakeProperty(string nameForTextualCode, string originalName, Type clrType, Func<object, object?> read, Func<object, object?, object> write, bool managed = false)
        {
            NameForTextualCode = nameForTextualCode;
            OriginalName = originalName;
            Type = new FakePropertyType(clrType);
            _read = read;
            _write = write;
            IsManaged = managed;
        }

        public string NameForTextualCode { get; }
        public string OriginalName { get; }
        public string Name => NameForTextualCode;
        public object GetValue(object instance) => _read(instance)!;
        public object WithValue(object instance, object? value) => _write(instance, value);

        public IVLTypeInfo DeclaringType => throw new NotSupportedException();
        public uint Id => 0;
        public IVLTypeInfo Type { get; }
        public bool IsManaged { get; }
        public bool ShouldBeSerialized => true;
        public Spread<Attribute> Attributes => Spread<Attribute>.Empty;
        public IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
            => Enumerable.Empty<TAttribute>();
    }

    /// <summary>The bare minimum a property's type has to answer: its CLR type and a name.</summary>
    sealed class FakePropertyType : IVLTypeInfo
    {
        public FakePropertyType(Type clrType) => ClrType = clrType;

        public Type ClrType { get; }
        public string Name => ClrType.Name;

        public Spread<IVLPropertyInfo> Properties => Spread<IVLPropertyInfo>.Empty;
        public Spread<IVLPropertyInfo> AllProperties => Properties;
        public IVLPropertyInfo? GetProperty(string name) => null;
        public string Category => "Test";
        public string FullName => Name;
        public UniqueId Id => default;
        public bool IsPatched => false;
        public bool IsClass => false;
        public bool IsRecord => false;
        public bool IsImmutable => true;
        public bool IsInterface => false;
        public string ToString(bool includeCategory) => Name;
        public object CreateInstance(NodeContext context, IReadOnlyDictionary<string, object?>? arguments = null)
            => throw new NotSupportedException();
        public object GetDefaultValue() => throw new NotSupportedException();
        public IVLTypeInfo MakeGenericType(IReadOnlyList<IVLTypeInfo> arguments) => throw new NotSupportedException();
        public Spread<Attribute> Attributes => Spread<Attribute>.Empty;
        public IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
            => Enumerable.Empty<TAttribute>();
    }

    sealed class FakeType : IVLTypeInfo
    {
        public FakeType(params IVLPropertyInfo[] properties) => Properties = properties.ToSpread();

        public Spread<IVLPropertyInfo> Properties { get; }
        public Spread<IVLPropertyInfo> AllProperties => Properties;
        public IVLPropertyInfo? GetProperty(string name) =>
            Properties.FirstOrDefault(p => p.OriginalName == name || p.NameForTextualCode == name);

        public string Name => "FakeRecord";
        public string Category => "Test";
        public string FullName => "FakeRecord [Test]";
        public UniqueId Id => default;
        public Type ClrType => typeof(FakeRecord);
        public bool IsPatched => true;
        public bool IsClass => false;
        public bool IsRecord => true;
        public bool IsImmutable => true;
        public bool IsInterface => false;
        public string ToString(bool includeCategory) => FullName;
        public object CreateInstance(NodeContext context, IReadOnlyDictionary<string, object?>? arguments = null)
            => throw new NotSupportedException();
        public object GetDefaultValue() => throw new NotSupportedException();
        public IVLTypeInfo MakeGenericType(IReadOnlyList<IVLTypeInfo> arguments) => throw new NotSupportedException();
        public Spread<Attribute> Attributes => Spread<Attribute>.Empty;
        public IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
            => Enumerable.Empty<TAttribute>();
    }
}
