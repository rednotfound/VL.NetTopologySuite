using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NetTopologySuite.Geometries;
using VL.Core;
using VL.Core.Import;

using NtsFeature = NetTopologySuite.Features.Feature;
using VLName = VL.Core.Import.NameAttribute;

namespace VL.NetTopologySuite;

// The /// text below is what vvvv shows on hover - short, one sentence per line. The reasoning
// lives here.
//
// ── The user's record IS the feature ──────────────────────────────────────────────────────────
//
// Branch record-bridge, 2026-11-07. The maintainer's definition, in two steps the same night:
// first "a GIS feature is a row of a table whose columns are defined first - in vvvv a Record",
// then "the record itself is the feature: it must hold one Geometry field, exactly one, and the
// rest is whatever describes it". So a record such as
//     Station { Geometry: Geometry, Name: String, Platforms: Integer32, Terminal: Boolean }
// is a feature, a Spread<Station> is a dataset, and these two nodes are the bridge to the NTS
// Feature that GeoJSON writes and Mapsui draws. The Dictionary and Attributes (Join)/(Split) on
// main stay as the schema-less layer underneath.
//
// The rule is the maintainer's and it is written on the node: EXACTLY ONE field of type Geometry.
// None, or two, and Status says which fields the record has, Success is false and nothing is
// guessed. vl-mapsui's ToFeatures (2026-08-15) picked the geometry by type too, and the
// maintainer removed it (2026-09-25) because that choice was a convention nobody could see; what
// changed is not the mechanism but that the rule is now theirs, stated, and loud when broken.
//
// Two more constraints paid for in vl-mapsui's NOTES.md:
// - A patched record is read through VL, never through System.Reflection. Inside the editor a
//   record has NO CLR members for its properties - its public surface is `__State, Context,
//   Identity, __Program__` - only IVLObject.Type.Properties sees them; vvvvc's export emits real
//   public fields, which is why reading the generated C# did not catch it. Keys are OriginalName
//   (the name the patch shows, spaces included); `Name` is [Obsolete] and says so. CLR reflection
//   stays as the fallback for imported .NET types and exported records.
// - A generic T pin, not an object pin: VL.Serialization.MessagePack, VL.ImGui.Editors and the
//   built-in Serialize all take the user's record that way and reflect inside.
//
// IsManaged properties (system generated) are skipped. The reader per type is cached: a thousand
// records are the same shape.
//
// Record (FromFeature) fills a TEMPLATE instance - the type argument has to come from somewhere,
// and a template also supplies the defaults for fields the feature does not carry. Each field is
// set with IVLPropertyInfo.WithValue, which returns "the instance with the newly set value"
// (VL.Core's words), i.e. a new record for an immutable one. Values are converted when the stored
// type differs from the field's (the GeoJSON reader stores every integer as Int64, a patch field
// is usually Integer32); a value that cannot be converted leaves the template's default and is
// named in Status. Nothing here needs an AppHost or a NodeContext, so the tests run without vvvv
// against a double shaped like the editor's record.
public static partial class FeatureNodes
{
    /// <summary>
    /// Makes a feature from your own record: its one Geometry field is the shape, every other field an attribute.
    /// </summary>
    /// <remarks>
    /// Define a record once (right-click the canvas, Record) with exactly one field of type Geometry and the fields a feature of that kind has.
    /// Without a Geometry field, or with two, nothing is guessed: Success is false and Status says what the record has.
    /// </remarks>
    /// <param name="record">A value of your record type. Unconnected gives an empty feature.</param>
    /// <param name="success">True when the record has exactly one Geometry field.</param>
    /// <param name="status">What was read, or why nothing was.</param>
    /// <returns>The feature.</returns>
    [Name("Feature (FromRecord)")]
    public static NtsFeature FeatureFromRecord<T>(T? record, out bool success, out string status)
    {
        if (record is null)
        {
            success = false;
            status = "no record connected";
            return Feature(null, null);
        }

        var reader = RecordReader.For(record, typeof(T));
        if (reader.Problem is { } problem)
        {
            success = false;
            status = problem;
            return Feature(null, null);
        }

        var geometry = reader.Geometry!.Read(record) as Geometry;
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var property in reader.Attributes)
            if (property.Read(record) is { } value)
                builder[property.Name] = value;

