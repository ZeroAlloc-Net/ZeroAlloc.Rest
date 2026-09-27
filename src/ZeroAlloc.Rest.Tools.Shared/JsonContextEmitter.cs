using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Writes the JsonSerializerContext covering every generated type and every request and response
// type, spec §4. AllowOutOfOrderMetadataProperties lets a discriminator appear anywhere in an object.
internal static class JsonContextEmitter
{
    private const string List = TypeMapper.ListPrefix;
    private const string Dictionary = TypeMapper.DictionaryPrefix;

    // The members the STJ source generator gives every context besides one property per type, and
    // those it inherits from JsonSerializerContext and object. A model's name never contains an
    // underscore, so the generator's underscored members, such as _Pet and Create_Pet, cannot clash.
    private static readonly string[] ContextMembers =
    [
        "Default", "Options", "GeneratedSerializerOptions", "GetTypeInfo", "InstanceMemberBindingFlags",
        "TryGetTypeInfoForRuntimeCustomConverter", "GetRuntimeConverterForType", "ExpandConverter",
        "Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone", "Finalize", "ReferenceEquals",
    ];

    // Besides its property, the STJ source generator gives each type these methods, named by the
    // property with a suffix: PetPropInit, PetSerializeHandler and PetCtorParamInit.
    private static readonly string[] PerTypeSuffixes = ["PropInit", "SerializeHandler", "CtorParamInit"];

    // typeNames are the types the context registers. models are the generated types, whose members
    // the STJ generator reaches as well.
    internal static void Emit(StringBuilder sb, string contextName, IEnumerable<string> typeNames, IReadOnlyCollection<ModelDefinition> models)
    {
        var names = typeNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var propertyNames = PropertyNames(names, models);
        sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSourceGenerationOptions(").Append(ModelEmitter.Json)
            .AppendLine(".JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true)]");
        foreach (var typeName in names)
        {
            sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSerializable(typeof(").Append(typeName).Append(')');
            if (propertyNames.TryGetValue(typeName, out var propertyName))
                sb.Append(", TypeInfoPropertyName = ").Append(CSharpNames.Literal(propertyName));
            sb.AppendLine(")]");
        }
        sb.Append("public partial class ").Append(contextName).Append(" : ").Append(ModelEmitter.Serialization).AppendLine(".JsonSerializerContext");
        sb.AppendLine("{");
        sb.AppendLine("}");
    }

    // The STJ generator names a context property after its type: the simple CLR name, ListPet for
    // List<Pet>, DictionaryStringPet for Dictionary<string, Pet>, NullableGuid for Guid? and
    // ByteArray for byte[]. It does so for every type it reaches, registered or not, so a model
    // named like a member the context has anyway would clash: CS0102, or SYSLIB1031 for two types.
    // Such a model gets its own property name, {Name}Model; the model keeps its type name, and a
    // framework type cannot be renamed, since the generator derives a built-in converter's name
    // from its property name. Returns the models that are renamed, with their property names.
    private static Dictionary<string, string> PropertyNames(List<string> registered, IReadOnlyCollection<ModelDefinition> models)
    {
        var modelNames = new HashSet<string>(models.Select(m => m.Name), StringComparer.Ordinal);
        var taken = new HashSet<string>(ContextMembers, StringComparer.Ordinal);
        foreach (var type in registered.Concat(MemberTypes(models)).SelectMany(Components))
        {
            if (!modelNames.Contains(type))
                AddMembers(taken, PropertyName(type));
        }
        foreach (var model in modelNames)
        {
            foreach (var suffix in PerTypeSuffixes)
                taken.Add(model + suffix);
        }

        var used = new HashSet<string>(taken.Concat(modelNames), StringComparer.Ordinal);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var model in registered.Where(modelNames.Contains))
        {
            if (!taken.Contains(model))
                continue;
            var propertyName = CSharpNames.Unique(model + "Model", used);
            AddMembers(used, propertyName);
            renamed.Add(model, propertyName);
        }
        return renamed;
    }

    private static void AddMembers(HashSet<string> names, string propertyName)
    {
        names.Add(propertyName);
        foreach (var suffix in PerTypeSuffixes)
            names.Add(propertyName + suffix);
    }

    // The type of each member of a generated type as the STJ generator sees it: an optional or
    // nullable value type is Nullable<T>. A union's members are not: its converter reads and writes
    // them, and the context registers each variant type on its own.
    private static IEnumerable<string> MemberTypes(IEnumerable<ModelDefinition> models)
    {
        foreach (var model in models)
        {
            var properties = model switch
            {
                RecordModel record => record.Properties,
                PolymorphicModel polymorphic => polymorphic.Properties,
                _ => EquatableList<PropertyModel>.Empty,
            };
            foreach (var property in properties)
            {
                yield return property.Type.IsValueType
                    ? TypeMapper.Declare(property.Type, property.Required, property.Nullable)
                    : property.Type.Name;
            }
        }
    }

    // A type and every type it is built from: List<Guid?> gives itself, Guid? and Guid.
    private static IEnumerable<string> Components(string typeName)
    {
        yield return typeName;
        IEnumerable<string> inner = typeName switch
        {
            _ when typeName.EndsWith('?') => Components(typeName[..^1]),
            _ when typeName.StartsWith(List, StringComparison.Ordinal) => Components(typeName[List.Length..^1]),
            _ when typeName.StartsWith(Dictionary, StringComparison.Ordinal) => Components(typeName[Dictionary.Length..^1]).Append("string"),
            _ when typeName.EndsWith("[]", StringComparison.Ordinal) => Components(typeName[..^2]),
            _ => [],
        };
        foreach (var component in inner)
            yield return component;
    }

    private static string PropertyName(string typeName)
    {
        if (typeName.EndsWith('?'))
            return "Nullable" + PropertyName(typeName[..^1]);
        if (typeName.StartsWith(List, StringComparison.Ordinal))
            return "List" + PropertyName(typeName[List.Length..^1]);
        if (typeName.StartsWith(Dictionary, StringComparison.Ordinal))
            return "DictionaryString" + PropertyName(typeName[Dictionary.Length..^1]);
        if (typeName.EndsWith("[]", StringComparison.Ordinal))
            return PropertyName(typeName[..^2]) + "Array";
        return TypeRef.ClrName(typeName) ?? typeName[(typeName.LastIndexOf('.') + 1)..];
    }
}
