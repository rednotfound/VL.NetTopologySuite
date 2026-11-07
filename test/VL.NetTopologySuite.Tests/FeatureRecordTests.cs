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
/// The record bridge: a user's own record, holding one or more Geometry fields, is a feature - the
/// door picks which field is the shape - and comes back out of one.
/// </summary>
/// <remarks>
/// A patched record cannot be built without the vvvv runtime (AppHost is null here), so the IVLObject
/// path runs against a double shaped the way the EDITOR shapes a record: values in a private state,
/// no public CLR member per property, everything reachable only through IVLObject.Type.Properties.
/// The double is copied from vl-mapsui's ToFeaturesTests (2026-08-15), where a double with real
/// public fields had let CLR reflection pass a test while the patch drew nothing. It covers our
/// field selection and conversion, not VL's reflection; the latter only a vvvv round can prove.
/// </remarks>
public class FeatureRecordTests
{
    static Point Tokyo() => GeometryNodes.Point(GeometryNodes.Coordinate(139.7671, 35.6812));
    static Point Ueno() => GeometryNodes.Point(GeometryNodes.Coordinate(139.7774, 35.7141));

    // ── Feature (FromRecord) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_record_with_one_Geometry_field_is_a_feature_whose_other_fields_are_the_attributes()
    {
        var record = new Station(Tokyo(), "Tokyo Station", 28, true);

        var feature = FeatureNodes.FeatureFromRecord(record, out var success, out var status);

        Assert.True(success);
        FeatureNodes.Split(feature, out var geometry, out var attributes);
        Assert.Equal(Tokyo(), geometry);
        Assert.Equal("Tokyo Station", attributes["Name"]);
        Assert.Equal(28, attributes["Platforms"]);
        Assert.Equal(true, attributes["Is Terminal"]);   // "Is Terminal" is OriginalName, Is_Terminal the C# name
        Assert.Equal(3, attributes.Count);
        Assert.False(attributes.ContainsKey("Geometry"));
        Assert.Equal("Station: Geometry is the shape, 3 attributes", status);
    }

    [Fact]
    public void A_record_without_a_Geometry_field_is_not_a_feature_and_Status_lists_what_it_has()
    {
        var feature = FeatureNodes.FeatureFromRecord(new NoGeometry(), out var success, out var status);

        Assert.False(success);
        Assert.Equal(0, feature.Attributes.Count);
        Assert.Equal("NoGeometry is not a feature: it needs a field of type Geometry, and it has Name: String, Platforms: Int32", status);
    }

    [Fact]
    public void A_record_with_two_Geometry_fields_and_no_name_is_refused_with_the_candidates_listed()
    {
        var feature = FeatureNodes.FeatureFromRecord(new TwoGeometries(), out var success, out var status);

        Assert.False(success);
        Assert.Equal(0, feature.Attributes.Count);
        Assert.Equal("TwoGeometries has 2 fields of type Geometry (Shape, Outline): name the shape in Geometry Field", status);
    }

    [Fact]
    public void Geometry_Field_names_the_shape_and_the_other_geometries_become_attributes()
    {
        var building = new Building(Tokyo().Buffer(0.001), Ueno(), "Marunouchi");

        var feature = FeatureNodes.FeatureFromRecord(building, out var success, out var status, "Entrance");

        Assert.True(success);
        Assert.Equal(Ueno(), feature.Geometry);
        Assert.Equal(building.State.Outline, feature.Attributes["Outline"]);
        Assert.Equal("Marunouchi", feature.Attributes["Name"]);
        Assert.Equal(2, feature.Attributes.Count);
        Assert.Equal("Building: Entrance is the shape, 2 attributes", status);
    }

