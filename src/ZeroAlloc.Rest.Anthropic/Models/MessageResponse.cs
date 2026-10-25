using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The model's answer to a messages request, or the message of a stream's start event.</summary>
/// <param name="Id">The message id.</param>
/// <param name="Model">The model that answered.</param>
/// <param name="Role">Always the assistant.</param>
/// <param name="Content">The content blocks.</param>
/// <param name="StopReason">Why generation stopped, see <see cref="StopReasons"/>; null while streaming.</param>
/// <param name="StopSequence">The stop sequence that matched, if any.</param>
/// <param name="Usage">Token usage; null when the payload carries none.</param>
public sealed record MessageResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("role")] MessageRole Role,
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content,
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence,
    [property: JsonPropertyName("usage")] Usage? Usage);
