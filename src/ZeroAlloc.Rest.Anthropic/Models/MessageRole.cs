using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The speaker of a message.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MessageRole>))]
public enum MessageRole
{
    /// <summary>The human side.</summary>
    [JsonStringEnumMemberName("user")]
    User,

    /// <summary>The model side.</summary>
    [JsonStringEnumMemberName("assistant")]
    Assistant,
}