    [Fact]
    public void Geometry_Field_is_matched_ignoring_case_and_surrounding_spaces()
    {
        var feature = FeatureNodes.FeatureFromRecord(new Building(Tokyo().Buffer(0.001), Ueno(), "x"), out var success, out _, " outline ");

        Assert.True(success);
        Assert.IsType<Polygon>(feature.Geometry);
        Assert.Equal(Ueno(), feature.Attributes["Entrance"]);
    }

    [Fact]
    public void Geometry_Field_naming_a_field_the_record_does_not_have_is_refused_with_the_candidates()
    {
        var feature = FeatureNodes.FeatureFromRecord(new Building(null, null, "x"), out var success, out var status, "Centre");

        Assert.False(success);
        Assert.Equal(0, feature.Attributes.Count);
        Assert.Equal("Building has no field named Centre; its Geometry fields are Outline, Entrance", status);
    }

    [Fact]
    public void Geometry_Field_naming_a_field_that_is_not_a_geometry_is_refused()
    {
        FeatureNodes.FeatureFromRecord(new Building(null, null, "x"), out var success, out var status, "Name");

        Assert.False(success);
        Assert.Equal("Building: Name is a String, not a Geometry; its Geometry fields are Outline, Entrance", status);
    }

    [Fact]
    public void Geometry_Field_also_works_on_a_record_with_one_Geometry_field()
    {
        var feature = FeatureNodes.FeatureFromRecord(new Station(Tokyo(), "Tokyo Station", 28, true), out var success, out _, "Geometry");

        Assert.True(success);
        Assert.Equal(Tokyo(), feature.Geometry);
    }

    [Fact]
    public void A_Point_typed_field_counts_as_the_Geometry_field()
    {
        var feature = FeatureNodes.FeatureFromRecord(new PlainStation { Location = Tokyo(), Name = "Shinjuku", Platforms = 16 }, out var success, out _);

        Assert.True(success);
        Assert.Equal(Tokyo(), feature.Geometry);
        Assert.Equal("Shinjuku", feature.Attributes["Name"]);
        Assert.Equal(16, feature.Attributes["Platform Count"]);   // VL's own [Name] applied on the reflection path
    }

    [Fact]
    public void An_empty_Geometry_field_still_gives_a_feature_and_Status_says_so()
    {
        var feature = FeatureNodes.FeatureFromRecord(new Station(null, "Nowhere", 0, false), out var success, out var status);

        Assert.True(success);
        Assert.Null(feature.Geometry);
        Assert.EndsWith("(Geometry is empty)", status);
    }

    [Fact]
    public void A_managed_property_is_not_an_attribute()
    {
        var feature = FeatureNodes.FeatureFromRecord(new Station(Tokyo(), "Tokyo Station", 28, true, withManaged: true), out _, out _);

        Assert.False(feature.Attributes.Exists("__Program__"));
    }

    [Fact]
    public void No_record_gives_an_empty_feature()
    {
        var feature = FeatureNodes.FeatureFromRecord<Station>(null, out var success, out var status);

        Assert.False(success);
        Assert.Equal(0, feature.Attributes.Count);
        Assert.Equal("no record connected", status);
    }

    // ── Record (FromFeature) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_feature_fills_a_template_record_shape_and_fields()
    {
        var feature = FeatureNodes.Feature(Ueno(), Attributes(("Name", "Ueno"), ("Platforms", 17), ("Is Terminal", false)));

        var record = FeatureNodes.RecordFromFeature(feature, new Station(null, "", 0, true), out var success, out var status);

        Assert.True(success);
        Assert.Equal(Ueno(), record.State.Geometry);
        Assert.Equal(("Ueno", 17, false), (record.State.Name, record.State.Platforms, record.State.Terminal));
        Assert.Equal("4 of 4 fields filled from Station", status);
    }

    [Fact]
    public void Filling_never_mutates_the_template()
    {
        var template = new Station(null, "", 0, false);
        var feature = FeatureNodes.Feature(Ueno(), Attributes(("Name", "Ueno")));

        FeatureNodes.RecordFromFeature(feature, template, out _, out _);

        Assert.Null(template.State.Geometry);
        Assert.Equal("", template.State.Name);
    }

