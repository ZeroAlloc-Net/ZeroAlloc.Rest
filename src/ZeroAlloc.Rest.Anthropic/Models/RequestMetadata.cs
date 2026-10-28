using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>Request metadata.</summary>
/// <param name="UserId">An opaque id for the end user, used for abuse detection.</param>
public sealed record RequestMetadata([property: JsonPropertyName("user_id")] string? UserId);
