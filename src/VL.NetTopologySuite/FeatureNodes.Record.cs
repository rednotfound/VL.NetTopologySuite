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
// ── A record as a feature's schema ────────────────────────────────────────────────────────────
//
// Branch record-bridge, 2026-11-07 (the maintainer: a GIS feature is a row of a table whose columns
// are defined first; in vvvv "define first, then fill" is a Record, and Spread<Record> is the
// dataset). Two bridges between the user's own record and the NTS Feature that GeoJSON writes and
// Mapsui draws. The Dictionary stays underneath as the schema-less layer.
//
// Three constraints, every one paid for in vl-mapsui (NOTES.md 2026-08-15 and 2026-09-25, where
// a first `ToFeatures<T>` was built and then removed at the maintainer's request):
//
// 1. The record gives the ATTRIBUTES only; the geometry is its own pin. ToFeatures picked the
//    geometry by property type, and the maintainer rejected it: the record is the user's, and a
//    rule nobody can see until a Status complains is not a rule.
// 2. A patched record is read through VL, never through System.Reflection. Inside the editor a
//    record has NO CLR members for its properties - its public surface is `__State, Context,
//    Identity, __Program__` - only IVLObject.Type.Properties sees them; vvvvc's export emits real
//    public fields, which is why reading the generated C# did not catch it. Keys are OriginalName
//    (the name the patch shows, spaces included); `Name` is [Obsolete] and says so. CLR reflection
//    stays as the fallback for imported .NET types and exported records.
// 3. A generic T pin, not an object pin: VL.Serialization.MessagePack, VL.ImGui.Editors and the
//    built-in Serialize all take the user's record that way and reflect inside.
//
// IsManaged properties (system generated) are skipped. The reader per type is cached: a thousand
// records are the same shape.
//
// Record (FromFeature) fills a TEMPLATE instance - the type argument has to come from somewhere,
// and a template also supplies the defaults for attributes the feature does not carry. Each
// property is set with IVLPropertyInfo.WithValue, which returns "the instance with the newly set
// value" (VL.Core's words), i.e. a new record for an immutable one. Values are converted when the
// stored type differs from the slot's (the GeoJSON reader stores every integer as Int64, a patch
// slot is usually Integer32), and a value that cannot be converted leaves the template's default
// and is named in Status. Nothing here needs an AppHost or a NodeContext, so the tests run without
// vvvv against a double shaped like the editor's record.
public static partial class FeatureNodes
{
    /// <summary>
    /// Makes a feature from a geometry and your own record: every field of the record becomes an attribute.
    /// </summary>
    /// <remarks>
    /// Define a record once (right-click the canvas, Record) with the fields a feature of that kind has.
    /// The field names are the attribute names, with their types.
    /// </remarks>
    /// <param name="geometry">The shape.</param>
    /// <param name="record">A value of your record type. Unconnected gives a feature with no attributes.</param>
    /// <param name="status">What was read, or why nothing was.</param>
    /// <returns>The feature.</returns>
    [Name("Feature (FromRecord)")]
    public static NtsFeature FeatureFromRecord<T>(Geometry? geometry, T? record, out string status)
    {
        if (record is null)
        {
            status = "no record connected";
            return Feature(geometry, null);
        }

        var reader = RecordReader.For(record, typeof(T));
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var property in reader.Properties)
            if (property.Read(record) is { } value)
                builder[property.Name] = value;

        status = $"{builder.Count} attributes from {reader.TypeName}";
        return Feature(geometry, builder.ToImmutable());
    }

    /// <summary>
    /// Fills your own record from a feature: each field takes the attribute of the same name.
    /// </summary>
    /// <remarks>
    /// Template is a value of your record type, usually its Create node; fields the feature does not have keep the template's value.
    /// Success is false when a field was missing or its value could not be converted; Status says which.
    /// </remarks>
    /// <param name="feature">The feature to read.</param>
    /// <param name="template">A value of your record type: gives the type and the defaults.</param>
    /// <param name="geometry">The feature's shape.</param>
    /// <param name="success">True when every field was filled.</param>
    /// <param name="status">Which fields were filled, and which were not.</param>
    /// <returns>The record with the feature's attributes.</returns>
    [Name("Record (FromFeature)")]
    public static T RecordFromFeature<T>(NtsFeature? feature, T template, out Geometry? geometry, out bool success, out string status)
    {
        geometry = feature?.Geometry;
        if (template is null)
        {
            success = false;
            status = "no template connected";
            return template!;
        }

        var reader = RecordReader.For(template, typeof(T));
        var table = feature?.Attributes;
        object current = template;
        var filled = new List<string>();
        var missing = new List<string>();
        var unconvertible = new List<string>();

        foreach (var property in reader.Properties)
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
    // or CSV round trip needs. Anything already of the slot's type passes untouched.
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
    /// How to read and write the fields of one record type, worked out once per type.
    /// </summary>
    sealed class RecordReader
    {
        static readonly ConcurrentDictionary<Type, RecordReader> Readers = new();

        public string TypeName { get; private init; } = "";
        public RecordProperty[] Properties { get; private init; } = Array.Empty<RecordProperty>();

        public sealed record RecordProperty(string Name, Type Type, Func<object, object?> Read, Func<object, object?, object> Write);

        // The reader comes from the VALUE, not from typeof(T): inside the editor the patched record
        // carries its own IVLObject.Type, and typeof(T) has no CLR members to reflect over.
        public static RecordReader For(object value, Type declared)
        {
            if (value is IVLObject patched)
                return Readers.GetOrAdd(patched.Type.ClrType ?? declared, _ => For(patched.Type));
            return Readers.GetOrAdd(value.GetType(), static type => For(type));
        }

        static RecordReader For(IVLTypeInfo type) => new()
        {
            TypeName = type.Name,
            Properties = type.Properties
                .Where(p => !p.IsManaged && p.Type?.ClrType is not null)
                .Select(p => new RecordProperty(KeyOf(p), p.Type!.ClrType!, p.GetValue, p.WithValue))
                .ToArray(),
        };

        // OriginalName is the name the patch shows - a property called "Some Field" keeps its space.
        static string KeyOf(IVLPropertyInfo property)
            => !string.IsNullOrEmpty(property.OriginalName) ? property.OriginalName : property.NameForTextualCode;

        // The fallback for an imported .NET type or an exported record: public fields and settable
        // properties, VL's own [Name] applied. Writing clones first so the template is never mutated.
        static RecordReader For(Type type)
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
            return new RecordReader { TypeName = type.Name, Properties = properties.ToArray() };
        }

        static object Clone(object o)
            => typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, null)!;

        static string NameOf(MemberInfo member, string fallback)
            => member.GetCustomAttribute<VLName>()?.Name is { Length: > 0 } named ? named : fallback;
    }
}
