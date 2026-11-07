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
// ── The user's record IS the feature; the door picks the geometry ─────────────────────────────
//
// Branch record-bridge, 2026-11-07. The maintainer's definition, in three steps the same night:
// first "a GIS feature is a row of a table whose columns are defined first - in vvvv a Record",
// then "the record itself is the feature: one Geometry field, the rest is whatever describes it",
// and last, after a longer think: "one geometry per feature is GeoJSON's rule, a rule of the
// exchange format, not of how data is kept. A feature in any other system can carry more - its
// outline, its entrance, its centre, its bounding box - and WHICH one to draw or compute with is
// a choice made at that moment". Measured against the field: PostGIS tables and GDAL/OGR (since
// 1.11) hold any number of geometry columns; GeoPackage, Shapefile, GeoJSON, Mapsui's
// GeometryFeature and NTS's own Feature hold exactly one; QGIS loading a PostGIS table asks which
// column. Storage allows many, every exchange and drawing layer takes one, and the selection is
// made once at the door.
//
// So: a record such as
//     Station  { Geometry: Geometry, Name: String, Platforms: Integer32, Terminal: Boolean }
//     Building { Outline: Polygon, Entrance: Point, Name: String }
// is a feature, a Spread of it a dataset, and these two nodes are the door to the NTS Feature
// that GeoJSON writes and Mapsui draws (vl-mapsui's FeatureLayer converts an NTS Feature to a
// GeometryFeature with its one Geometry; Pick hands an NTS Feature back). The rule "one geometry"
// lives on the door, not on the record: with one Geometry field the choice is made; with several,
// `Geometry Field` names the one, and an unnamed choice is refused with the candidates listed,
// never guessed. vl-mapsui's ToFeatures (2026-08-15) picked the geometry by type and the
// maintainer removed it (2026-09-25) because that was a convention nobody could see; here the
// choice is a pin. The fields not chosen go into the attribute table as Geometry values, so
// nothing is lost and Record (FromFeature) with the same pin puts them back. The Dictionary and
// Attributes (Join)/(Split) on main stay as the schema-less layer underneath.
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
// records are the same shape. `Geometry Field` is the last parameter so that it is optional in
// C# (the out parameters are required) - in the patch it is the second input pin either way.
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
    /// Makes a feature from your own record: one Geometry field is the shape, every other field an attribute.
    /// </summary>
    /// <remarks>
    /// Define a record once (right-click the canvas, Record) with a field of type Geometry and the fields a feature of that kind has.
    /// With several Geometry fields, Geometry Field names the shape and the others travel as attributes; unnamed, Success is false and Status lists them.
    /// </remarks>
    /// <param name="record">A value of your record type. Unconnected gives an empty feature.</param>
    /// <param name="success">True when one Geometry field was chosen.</param>
    /// <param name="status">What was read, or why nothing was.</param>
    /// <param name="geometryField">Which field is the shape. Leave empty when the record has one Geometry field.</param>
    /// <returns>The feature.</returns>
    [Name("Feature (FromRecord)")]
    public static NtsFeature FeatureFromRecord<T>(T? record, out bool success, out string status, string geometryField = "")
    {
        if (record is null)
        {
            success = false;
            status = "no record connected";
            return Feature(null, null);
        }

        var reader = RecordReader.For(record, typeof(T));
        if (!reader.TryPick(geometryField, out var shape, out var problem))
        {
            success = false;
            status = problem;
            return Feature(null, null);
        }

        var geometry = shape.Read(record) as Geometry;
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var property in reader.AttributesBeside(shape))
            if (property.Read(record) is { } value)
                builder[property.Name] = value;

        success = true;
        status = $"{reader.TypeName}: {shape.Name} is the shape, {builder.Count} attributes"
            + (geometry is null ? $" ({shape.Name} is empty)" : "");
        return Feature(geometry, builder.ToImmutable());
    }

    /// <summary>
    /// Fills your own record from a feature: one Geometry field takes the shape, each other field the attribute of the same name.
    /// </summary>
    /// <remarks>
    /// Template is a value of your record type, usually its Create node; fields the feature does not have keep the template's value.
    /// With several Geometry fields, Geometry Field names the one that takes the shape; Success is false when a field was missing or not converted.
    /// </remarks>
    /// <param name="feature">The feature to read.</param>
    /// <param name="template">A value of your record type: gives the type and the defaults.</param>
    /// <param name="success">True when every field was filled.</param>
    /// <param name="status">Which fields were filled, and which were not.</param>
    /// <param name="geometryField">Which field takes the shape. Leave empty when the record has one Geometry field.</param>
    /// <returns>The record with the feature's shape and attributes.</returns>
    [Name("Record (FromFeature)")]
    public static T RecordFromFeature<T>(NtsFeature? feature, T template, out bool success, out string status, string geometryField = "")
    {
        if (template is null)
        {
            success = false;
            status = "no template connected";
            return template!;
        }

        var reader = RecordReader.For(template, typeof(T));
        if (!reader.TryPick(geometryField, out var shape, out var problem))
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
            current = shape.Write(current, geometry);
            filled.Add(shape.Name);
        }
        else
        {
            missing.Add(shape.Name);
        }

        var table = feature?.Attributes;
        foreach (var property in reader.AttributesBeside(shape))
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
    // or CSV round trip needs. Anything already of the field's type passes untouched - which is
    // how a Geometry kept as an attribute gets back into its Geometry field.
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
    /// How to read and write the fields of one record type, worked out once per type: which fields
    /// are geometries, and how to pick the one that is the shape.
    /// </summary>
    sealed class RecordReader
    {
        static readonly ConcurrentDictionary<Type, RecordReader> Readers = new();

        public string TypeName { get; private init; } = "";
        public RecordProperty[] Properties { get; private init; } = Array.Empty<RecordProperty>();
        /// <summary>The fields of type Geometry (or a subtype), in the record's order.</summary>
        public RecordProperty[] Geometries { get; private init; } = Array.Empty<RecordProperty>();

        public sealed record RecordProperty(string Name, Type Type, Func<object, object?> Read, Func<object, object?, object> Write);

        // The reader comes from the VALUE, not from typeof(T): inside the editor the patched record
        // carries its own IVLObject.Type, and typeof(T) has no CLR members to reflect over.
        public static RecordReader For(object value, Type declared)
        {
            if (value is IVLObject patched)
                return Readers.GetOrAdd(patched.Type.ClrType ?? declared, _ => Build(patched.Type.Name, PropertiesOf(patched.Type)));
            return Readers.GetOrAdd(value.GetType(), static type => Build(type.Name, PropertiesOf(type)));
        }

        static RecordReader Build(string typeName, RecordProperty[] properties) => new()
        {
            TypeName = typeName,
            Properties = properties,
            Geometries = properties.Where(p => typeof(Geometry).IsAssignableFrom(p.Type)).ToArray(),
        };

        /// <summary>Every field except the one that is the shape - the other geometries included.</summary>
        public IEnumerable<RecordProperty> AttributesBeside(RecordProperty shape)
            => Properties.Where(p => !ReferenceEquals(p, shape));

        // The door's rule. One Geometry field: that one. Several: the named one, and no name is a
        // refusal that lists the candidates - a guess here is the convention nobody could see that
        // vl-mapsui's ToFeatures was removed for. The message names what the record has instead,
        // because "no geometry" alone is a guess, not a diagnosis.
        public bool TryPick(string? geometryField, out RecordProperty shape, out string problem)
        {
            var wanted = geometryField?.Trim() ?? "";
            var candidates = string.Join(", ", Geometries.Select(g => g.Name));

            if (wanted.Length > 0)
            {
                var named = Properties.FirstOrDefault(p => p.Name == wanted)
                    ?? Properties.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (named is null)
                {
                    shape = null!;
                    problem = Geometries.Length == 0
                        ? $"{TypeName} has no field named {wanted}, and no field of type Geometry at all"
                        : $"{TypeName} has no field named {wanted}; its Geometry fields are {candidates}";
                    return false;
                }
                if (!typeof(Geometry).IsAssignableFrom(named.Type))
                {
                    shape = null!;
                    problem = Geometries.Length == 0
                        ? $"{TypeName}: {named.Name} is a {named.Type.Name}, not a Geometry, and the record has no field of type Geometry at all"
                        : $"{TypeName}: {named.Name} is a {named.Type.Name}, not a Geometry; its Geometry fields are {candidates}";
                    return false;
                }
                shape = named;
                problem = "";
                return true;
            }

            if (Geometries.Length == 1)
            {
                shape = Geometries[0];
                problem = "";
                return true;
            }

            shape = null!;
            if (Geometries.Length == 0)
            {
                var has = Properties.Length == 0
                    ? "it has no fields at all"
                    : "it has " + string.Join(", ", Properties.Select(p => $"{p.Name}: {p.Type.Name}"));
                problem = $"{TypeName} is not a feature: it needs a field of type Geometry, and {has}";
            }
            else
            {
                problem = $"{TypeName} has {Geometries.Length} fields of type Geometry ({candidates}): name the shape in Geometry Field";
            }
            return false;
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
