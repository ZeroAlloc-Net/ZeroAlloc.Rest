using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>An error in the middle of a stream, such as an overloaded_error.</summary>
/// <param name="Error">The error.</param>
public sealed record ErrorEvent([property: JsonPropertyName("error")] ErrorDetail Error) : MessageStreamEvent;
