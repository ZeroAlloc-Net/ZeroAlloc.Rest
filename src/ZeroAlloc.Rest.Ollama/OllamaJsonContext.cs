using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>
/// The source-generated JSON metadata for this package's types. It is internal because the generator emits
/// nullable-oblivious members, which PublicApiAnalyzers rejects in a public API.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EmbedRequest))]
[JsonSerializable(typeof(EmbedResponse))]
internal sealed partial class OllamaJsonContext : JsonSerializerContext;
