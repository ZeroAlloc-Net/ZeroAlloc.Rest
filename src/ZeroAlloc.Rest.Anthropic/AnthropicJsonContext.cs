using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// The source-generated JSON metadata for every type of this package. The client hands it to
/// <c>SystemTextJsonSerializer</c>; nothing here serializes through reflection. It is internal because the
/// generator emits nullable-oblivious members, which PublicApiAnalyzers rejects in a public API (RS0041).
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(MessageRequest))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(ContentBlockParam))]
[JsonSerializable(typeof(TextBlockParam))]
[JsonSerializable(typeof(ToolUseBlockParam))]
[JsonSerializable(typeof(ToolResultBlockParam))]
[JsonSerializable(typeof(CacheControl))]
[JsonSerializable(typeof(ContentBlock))]
[JsonSerializable(typeof(TextBlock))]
[JsonSerializable(typeof(ToolUseBlock))]
[JsonSerializable(typeof(Usage))]
[JsonSerializable(typeof(CacheCreation))]
[JsonSerializable(typeof(MessageStreamEvent))]
[JsonSerializable(typeof(MessageStartEvent))]
[JsonSerializable(typeof(ContentBlockStartEvent))]
[JsonSerializable(typeof(ContentBlockDeltaEvent))]
[JsonSerializable(typeof(ContentBlockStopEvent))]
[JsonSerializable(typeof(MessageDeltaEvent))]
[JsonSerializable(typeof(MessageStopEvent))]
[JsonSerializable(typeof(PingEvent))]
[JsonSerializable(typeof(ErrorEvent))]
[JsonSerializable(typeof(ContentDelta))]
[JsonSerializable(typeof(TextDelta))]
[JsonSerializable(typeof(InputJsonDelta))]
[JsonSerializable(typeof(ErrorDetail))]
internal sealed partial class AnthropicJsonContext : JsonSerializerContext;
