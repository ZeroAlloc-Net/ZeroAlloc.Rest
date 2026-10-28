using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// A change to a content block. A delta of a type this package does not model reads as a plain
/// <see cref="ContentDelta"/>, so a caller can skip it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(TextDelta), "text_delta")]
[JsonDerivedType(typeof(InputJsonDelta), "input_json_delta")]
public record ContentDelta;
