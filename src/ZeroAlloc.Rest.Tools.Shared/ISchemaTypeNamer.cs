using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// What TypeMapper asks for when a schema needs a type it cannot spell by itself.
internal interface ISchemaTypeNamer
{
    // The type of a schema that needs a generated model: a component object, an inline object, an
    // enum or a composition. contextName names an inline schema; path locates it for messages.
    TypeRef Named(OpenApiSchema schema, string contextName, string path);

    // Reports a schema mapped to JsonElement, as warning ZRT002.
    void Unsupported(string path, string reason);
}
