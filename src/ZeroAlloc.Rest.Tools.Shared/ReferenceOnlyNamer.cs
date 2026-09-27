using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// The namer for --models false, design decision 16: a $ref is its bare PascalCase name, which a
// hand-written DTO in the same namespace satisfies, and an inline schema that would need a model
// has no type, so it maps to JsonElement with ZRT002.
internal sealed class ReferenceOnlyNamer(List<OpenApiWarning> warnings) : ISchemaTypeNamer
{
    public TypeRef Named(OpenApiSchema schema, string contextName, string path)
    {
        if (schema.Reference?.Id is { } id)
            return new TypeRef(CSharpNames.Pascal(id, "Model"), TypeRefKind.Model, IsValueType: TypeMapper.IsEnum(schema));
        Unsupported(path, "models are off, so an inline schema has no generated type");
        return TypeRef.JsonElement;
    }

    public void Unsupported(string path, string reason) => warnings.Add(OpenApiWarning.MappedToJsonElement(path, reason));
}