    [Fact]
    public void An_Int64_attribute_fills_an_Integer32_field_as_the_GeoJSON_reader_stores_integers()
    {
        var feature = FeatureNodes.Feature(Ueno(), Attributes(("Name", "Ueno"), ("Platforms", 17L), ("Is Terminal", false)));

        var record = FeatureNodes.RecordFromFeature(feature, new Station(null, "", 0, false), out var success, out _);

        Assert.True(success);
        Assert.Equal(17, record.State.Platforms);
    }

    [Fact]
    public void A_missing_attribute_or_geometry_keeps_the_template_value_and_is_named_in_Status()
    {
        var feature = FeatureNodes.Feature(null, Attributes(("Name", "Ueno")));

        var record = FeatureNodes.RecordFromFeature(feature, new Station(Tokyo(), "", 7, true), out var success, out var status);

        Assert.False(success);
        Assert.Equal(Tokyo(), record.State.Geometry);
        Assert.Equal(("Ueno", 7, true), (record.State.Name, record.State.Platforms, record.State.Terminal));
        Assert.Equal("1 of 4 fields filled from Station; missing: Geometry, Platforms, Is Terminal", status);
    }

    [Fact]
    public void A_value_that_cannot_be_converted_keeps_the_template_value_and_is_named_in_Status()
    {
        var feature = FeatureNodes.Feature(Ueno(), Attributes(("Name", "Ueno"), ("Platforms", "many"), ("Is Terminal", false)));

        var record = FeatureNodes.RecordFromFeature(feature, new Station(null, "", 7, true), out var success, out var status);

        Assert.False(success);
        Assert.Equal(7, record.State.Platforms);
        Assert.Contains("not converted: Platforms (String to Int32)", status);
    }

    [Fact]
    public void A_template_without_a_Geometry_field_is_returned_untouched_with_the_reason()
    {
        var feature = FeatureNodes.Feature(Ueno(), Attributes(("Name", "Ueno")));
        var template = new NoGeometry();

        var record = FeatureNodes.RecordFromFeature(feature, template, out var success, out var status);

        Assert.False(success);
        Assert.Same(template, record);
        Assert.StartsWith("NoGeometry is not a feature", status);
    }

    [Fact]
    public void A_plain_dotnet_template_is_filled_on_a_copy()
    {
        var template = new PlainStation { Name = "", Platforms = 0 };
        var feature = FeatureNodes.Feature(Tokyo(), Attributes(("Name", "Shinjuku"), ("Platform Count", 16)));

        var record = FeatureNodes.RecordFromFeature(feature, template, out var success, out _);

        Assert.True(success);
        Assert.Equal(Tokyo(), record.Location);
        Assert.Equal("Shinjuku", record.Name);
        Assert.Equal(16, record.Platforms);
        Assert.Null(template.Location);
        Assert.Equal("", template.Name);
    }

    [Fact]
    public void A_template_with_two_Geometry_fields_needs_Geometry_Field_too()
    {
        var feature = FeatureNodes.Feature(Ueno(), Attributes(("Name", "x")));
        var template = new Building(null, null, "");

        var record = FeatureNodes.RecordFromFeature(feature, template, out var success, out var status);

        Assert.False(success);
        Assert.Same(template, record);
        Assert.Equal("Building has 2 fields of type Geometry (Outline, Entrance): name the shape in Geometry Field", status);
    }

    [Fact]
    public void Geometry_Field_puts_the_shape_in_the_named_field_and_the_other_geometries_come_from_the_attributes()
    {
        var outline = Tokyo().Buffer(0.001);
        var feature = FeatureNodes.Feature(Ueno(), Attributes(("Outline", outline), ("Name", "Marunouchi")));

        var record = FeatureNodes.RecordFromFeature(feature, new Building(null, null, ""), out var success, out var status, "Entrance");

        Assert.True(success);
        Assert.Equal(Ueno(), record.State.Entrance);
        Assert.Equal(outline, record.State.Outline);
        Assert.Equal("Marunouchi", record.State.Name);
        Assert.Equal("3 of 3 fields filled from Building", status);
    }

