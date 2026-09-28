using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// Maps a schema to the C# type the generated code uses for it, spec §5.1. A schema that needs a
// generated type is named by the namer; everything else maps structurally, so a component that is
// only a string with a format maps to its primitive, not to a model.
internal static class TypeMapper
{
    // How a list and a map type name start. The JSON context and the union variant names take
    // such a name apart again, so they read these too.
    internal const string ListPrefix = "global::System.Collections.Generic.List<";
    internal const string DictionaryPrefix = "global::System.Collections.Generic.Dictionary<string, ";

    internal static TypeRef Map(OpenApiSchema? schema, string contextName, string path, ISchemaTypeNamer namer, bool isBody = false)
    {
        if (schema is null)
            return Unsupported(namer, path, "it has no schema");
        if (schema.Not is not null)
            return Unsupported(namer, path, "it uses 'not', which has no C# equivalent");
        if (SingleWrapped(schema) is { } wrapped)
            return Map(wrapped.Part, contextName, path + wrapped.Suffix, namer, isBody);
        if (NeedsModel(schema))
            return namer.Named(schema, contextName, path);
        return schema.Type switch
        {
            "integer" => string.Equals(schema.Format, "int64", StringComparison.Ordinal) ? TypeRef.Long : TypeRef.Int,
            "number" => Number(schema.Format),
            "boolean" => TypeRef.Bool,
            "string" => Text(schema.Format, isBody),
            "array" => ListOf(schema, contextName, path, namer),
            "object" => DictionaryOf(schema, contextName, path, namer),
            null when schema.AdditionalProperties is not null => DictionaryOf(schema, contextName, path, namer),
            null => Unsupported(namer, path, "it has neither a type nor a composition"),
            _ => Unsupported(namer, path, $"its type '{schema.Type}' is not an OpenAPI 3.0 type"),
        };
    }

    // Spec §5.2: required and not nullable is T; anything that may be absent or null is T?.
    internal static string Declare(TypeRef type, bool required, bool nullable)
        => required && !nullable ? type.Name : type.Name + "?";

    internal static bool IsEnum(OpenApiSchema schema)
        => schema.Enum.Count > 0 && schema.Type is "string" or "integer";

    // Design decision 6: enums, compositions and objects with properties get a model, and so does a
    // named component object with no properties, which becomes an empty record named after it.
    internal static bool NeedsModel(OpenApiSchema schema)
        => IsEnum(schema)
            || schema.AllOf.Count > 0 || schema.OneOf.Count > 0 || schema.AnyOf.Count > 0
            || schema.Properties.Count > 0
            || (schema.Reference is not null
                && string.Equals(schema.Type, "object", StringComparison.Ordinal)
                && schema.AdditionalProperties is null);

    // The schema a single-part wrapper stands for, or the schema itself.
    internal static OpenApiSchema Unwrap(OpenApiSchema schema)
        => SingleWrapped(schema) is { } wrapped ? Unwrap(wrapped.Part) : schema;

    // Design decision 7: allOf, oneOf or anyOf with one part and nothing else, which specs use to
    // attach nullable or a description to a $ref, stands for that part. A named component is never
    // a wrapper: it gets its own type.
    internal static (OpenApiSchema Part, string Suffix)? SingleWrapped(OpenApiSchema schema)
    {
        if (schema.Reference is not null || schema.Properties.Count > 0 || schema.Discriminator is not null)
            return null;
        if (schema.AllOf.Count + schema.OneOf.Count + schema.AnyOf.Count != 1)
            return null;
        if (schema.AllOf.Count == 1) return (schema.AllOf[0], "/allOf/0");
        if (schema.OneOf.Count == 1) return (schema.OneOf[0], "/oneOf/0");
        return (schema.AnyOf[0], "/anyOf/0");
    }

    private static TypeRef Number(string? format) => format switch
    {
        "float" => TypeRef.Float,
        "decimal" => TypeRef.Decimal,
        _ => TypeRef.Double,
    };

    private static TypeRef Text(string? format, bool isBody) => format switch
    {
        "date-time" => TypeRef.DateTimeOffset,
        "date" => TypeRef.DateOnly,
        "time" => TypeRef.TimeOnly,
        "uuid" => TypeRef.Guid,
        "uri" => TypeRef.Uri,
        "byte" => TypeRef.Bytes,
        "binary" when isBody => TypeRef.Stream,
        _ => TypeRef.String,
    };

    private static TypeRef ListOf(OpenApiSchema schema, string contextName, string path, ISchemaTypeNamer namer)
    {
        var item = Map(schema.Items, contextName + "Item", path + "/items", namer);
        return new TypeRef(ListPrefix + Element(item, schema.Items) + ">", TypeRefKind.List, IsValueType: false);
    }

    private static TypeRef DictionaryOf(OpenApiSchema schema, string contextName, string path, ISchemaTypeNamer namer)
    {
        var value = schema.AdditionalProperties is null
            ? TypeRef.JsonElement
            : Map(schema.AdditionalProperties, contextName + "Value", path + "/additionalProperties", namer);
        return new TypeRef(DictionaryPrefix + Element(value, schema.AdditionalProperties) + ">", TypeRefKind.Dictionary, IsValueType: false);
    }

    // A nullable element is annotated only when it is a value type: the JSON context registers the
    // collection with typeof, which takes no nullable reference type annotations.
    private static string Element(TypeRef type, OpenApiSchema? schema)
        => schema?.Nullable == true && type.IsValueType ? type.Name + "?" : type.Name;

    private static TypeRef Unsupported(ISchemaTypeNamer namer, string path, string reason)
    {
        namer.Unsupported(path, reason);
        return TypeRef.JsonElement;
    }
}
