namespace ZeroAlloc.Rest.Anthropic.Tests.Support;

internal static class TestClients
{
    /// <summary>A fake key; no test ever holds a real one.</summary>
    public const string FakeKey = "sk-ant-test-123";

    public static MessageRequest Request() => new()
    {
        Model = "claude-opus-5-5",
        MaxTokens = 16,
        Messages = [new MessageParam(MessageRole.User, [new TextBlockParam("hi", null)])],
    };

    public static AnthropicClient Create(HttpClient http, Action<AnthropicClientOptions>? configure = null)
    {
        var options = new AnthropicClientOptions { ApiKey = FakeKey };
        configure?.Invoke(options);
        return AnthropicClient.Create(http, options);
    }

    public static async Task<List<MessageStreamEvent>> CollectAsync(
        AnthropicClient client, List<MessageStreamEvent>? seen = null, CancellationToken ct = default)
    {
        seen ??= [];
        await foreach (var e in client.StreamMessageAsync(Request(), ct).ConfigureAwait(false))
            seen.Add(e);
        return seen;
    }
}
