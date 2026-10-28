using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A content block sent to the model. The <c>type</c> member selects the derived type.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlockParam), "text")]
[JsonDerivedType(typeof(ToolUseBlockParam), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlockParam), "tool_result")]
public abstract record ContentBlockParam;
