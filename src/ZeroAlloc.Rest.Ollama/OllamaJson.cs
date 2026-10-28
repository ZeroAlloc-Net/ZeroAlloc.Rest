using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>
/// The public, AOT-safe way to serialize this package's types from another assembly. It exposes the
/// source-generated metadata only, and nothing here falls back to reflection.
/// </summary>
public static class OllamaJson
{
    /// <summary>
    /// The source-generated context for every type of this package. Build the serializer from it:
    /// <c>new SystemTextJsonSerializer(OllamaJson.Context)</c>.
    /// </summary>
    public static JsonSerializerContext Context => OllamaJsonContext.Default;
}
