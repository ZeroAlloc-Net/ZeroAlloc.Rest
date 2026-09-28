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

    private readonly List<OpenApiDiagnostic> _warnings;
    private readonly HashSet<string> _typeNames;
    private readonly Dictionary<OpenApiSchema, TypeRef> _types = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<(OpenApiSchema Schema, string Name, string Path)> _pending = new();
    private readonly List<ModelDefinition> _models = [];

    // The type of each inline union by its structural key; see InlineUnionKey.
    private readonly Dictionary<string, TypeRef> _inlineUnions = new(StringComparer.Ordinal);

    // Each variant of a discriminated oneOf or anyOf, with its base.
    private readonly Dictionary<OpenApiSchema, OpenApiSchema> _baseOf = new(ReferenceEqualityComparer.Instance);

    // reservedNames are the names the generated file already uses: the interface, the client the
    // source generator derives from it, and the JSON context.
    internal SchemaModelBuilder(OpenApiDocument document, IEnumerable<string> reservedNames, List<OpenApiDiagnostic> warnings)
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
        // A named schema is reported and built at its own path, not at whatever first reaches it.
        var schemaPath = schema.Reference is null ? path : "#/components/schemas/" + schema.Reference.Id;
        if (HasRecursiveAllOf(schema, new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance)))
        {
            Unsupported(schemaPath, "its allOf refers back to itself");
            _types.Add(schema, TypeRef.JsonElement);
            return TypeRef.JsonElement;
        }
        var unionKey = InlineUnionKey(schema);
        if (unionKey is not null && _inlineUnions.TryGetValue(unionKey, out var shared))
        {
            _types.Add(schema, shared);
            return shared;
        }
        var name = Reserve(schema.Reference?.Id ?? UnionName(schema) ?? contextName);
        var type = new TypeRef(name, TypeRefKind.Model, IsValueType: TypeMapper.IsEnum(schema));
        _types.Add(schema, type);
        if (unionKey is not null)
            _inlineUnions.Add(unionKey, type);
        _pending.Enqueue((schema, name, schemaPath));
        return type;
    }

    // Issue #359: structurally identical inline unions share one type. The key holds everything
    // BuildUnion reads: oneOf or anyOf, and each variant in order with its type and the required
    // properties and single-value enums the converter matches it by. The first occurrence names the type and gives it its
    // description; a later one, whatever its path or description, refers to that type. Titles,
    // descriptions and the union's own nullable are not part of the key: they change no generated
    // member, and nullable is declared on the property that uses the union.
    //
    // Only a union whose every variant is a $ref, or an inline primitive or array of such, has a
    // key. An inline object, map, wrapper or nested union variant has none, so such a union keeps a
    // type of its own: it would generate an inline type per occurrence, and a wrapper may add
    // required properties the plain $ref does not have. A component union has no key either: it
    // keeps its own name.
    private static string? InlineUnionKey(OpenApiSchema schema)
    {
        if (schema.Reference is not null || schema.Discriminator is not null || TypeMapper.IsEnum(schema)
            || schema.AllOf.Count > 0 || schema.Properties.Count > 0 || schema.Not is not null)
            return null;
        var isOneOf = schema.OneOf.Count > 0;
        var parts = isOneOf ? schema.OneOf : schema.AnyOf;
        if (parts.Count < 2 || (isOneOf && schema.AnyOf.Count > 0))
            return null;
        var key = new System.Text.StringBuilder(isOneOf ? "oneOf" : "anyOf");
        foreach (var part in parts)
        {
            if (VariantKey(part) is not { } variant)
                return null;
            key.Append('|').Append(variant).Append('{').Append(string.Join(",", RequiredOf(part))).Append(';')
                .Append(string.Join(",", ValuesOf(part).Select(v => v.WireName + "=" + v.Kind + ":" + v.Value))).Append('}');
        }
        return key.ToString();
    }

    // A $ref is its schema id; an inline primitive its type and format; an inline array its item's
    // key and whether the item is nullable, which the list's element type records. Anything else,
    // and anything TypeMapper would report as unsupported, has no key.
    private static string? VariantKey(OpenApiSchema part)
    {
        if (part.Reference?.Id is { } id)
            return TypeMapper.NeedsModel(part) ? "$ref:" + id : PrimitiveKey(part) is { } primitive ? "$ref:" + id + ":" + primitive : null;
        return TypeMapper.NeedsModel(part) ? null : PrimitiveKey(part);
    }

    private static string? PrimitiveKey(OpenApiSchema schema)
    {
        if (schema.Not is not null || schema.AdditionalProperties is not null)
            return null;
        switch (schema.Type)
        {
            case "integer" or "number" or "boolean" or "string":
                return schema.Type + ":" + schema.Format;
            case "array" when schema.Items is not null && VariantKey(schema.Items) is { } item:
                return "array[" + item + (schema.Items.Nullable ? "?" : "") + "]";
            default:
                return null;
        }
    }

    public void Unsupported(string path, string reason) => _warnings.Add(OpenApiDiagnostic.MappedToJsonElement(path, reason));

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
        if (schema.OneOf.Count > 0 || schema.AnyOf.Count > 0)
            return BuildUnion(schema, name, path);
        return BuildRecord(schema, name, path);
    }

    // A variant derives from its base, and inherits the base's properties, those of its allOf parts
    // included, and the discriminator, which STJ writes itself and rejects as a declared property.
    private RecordModel BuildRecord(OpenApiSchema schema, string name, string path)
    {
        string? baseName = null;
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        _baseOf.TryGetValue(schema, out var baseSchema);
        if (baseSchema is not null)
        {
            baseName = Named(baseSchema, name + "Base", path).Name;
            inherited.Add(baseSchema.Discriminator.PropertyName);
            var baseProperties = new List<CollectedProperty>();
            Collect(baseSchema, path, baseName, baseProperties, new HashSet<string>(StringComparer.Ordinal), skip: null);
            inherited.UnionWith(baseProperties.Select(p => p.WireName));
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

    // Design decision 8: an inline union whose variants are all $refs is named after them, PetOrError.
    private static string? UnionName(OpenApiSchema schema)
    {
        if (schema.Discriminator is not null)
            return null;
        var parts = schema.OneOf.Count > 0 ? schema.OneOf : schema.AnyOf;
        if (parts.Count < 2)
            return null;
        var names = new List<string>(parts.Count);
        foreach (var part in parts)
        {
            if (part.Reference?.Id is not { } id)
                return null;
            names.Add(CSharpNames.Pascal(id, "Variant"));
        }
        return string.Join("Or", names);
    }

    private UnionModel BuildUnion(OpenApiSchema schema, string name, string path)
    {
        var isOneOf = schema.OneOf.Count > 0;
        var parts = isOneOf ? schema.OneOf : schema.AnyOf;
        var keyword = isOneOf ? "/oneOf/" : "/anyOf/";
        var used = new HashSet<string>(StringComparer.Ordinal) { name };
        var variants = new List<UnionVariantModel>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var contextName = name + "Variant" + (i + 1).ToString(CultureInfo.InvariantCulture);
            var type = TypeMapper.Map(part, contextName, path + keyword + i.ToString(CultureInfo.InvariantCulture), this);
            var kind = KindOf(part, type);
            var required = kind == JsonKind.Object ? RequiredOf(part) : EquatableList<string>.Empty;
            var values = kind == JsonKind.Object ? ValuesOf(part) : EquatableList<UnionValueModel>.Empty;
            variants.Add(new UnionVariantModel(CSharpNames.Unique(VariantName(part, type), used), type, kind, required, values));
        }
        ReportIndistinguishable(name, path, isOneOf, variants);
        return new UnionModel(name, schema.Description, isOneOf, new EquatableList<UnionVariantModel>(variants));
    }

    // Issue #360: ZRT003 for each pair of object variants the converter cannot tell apart. Both
    // require the same properties, so both pass the required-property filter for the same objects
    // and score the same. For oneOf, the pair is reported unless a required property holds a
    // different single-value enum in each: otherwise some object matches both and reading it
    // throws. For anyOf, the first declared variant wins such an object, so the pair is reported
    // when the later variant is never read: every value the earlier one requires, the later one
    // requires too, so each object the later one matches, the earlier one matches as well.
    private void ReportIndistinguishable(string name, string path, bool isOneOf, List<UnionVariantModel> variants)
    {
        for (var i = 0; i < variants.Count; i++)
        {
            var first = variants[i];
            if (first.Kind != JsonKind.Object)
                continue;
            for (var j = i + 1; j < variants.Count; j++)
            {
                var second = variants[j];
                if (second.Kind != JsonKind.Object || !first.RequiredWireNames.Equals(second.RequiredWireNames))
                    continue;
                if (isOneOf && !HaveDifferentRequiredValue(first, second))
                {
                    _warnings.Add(new OpenApiDiagnostic(OpenApiDiagnostic.IndistinguishableUnionVariants,
                        $"Schema '{path}': oneOf variants '{first.Name}' and '{second.Name}' of union '{name}' cannot be told apart. "
                        + "They require the same properties, and no required property holds a different single-value enum in each, "
                        + "so reading a JSON object that matches both throws. " + IndistinguishableAdvice,
                        OpenApiSeverity.Error));
                }
                else if (!isOneOf && first.Values.All(second.Values.Contains))
                {
                    _warnings.Add(new OpenApiDiagnostic(OpenApiDiagnostic.IndistinguishableUnionVariants,
                        $"Schema '{path}': anyOf variants '{first.Name}' and '{second.Name}' of union '{name}' cannot be told apart. "
                        + $"Every JSON object that matches '{second.Name}' also matches '{first.Name}', and the first declared variant "
                        + $"always wins, so '{second.Name}' is never read. " + IndistinguishableAdvice));
                }
            }
        }
    }

    private const string IndistinguishableAdvice
        = "Add a discriminator, or a required property with a different single-value enum to each variant.";

    // A property absent from the object passes the value filter, so only a required one separates
    // the variants, and only when each variant gives it a value and no value is shared.
    private static bool HaveDifferentRequiredValue(UnionVariantModel first, UnionVariantModel second)
    {
        foreach (var wireName in first.RequiredWireNames)
        {
            var mine = first.Values.Where(v => string.Equals(v.WireName, wireName, StringComparison.Ordinal)).ToList();
            var theirs = second.Values.Where(v => string.Equals(v.WireName, wireName, StringComparison.Ordinal)).ToList();
            if (mine.Count > 0 && theirs.Count > 0 && !mine.Exists(theirs.Contains))
                return true;
        }
        return false;
    }

    private static string VariantName(OpenApiSchema part, TypeRef type)
        => part.Reference?.Id is { } id ? CSharpNames.Pascal(id, "Variant") : ReadableName(type.Name);

    // A readable name for a type: its CLR name for a keyword, its last segment otherwise, and the
    // element's name first for a collection: List<string> is StringList.
    private static string ReadableName(string typeName)
    {
        if (typeName.StartsWith(TypeMapper.ListPrefix, StringComparison.Ordinal))
            return ReadableName(typeName[TypeMapper.ListPrefix.Length..^1]) + "List";
        if (typeName.StartsWith(TypeMapper.DictionaryPrefix, StringComparison.Ordinal))
            return ReadableName(typeName[TypeMapper.DictionaryPrefix.Length..^1]) + "Map";
        var name = typeName.TrimEnd('?');
        if (string.Equals(name, TypeRef.Bytes.Name, StringComparison.Ordinal))
            return "Bytes";
        return TypeRef.ClrName(name) ?? name[(name.LastIndexOf('.') + 1)..];
    }

    private static JsonKind KindOf(OpenApiSchema part, TypeRef type) => type.Kind switch
    {
        TypeRefKind.List => JsonKind.Array,
        TypeRefKind.Dictionary => JsonKind.Object,
        TypeRefKind.JsonElement or TypeRefKind.Stream => JsonKind.Any,
        TypeRefKind.Model => ModelKind(TypeMapper.Unwrap(part)),
        _ when type == TypeRef.Bool => JsonKind.Boolean,
        _ when type == TypeRef.Int || type == TypeRef.Long || type == TypeRef.Float || type == TypeRef.Double || type == TypeRef.Decimal
            => JsonKind.Number,
        _ => JsonKind.String,
    };

    private static JsonKind ModelKind(OpenApiSchema schema)
    {
        if (TypeMapper.IsEnum(schema))
            return string.Equals(schema.Type, "integer", StringComparison.Ordinal) ? JsonKind.Number : JsonKind.String;
        if (IsPolymorphic(schema) || (schema.OneOf.Count == 0 && schema.AnyOf.Count == 0))
            return JsonKind.Object;
        return JsonKind.Any;
    }

    // The required properties of an object variant, its allOf parts included, sorted so the model is
    // deterministic. A recursive allOf never gets here: Named mapped it to JsonElement, kind Any.
    // A single-part wrapper stands for its part, but what it requires still counts.
    private static EquatableList<string> RequiredOf(OpenApiSchema schema)
    {
        var required = new SortedSet<string>(StringComparer.Ordinal);
        while (TypeMapper.SingleWrapped(schema) is { } wrapped)
        {
            required.UnionWith(schema.Required);
            schema = wrapped.Part;
        }
        AddRequired(schema, required);
        return new EquatableList<string>(required);
    }

    // Issue #360: the single-value enums of an object variant's properties, its allOf parts included,
    // which the converter checks before scoring required properties. A single-part wrapper declares
    // no properties, so the part it wraps stands for it. Sorted, so the model is deterministic.
    private static EquatableList<UnionValueModel> ValuesOf(OpenApiSchema schema)
    {
        var values = new List<UnionValueModel>();
        AddValues(TypeMapper.Unwrap(schema), values);
        return new EquatableList<UnionValueModel>(values
            .Distinct()
            .OrderBy(v => v.WireName, StringComparer.Ordinal)
            .ThenBy(v => v.Value, StringComparer.Ordinal)
            .ToList());
    }

    private static void AddValues(OpenApiSchema schema, List<UnionValueModel> values)
    {
        foreach (var (wireName, property) in schema.Properties)
        {
            if (SingleValue(wireName, TypeMapper.Unwrap(property)) is { } value)
                values.Add(value);
        }
        foreach (var part in schema.AllOf)
            AddValues(part, values);
    }

    private static UnionValueModel? SingleValue(string wireName, OpenApiSchema property)
    {
        if (property.Enum.Count != 1)
            return null;
        return property.Enum[0] switch
        {
            OpenApiString text => new UnionValueModel(wireName, JsonKind.String, text.Value),
            OpenApiInteger number => new UnionValueModel(wireName, JsonKind.Number, number.Value.ToString(CultureInfo.InvariantCulture)),
            OpenApiLong number => new UnionValueModel(wireName, JsonKind.Number, number.Value.ToString(CultureInfo.InvariantCulture)),
            OpenApiBoolean flag => new UnionValueModel(wireName, JsonKind.Boolean, flag.Value ? "true" : "false"),
            _ => null,
        };
    }

    private static void AddRequired(OpenApiSchema schema, SortedSet<string> required)
    {
        required.UnionWith(schema.Required);
        foreach (var part in schema.AllOf)
            AddRequired(part, required);
    }
}
