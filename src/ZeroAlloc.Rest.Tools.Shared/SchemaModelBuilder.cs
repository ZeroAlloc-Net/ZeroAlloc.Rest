using System.Globalization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// Reads the schemas the interface references into the intermediate model, spec §4. It owns naming:
// every generated type gets a unique name, and a schema is built once however often it is
// referenced. Models are built lazily in the order they are first referenced, so the output
// follows the spec and holds only what the interface uses.
internal sealed class SchemaModelBuilder : ISchemaTypeNamer
{
    // Members every record has. A property of the same name would not compile, so it gets a suffix.
    private static readonly string[] RecordMembers =
        ["EqualityContract", "Equals", "GetHashCode", "ToString", "PrintMembers", "Deconstruct", "GetType", "MemberwiseClone", "Finalize"];

    private readonly List<OpenApiWarning> _warnings;
    private readonly HashSet<string> _typeNames;
    private readonly Dictionary<OpenApiSchema, TypeRef> _types = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<(OpenApiSchema Schema, string Name, string Path)> _pending = new();
    private readonly List<ModelDefinition> _models = [];

    // Each variant of a discriminated oneOf or anyOf, with its base.
    private readonly Dictionary<OpenApiSchema, OpenApiSchema> _baseOf = new(ReferenceEqualityComparer.Instance);

    // reservedNames are the names the generated file already uses: the interface, the client the
    // source generator derives from it, and the JSON context.
    internal SchemaModelBuilder(OpenApiDocument document, IEnumerable<string> reservedNames, List<OpenApiWarning> warnings)
    {
        _warnings = warnings;
        _typeNames = new HashSet<string>(reservedNames, StringComparer.Ordinal);
        ClaimVariants(document);
    }

    // A C# record has one base type. Claiming every variant before anything is built means a
    // variant the interface reaches before its base still derives from it.
    private void ClaimVariants(OpenApiDocument document)
    {
        if (document.Components?.Schemas is null)
            return;
        foreach (var (id, schema) in document.Components.Schemas)
        {
            if (!IsPolymorphic(schema))
                continue;
            foreach (var variant in Variants(schema))
            {
                if (variant.Reference?.Id is not { } variantId)
                    throw new InvalidOperationException($"Schema '{id}': each variant of a oneOf or anyOf with a discriminator must be a $ref.");
                if (_baseOf.TryGetValue(variant, out var other) && !ReferenceEquals(other, schema))
                    throw new InvalidOperationException(
                        $"Schema '{variantId}' is a variant of both '{other.Reference?.Id}' and '{id}'; a C# record has one base type.");
                _baseOf[variant] = schema;
            }
        }
    }

    private static bool IsPolymorphic(OpenApiSchema schema)
        => schema.Discriminator is not null && (schema.OneOf.Count > 0 || schema.AnyOf.Count > 0);

    private static IList<OpenApiSchema> Variants(OpenApiSchema schema) => schema.OneOf.Count > 0 ? schema.OneOf : schema.AnyOf;

