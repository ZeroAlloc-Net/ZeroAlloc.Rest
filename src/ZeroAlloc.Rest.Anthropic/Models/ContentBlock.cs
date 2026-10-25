using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// A content block the model returned. A block of a type this package does not model reads as a plain
/// <see cref="ContentBlock"/>, so a caller can skip it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
public record ContentBlock;
