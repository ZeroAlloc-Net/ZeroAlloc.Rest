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

    // reservedNames are the names the generated file already uses: the interface, the client the
    // source generator derives from it, and the JSON context.
    internal SchemaModelBuilder(IEnumerable<string> reservedNames, List<OpenApiWarning> warnings)
    {
        _warnings = warnings;
        _typeNames = new HashSet<string>(reservedNames, StringComparer.Ordinal);
    }

    public TypeRef Named(OpenApiSchema schema, string contextName, string path)
    {
        if (_types.TryGetValue(schema, out var known))
            return known;
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
        => TypeMapper.IsEnum(schema) ? BuildEnum(schema, name) : BuildRecord(schema, name, path);

    private RecordModel BuildRecord(OpenApiSchema schema, string name, string path)
    {
        var used = new HashSet<string>(RecordMembers, StringComparer.Ordinal) { name };
        var properties = new List<PropertyModel>(schema.Properties.Count);
        foreach (var (wireName, propertySchema) in schema.Properties)
        {
            var identifier = CSharpNames.Unique(CSharpNames.Pascal(wireName, "Property"), used);
            var type = TypeMapper.Map(propertySchema, name + identifier, path + "/properties/" + wireName, this);
            properties.Add(new PropertyModel(identifier, wireName, type, schema.Required.Contains(wireName), propertySchema.Nullable, propertySchema.Description));
        }
        return new RecordModel(name, schema.Description, new EquatableList<PropertyModel>(properties));
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
