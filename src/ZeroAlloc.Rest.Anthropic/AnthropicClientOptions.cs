namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// Settings for <see cref="AnthropicClient"/>. The defaults are those of the Anthropic C# SDK 13.1.0.
/// </summary>
public sealed class AnthropicClientOptions
{
    /// <summary>
    /// The API key, sent as the <c>x-api-key</c> header. When null no key header is sent, so a handler can
    /// authenticate instead. Nothing reads it from the environment.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The API's base address. Default: <c>https://api.anthropic.com/</c>.</summary>
    public Uri BaseAddress { get; set; } = new("https://api.anthropic.com/");

    /// <summary>The <c>anthropic-version</c> header. Default: <c>2023-06-01</c>.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    /// <summary>
    /// The longest one attempt may take, set as <see cref="HttpClient.Timeout"/>. For a stream it bounds the wait
    /// for the response headers only. Default: ten minutes.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often a 429, 529 or other 5xx response is retried after the first attempt. Default: 2, three attempts in all.
    /// </summary>
    public int MaxRetries { get; set; } = 2;
}