        success = true;
        status = $"{builder.Count} attributes and the geometry from {reader.TypeName}"
            + (geometry is null ? " (the Geometry field is empty)" : "");
        return Feature(geometry, builder.ToImmutable());
    }

    /// <summary>
    /// Fills your own record from a feature: the Geometry field takes the shape, each other field the attribute of the same name.
    /// </summary>
    /// <remarks>
    /// Template is a value of your record type, usually its Create node; fields the feature does not have keep the template's value.
    /// Success is false when the record has no single Geometry field, a field was missing, or a value could not be converted; Status says which.
    /// </remarks>
    /// <param name="feature">The feature to read.</param>
    /// <param name="template">A value of your record type: gives the type and the defaults.</param>
    /// <param name="success">True when every field was filled.</param>
    /// <param name="status">Which fields were filled, and which were not.</param>
    /// <returns>The record with the feature's shape and attributes.</returns>
    [Name("Record (FromFeature)")]
    public static T RecordFromFeature<T>(NtsFeature? feature, T template, out bool success, out string status)
    {
        if (template is null)
        {
            success = false;
            status = "no template connected";
            return template!;
        }

        var reader = RecordReader.For(template, typeof(T));
        if (reader.Problem is { } problem)
        {
            success = false;
            status = problem;
            return template;
        }

        object current = template;
        var filled = new List<string>();
        var missing = new List<string>();
        var unconvertible = new List<string>();

        if (feature?.Geometry is { } geometry)
        {
            current = reader.Geometry!.Write(current, geometry);
            filled.Add(reader.Geometry.Name);
        }
        else
        {
            missing.Add(reader.Geometry!.Name);
        }

        var table = feature?.Attributes;
        foreach (var property in reader.Attributes)
        {
            if (table is null || !table.Exists(property.Name) || table[property.Name] is not { } raw)
            {
                missing.Add(property.Name);
                continue;
            }

            if (!TryConvert(raw, property.Type, out var value))
            {
                unconvertible.Add($"{property.Name} ({raw.GetType().Name} to {property.Type.Name})");
                continue;
            }

            current = property.Write(current, value);
            filled.Add(property.Name);
        }

        success = missing.Count == 0 && unconvertible.Count == 0;
        status = $"{filled.Count} of {reader.Properties.Length} fields filled from {reader.TypeName}"
            + (missing.Count > 0 ? $"; missing: {string.Join(", ", missing)}" : "")
            + (unconvertible.Count > 0 ? $"; not converted: {string.Join(", ", unconvertible)}" : "");
        return (T)current;
    }

    // Int64 -> Int32, Int32 -> Float64, text -> anything IConvertible: the conversions a GeoJSON
    // or CSV round trip needs. Anything already of the field's type passes untouched.
    static bool TryConvert(object raw, Type target, out object? value)
    {
        if (target.IsInstanceOfType(raw))
        {
            value = raw;
            return true;
        }

        try
        {
            value = Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// How to read and write the fields of one record type, worked out once per type: which field
    /// is the geometry, which are attributes, or why the record is not a feature.
    /// </summary>
    sealed class RecordReader
    {
        static readonly ConcurrentDictionary<Type, RecordReader> Readers = new();

        public string TypeName { get; private init; } = "";
        public RecordProperty[] Properties { get; private init; } = Array.Empty<RecordProperty>();
        /// <summary>The one field of type Geometry, or null when Problem says why there is none.</summary>
        public RecordProperty? Geometry { get; private init; }
        public RecordProperty[] Attributes { get; private init; } = Array.Empty<RecordProperty>();
        /// <summary>Why this record is not a feature, or null if it is.</summary>
        public string? Problem { get; private init; }

        public sealed record RecordProperty(string Name, Type Type, Func<object, object?> Read, Func<object, object?, object> Write);

        // The reader comes from the VALUE, not from typeof(T): inside the editor the patched record
        // carries its own IVLObject.Type, and typeof(T) has no CLR members to reflect over.
        public static RecordReader For(object value, Type declared)
        {
            if (value is IVLObject patched)
                return Readers.GetOrAdd(patched.Type.ClrType ?? declared, _ => Build(patched.Type.Name, PropertiesOf(patched.Type)));
            return Readers.GetOrAdd(value.GetType(), static type => Build(type.Name, PropertiesOf(type)));
        }

        // Exactly one Geometry field: the maintainer's rule, and the message names what the record
        // has instead, because "no geometry" alone is a guess, not a diagnosis.
        static RecordReader Build(string typeName, RecordProperty[] properties)
        {
            var geometries = properties.Where(p => typeof(Geometry).IsAssignableFrom(p.Type)).ToArray();
            if (geometries.Length == 1)
                return new RecordReader
                {
                    TypeName = typeName,
                    Properties = properties,
                    Geometry = geometries[0],
                    Attributes = properties.Where(p => !ReferenceEquals(p, geometries[0])).ToArray(),
                };

            var has = properties.Length == 0
                ? "it has no fields at all"
                : "it has " + string.Join(", ", properties.Select(p => $"{p.Name}: {p.Type.Name}"));
            return new RecordReader
            {
                TypeName = typeName,
                Properties = properties,
                Problem = geometries.Length == 0
                    ? $"{typeName} is not a feature: it needs one field of type Geometry, and {has}"
                    : $"{typeName} is not a feature: it has {geometries.Length} fields of type Geometry ({string.Join(", ", geometries.Select(g => g.Name))}), a feature has exactly one",
            };
        }

        static RecordProperty[] PropertiesOf(IVLTypeInfo type) => type.Properties
            .Where(p => !p.IsManaged && p.Type?.ClrType is not null)
            .Select(p => new RecordProperty(KeyOf(p), p.Type!.ClrType!, p.GetValue, p.WithValue))
            .ToArray();

        // OriginalName is the name the patch shows - a property called "Some Field" keeps its space.
        static string KeyOf(IVLPropertyInfo property)
            => !string.IsNullOrEmpty(property.OriginalName) ? property.OriginalName : property.NameForTextualCode;

        // The fallback for an imported .NET type or an exported record: public fields and settable
        // properties, VL's own [Name] applied. Writing clones first so the template is never mutated.
        static RecordProperty[] PropertiesOf(Type type)
        {
            var properties = new List<RecordProperty>();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                properties.Add(new RecordProperty(NameOf(field, field.Name), field.FieldType,
                    o => field.GetValue(o),
                    (o, v) => { var copy = Clone(o); field.SetValue(copy, v); return copy; }));
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    properties.Add(new RecordProperty(NameOf(property, property.Name), property.PropertyType,
                        o => property.GetValue(o),
                        (o, v) => { if (!property.CanWrite) return o; var copy = Clone(o); property.SetValue(copy, v); return copy; }));
            return properties.ToArray();
        }

        static object Clone(object o)
            => typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, null)!;

        static string NameOf(MemberInfo member, string fallback)
            => member.GetCustomAttribute<VLName>()?.Name is { Length: > 0 } named ? named : fallback;
    }
}
