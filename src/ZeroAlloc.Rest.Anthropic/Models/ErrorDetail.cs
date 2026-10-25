using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The type and message of an API error.</summary>
/// <param name="Type">The error type, such as <c>overloaded_error</c>.</param>
/// <param name="Message">The human readable message.</param>
public sealed record ErrorDetail(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] string Message);
