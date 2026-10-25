using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The body of <c>POST /v1/messages</c>. Unset optional members are not written.</summary>
public sealed record MessageRequest
{
    /// <summary>The model id.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>The most tokens to generate.</summary>
    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; init; }

    /// <summary>The system prompt, written as a string or as text blocks; omitted when null.</summary>
    [JsonPropertyName("system")]
    public SystemPrompt? System { get; init; }

    /// <summary>The conversation so far.</summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<MessageParam> Messages { get; init; }

    /// <summary>The tools the model may call.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    /// <summary>The sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>The nucleus sampling probability mass.</summary>
    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    /// <summary>Sequences that end generation.</summary>
    [JsonPropertyName("stop_sequences")]
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>Request metadata.</summary>
    [JsonPropertyName("metadata")]
    public RequestMetadata? Metadata { get; init; }

    /// <summary>Set by the streaming facade; callers never set it.</summary>
    [JsonInclude]
    [JsonPropertyName("stream")]
    internal bool? Stream { get; init; }
}