    [Fact]
    public void Record_undoes_Feature_with_two_geometries()
    {
        var original = new Building(Tokyo().Buffer(0.001), Ueno(), "Marunouchi");

        var feature = FeatureNodes.FeatureFromRecord(original, out _, out _, "Outline");
        var back = FeatureNodes.RecordFromFeature(feature, new Building(null, null, ""), out var success, out _, "Outline");

        Assert.True(success);
        Assert.Equal(original.State, back.State);
    }

    [Fact]
    public void Record_undoes_Feature()
    {
        var original = new Station(Tokyo(), "Tokyo Station", 28, true);

        var feature = FeatureNodes.FeatureFromRecord(original, out _, out _);
        var back = FeatureNodes.RecordFromFeature(feature, new Station(null, "", 0, false), out var success, out _);

        Assert.True(success);
        Assert.Equal(original.State, back.State);
    }

    static ImmutableDictionary<string, object> Attributes(params (string key, object value)[] pairs)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var (key, value) in pairs) builder[key] = value;
        return builder.ToImmutable();
    }

    /// <summary>An imported .NET type, as the reflection fallback sees it: a Point field is the geometry, VL's own [Name] on one member.</summary>
    public sealed class PlainStation
    {
        public Point? Location;
        public string Name = "";
        [VL.Core.Import.Name("Platform Count")]
        public int Platforms { get; set; }
    }

    /// <summary>
    /// A record shaped the way the vvvv EDITOR shapes one: its properties are invisible to CLR
    /// reflection, reachable only through IVLObject.Type.Properties. WithValue returns a new instance.
    /// </summary>
    sealed class Station : IVLObject
    {
        readonly bool _withManaged;
        public (Geometry? Geometry, string Name, int Platforms, bool Terminal) State { get; }

        public Station(Geometry? geometry, string name, int platforms, bool terminal, bool withManaged = false)
        {
            State = (geometry, name, platforms, terminal);
            _withManaged = withManaged;
        }

        Station With((Geometry? Geometry, string Name, int Platforms, bool Terminal) state) => new(state.Geometry, state.Name, state.Platforms, state.Terminal, _withManaged);

        // The only public CLR members, mirroring what the editor actually exposes.
        public object __State => State;
        public uint Identity => 1;

        public IVLTypeInfo Type
        {
            get
            {
                var properties = new List<IVLPropertyInfo>
                {
                    new FakeProperty("Geometry", "Geometry", typeof(Geometry), o => ((Station)o).State.Geometry,
                        (o, v) => ((Station)o).With(((Station)o).State with { Geometry = (Geometry?)v })),
                    new FakeProperty("Name", "Name", typeof(string), o => ((Station)o).State.Name,
                        (o, v) => ((Station)o).With(((Station)o).State with { Name = (string)v! })),
                    new FakeProperty("Platforms", "Platforms", typeof(int), o => ((Station)o).State.Platforms,
                        (o, v) => ((Station)o).With(((Station)o).State with { Platforms = (int)v! })),
                    // What a patch property called "Is Terminal" compiles to.
                    new FakeProperty("Is_Terminal", "Is Terminal", typeof(bool), o => ((Station)o).State.Terminal,
                        (o, v) => ((Station)o).With(((Station)o).State with { Terminal = (bool)v! })),
                };
                if (_withManaged)
                    properties.Add(new FakeProperty("__Program__", "__Program__", typeof(object), _ => null, (o, _) => o, managed: true));
                return new FakeType("Station", typeof(Station), properties.ToArray());
            }
        }

        public ServiceRegistry Services => throw new NotSupportedException();
        public AppHost AppHost => throw new NotSupportedException();
        public NodeContext Context => throw new NotSupportedException();
        public IVLObject With(IReadOnlyDictionary<string, object> values) => throw new NotSupportedException();
        public object ReadProperty(string key) => throw new NotSupportedException();
    }

    /// <summary>A record with no Geometry field: not a feature.</summary>
    sealed class NoGeometry : IVLObject
    {
        public object __State => 0;
        public uint Identity => 2;
        public IVLTypeInfo Type => new FakeType("NoGeometry", typeof(NoGeometry),
            new FakeProperty("Name", "Name", typeof(string), _ => "x", (o, _) => o),
            new FakeProperty("Platforms", "Platforms", typeof(int), _ => 1, (o, _) => o));
        public ServiceRegistry Services => throw new NotSupportedException();
        public AppHost AppHost => throw new NotSupportedException();
        public NodeContext Context => throw new NotSupportedException();
        public IVLObject With(IReadOnlyDictionary<string, object> values) => throw new NotSupportedException();
        public object ReadProperty(string key) => throw new NotSupportedException();
    }

    /// <summary>
    /// A record with two Geometry fields and a name, editor-shaped like Station: a building with
    /// its outline and its entrance. Which one is the shape is the door's choice.
    /// </summary>
    sealed class Building : IVLObject
    {
        public (Geometry? Outline, Point? Entrance, string Name) State { get; }

        public Building(Geometry? outline, Point? entrance, string name) => State = (outline, entrance, name);

        Building With((Geometry? Outline, Point? Entrance, string Name) state) => new(state.Outline, state.Entrance, state.Name);

        public object __State => State;
        public uint Identity => 4;
        public IVLTypeInfo Type => new FakeType("Building", typeof(Building),
            new FakeProperty("Outline", "Outline", typeof(Geometry), o => ((Building)o).State.Outline,
                (o, v) => ((Building)o).With(((Building)o).State with { Outline = (Geometry?)v })),
            new FakeProperty("Entrance", "Entrance", typeof(Point), o => ((Building)o).State.Entrance,
                (o, v) => ((Building)o).With(((Building)o).State with { Entrance = (Point?)v })),
            new FakeProperty("Name", "Name", typeof(string), o => ((Building)o).State.Name,
                (o, v) => ((Building)o).With(((Building)o).State with { Name = (string)v! })));
        public ServiceRegistry Services => throw new NotSupportedException();
        public AppHost AppHost => throw new NotSupportedException();
        public NodeContext Context => throw new NotSupportedException();
        public IVLObject With(IReadOnlyDictionary<string, object> values) => throw new NotSupportedException();
        public object ReadProperty(string key) => throw new NotSupportedException();
    }

    /// <summary>A record with two Geometry fields and nothing else, both empty: the refusal message's subject.</summary>
    sealed class TwoGeometries : IVLObject
    {
        public object __State => 0;
        public uint Identity => 3;
        public IVLTypeInfo Type => new FakeType("TwoGeometries", typeof(TwoGeometries),
            new FakeProperty("Shape", "Shape", typeof(Polygon), _ => null, (o, _) => o),
            new FakeProperty("Outline", "Outline", typeof(LineString), _ => null, (o, _) => o));
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
        public FakeType(string name, Type clrType, params IVLPropertyInfo[] properties)
        {
            Name = name;
            ClrType = clrType;
            Properties = properties.ToSpread();
        }

        public Spread<IVLPropertyInfo> Properties { get; }
        public Spread<IVLPropertyInfo> AllProperties => Properties;
        public IVLPropertyInfo? GetProperty(string name) =>
            Properties.FirstOrDefault(p => p.OriginalName == name || p.NameForTextualCode == name);

        public string Name { get; }
        public string Category => "Test";
        public string FullName => $"{Name} [Test]";
        public UniqueId Id => default;
        public Type ClrType { get; }
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