    public TypeRef Named(OpenApiSchema schema, string contextName, string path)
    {
        if (_types.TryGetValue(schema, out var known))
            return known;
        if (HasRecursiveAllOf(schema, new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance)))
        {
            Unsupported(path, "its allOf refers back to itself");
            _types.Add(schema, TypeRef.JsonElement);
            return TypeRef.JsonElement;
        }
        var name = Reserve(schema.Reference?.Id ?? contextName);
        var type = new TypeRef(name, TypeRefKind.Model, IsValueType: TypeMapper.IsEnum(schema));
        _types.Add(schema, type);
        _pending.Enqueue((schema, name, schema.Reference is null ? path : "#/components/schemas/" + schema.Reference.Id));
        return type;
    }

    public void Unsupported(string path, string reason) => _warnings.Add(OpenApiWarning.MappedToJsonElement(path, reason));

    // Builds every model registered so far, and those their properties register in turn.
    internal EquatableList<ModelDefinition> Build()
    {
        while (_pending.TryDequeue(out var next))
            _models.Add(BuildModel(next.Schema, next.Name, next.Path));
        return new EquatableList<ModelDefinition>(_models);
    }

    // A type name and its {Name}Converter are reserved together, so a converter never clashes.
    private string Reserve(string name)
    {
        var baseName = CSharpNames.Pascal(name, "Model");
        var candidate = baseName;
        for (var n = 2; _typeNames.Contains(candidate) || _typeNames.Contains(candidate + "Converter"); n++)
            candidate = baseName + n.ToString(CultureInfo.InvariantCulture);
        _typeNames.Add(candidate);
        _typeNames.Add(candidate + "Converter");
        return candidate;
    }

    private ModelDefinition BuildModel(OpenApiSchema schema, string name, string path)
    {
        if (TypeMapper.IsEnum(schema))
            return BuildEnum(schema, name);
        if (IsPolymorphic(schema))
            return BuildPolymorphic(schema, name, path);
        return BuildRecord(schema, name, path);
    }

    // A variant derives from its base, and inherits the base's properties and the discriminator,
    // which STJ writes itself and rejects as a declared property.
    private RecordModel BuildRecord(OpenApiSchema schema, string name, string path)
    {
        string? baseName = null;
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        _baseOf.TryGetValue(schema, out var baseSchema);
        if (baseSchema is not null)
        {
            baseName = Named(baseSchema, name + "Base", path).Name;
            inherited.Add(baseSchema.Discriminator.PropertyName);
            inherited.UnionWith(baseSchema.Properties.Keys);
        }
        var collected = new List<CollectedProperty>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema, path, name, collected, required, skip: baseSchema);
        collected.RemoveAll(p => inherited.Contains(p.WireName));
        return new RecordModel(name, schema.Description, Properties(name, collected, required), baseName);
    }

    // Spec §5.5: values come from discriminator.mapping, and otherwise from the schema name. A
    // mapping target is a $ref or a bare schema name; either way its last segment is the name.
    private PolymorphicModel BuildPolymorphic(OpenApiSchema schema, string name, string path)
    {
        var discriminator = schema.Discriminator.PropertyName;
        var valueById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (value, target) in schema.Discriminator.Mapping)
            valueById.TryAdd(target[(target.LastIndexOf('/') + 1)..], value);

        var variants = new List<DerivedTypeModel>();
        foreach (var variant in Variants(schema))
        {
            var id = variant.Reference?.Id
                ?? throw new InvalidOperationException($"Schema '{name}': each variant of a oneOf or anyOf with a discriminator must be a $ref.");
            _baseOf.TryAdd(variant, schema);
            var type = Named(variant, id, path);
            variants.Add(new DerivedTypeModel(type.Name, valueById.GetValueOrDefault(id, id)));
        }

        var collected = new List<CollectedProperty>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema, path, name, collected, required, skip: null);
        collected.RemoveAll(p => string.Equals(p.WireName, discriminator, StringComparison.Ordinal));
        return new PolymorphicModel(name, schema.Description, discriminator, Properties(name, collected, required), new EquatableList<DerivedTypeModel>(variants));
    }

    private readonly record struct CollectedProperty(string WireName, OpenApiSchema Schema, string Path);

    // Spec §5.4: the properties of a schema and of every allOf part, parts first, in declaration
    // order. A property is required if any part requires it; the same property with two shapes is a
    // generation error. skip is an allOf part whose properties the record inherits instead.
    private static void Collect(
        OpenApiSchema schema, string path, string recordName,
        List<CollectedProperty> collected, HashSet<string> required, OpenApiSchema? skip)
    {
        for (var i = 0; i < schema.AllOf.Count; i++)
        {
            var part = schema.AllOf[i];
            if (!ReferenceEquals(part, skip))
                Collect(part, path + "/allOf/" + i.ToString(CultureInfo.InvariantCulture), recordName, collected, required, skip);
        }
        foreach (var (wireName, propertySchema) in schema.Properties)
        {
            var existing = collected.FindIndex(p => string.Equals(p.WireName, wireName, StringComparison.Ordinal));
            if (existing < 0)
            {
                collected.Add(new CollectedProperty(wireName, propertySchema, path + "/properties/" + wireName));
                continue;
            }
            var before = Shape(collected[existing].Schema);
            var after = Shape(propertySchema);
            if (!string.Equals(before, after, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Schema '{recordName}': property '{wireName}' has conflicting types in its allOf parts, '{before}' and '{after}'.");
        }
        required.UnionWith(schema.Required);
    }

    // What makes two declarations of a property the same type, without generating anything.
    private static string Shape(OpenApiSchema schema)
    {
        if (schema.Reference?.Id is { } id)
            return id;
        var shape = schema.Type ?? "any";
        if (schema.Format is not null)
            shape += ":" + schema.Format;
        if (schema.Items is not null)
            shape += "[" + Shape(schema.Items) + "]";
        return shape;
    }

    private EquatableList<PropertyModel> Properties(string recordName, List<CollectedProperty> collected, HashSet<string> required)
    {
        var used = new HashSet<string>(RecordMembers, StringComparer.Ordinal) { recordName };
        var properties = new List<PropertyModel>(collected.Count);
        foreach (var (wireName, propertySchema, propertyPath) in collected)
        {
            var identifier = CSharpNames.Unique(CSharpNames.Pascal(wireName, "Property"), used);
            var type = TypeMapper.Map(propertySchema, recordName + identifier, propertyPath, this);
            properties.Add(new PropertyModel(identifier, wireName, type, required.Contains(wireName), propertySchema.Nullable, propertySchema.Description));
        }
        return new EquatableList<PropertyModel>(properties);
    }

    // Only allOf edges count: a record whose property refers back to it is fine.
    private static bool HasRecursiveAllOf(OpenApiSchema schema, HashSet<OpenApiSchema> visiting)
    {
        if (!visiting.Add(schema))
            return true;
        foreach (var part in schema.AllOf)
        {
            if (HasRecursiveAllOf(part, visiting))
                return true;
        }
        visiting.Remove(schema);
        return false;
    }

    private static EnumModel BuildEnum(OpenApiSchema schema, string name)
    {
        var isString = string.Equals(schema.Type, "string", StringComparison.Ordinal);
        var isInt64 = string.Equals(schema.Format, "int64", StringComparison.Ordinal);
        var varNames = VarNames(schema);
        var used = new HashSet<string>(StringComparer.Ordinal) { name };
        var members = new List<EnumMemberModel>(schema.Enum.Count);
        for (var i = 0; i < schema.Enum.Count; i++)
        {
            // A null in a nullable enum is not a member.
            if (WireValue(schema.Enum[i]) is not { } wire)
                continue;
            string member;
            if (isString)
            {
                member = CSharpNames.Pascal(wire, "Empty");
            }
            else
            {
                var value = long.Parse(wire, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                isInt64 |= value is < int.MinValue or > int.MaxValue;
                member = i < varNames.Count
                    ? CSharpNames.Pascal(varNames[i], "Value")
                    : (value < 0 ? "ValueMinus" + wire[1..] : "Value" + wire);
            }
            members.Add(new EnumMemberModel(CSharpNames.Unique(member, used), wire));
        }
        return new EnumModel(name, schema.Description, isString, isInt64 ? "long" : "int", new EquatableList<EnumMemberModel>(members));
    }

    private static string? WireValue(IOpenApiAny value) => value switch
    {
        OpenApiString text => text.Value,
        OpenApiInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
        OpenApiLong number => number.Value.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static List<string> VarNames(OpenApiSchema schema)
    {
        var names = new List<string>();
        if (schema.Extensions.TryGetValue("x-enum-varnames", out IOpenApiExtension? extension) && extension is OpenApiArray array)
        {
            foreach (var item in array)
                names.Add(item is OpenApiString text ? text.Value : "");
        }
        return names;
    }
}
