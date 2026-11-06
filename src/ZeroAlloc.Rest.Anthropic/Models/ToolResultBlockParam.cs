using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The result of a tool call, sent back to the model.</summary>
/// <remarks>
/// The content is one string, <see cref="Content"/>, or a list of text blocks, <see cref="Result"/>, which reach the
/// model as separate blocks. Set one of them: both is an error when the request is written.
/// </remarks>
/// <param name="ToolUseId">The id of the tool use this answers.</param>
/// <param name="Content">The result text, or null when <see cref="Result"/> holds it.</param>
/// <param name="IsError">True when the tool failed.</param>
/// <param name="CacheControl">A cache breakpoint after this block.</param>
public sealed record ToolResultBlockParam(
    [property: JsonPropertyName("tool_use_id")] string ToolUseId,
    [property: JsonIgnore] string? Content,
    [property: JsonPropertyName("is_error"), JsonPropertyOrder(2)] bool? IsError,
    [property: JsonPropertyName("cache_control"), JsonPropertyOrder(3)] CacheControl? CacheControl = null) : ContentBlockParam
{
    /// <summary>
    /// The result as a string or as text blocks, for a result with several parts. Leave <see cref="Content"/> null
    /// when this is set.
    /// </summary>
    [JsonIgnore]
    public ToolResultContent? Result { get; init; }

    /// <summary>Creates a result made of text blocks.</summary>
    /// <param name="toolUseId">The id of the tool use this answers.</param>
    /// <param name="blocks">The parts of the result, one text block each.</param>
    /// <param name="isError">True when the tool failed.</param>
    /// <param name="cacheControl">A cache breakpoint after this block.</param>
    public static ToolResultBlockParam FromBlocks(
        string toolUseId, IReadOnlyList<TextBlockParam> blocks, bool? isError = null, CacheControl? cacheControl = null) =>
        new(toolUseId, null, isError, cacheControl) { Result = ToolResultContent.FromBlocks(blocks) };

    // The one "content" member on the wire, whichever of Content and Result holds it.
    [JsonPropertyName("content")]
    [JsonPropertyOrder(1)]
    [JsonInclude]
    internal ToolResultContent? Wire
    {
        get => (Content, Result) switch
        {
            (not null, not null) => throw new JsonException("A tool result sets either Content or Result, not both."),
            (not null, null) => ToolResultContent.FromString(Content),
            _ => Result,
        };
        init
        {
            if (value is not { } content)
                return;
            if (content.Blocks is null)
                Content = content.Text;
            else
                Result = content;
        }
    }
}
