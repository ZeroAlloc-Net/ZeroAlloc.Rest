using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A prompt cache breakpoint. Written as <c>{"type":"ephemeral","ttl":...}</c>.</summary>
public sealed record CacheControl
{
    /// <summary>A breakpoint with the given time to live.</summary>
    /// <param name="ttl"><c>5m</c>, <c>1h</c>, or null for the API default.</param>
    public CacheControl(string? ttl) => Ttl = ttl;

    /// <summary>A five minute breakpoint.</summary>
    public static CacheControl FiveMinutes { get; } = new("5m");

    /// <summary>A one hour breakpoint.</summary>
    public static CacheControl OneHour { get; } = new("1h");

    /// <summary>The only cache type there is.</summary>
    [JsonPropertyName("type")]
    public string Type => "ephemeral";

    /// <summary>The time to live, or null for the API default.</summary>
    [JsonPropertyName("ttl")]
    public string? Ttl { get; init; }
}
