using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.Anthropic.Tests.Support;
using ZeroAlloc.Rest.Anthropic.Transcripts;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Anthropic.Tests;

/// <summary>
/// Replays the recordings under <c>Transcripts/</c> made against the real API: the requests the client writes must
/// equal the recorded ones byte for byte, and the recorded answers must read back with their numbers.
/// </summary>
public sealed class TranscriptReplayTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Transcripts");

    private readonly FakeAnthropicHandler _handler = new();
    private readonly HttpClient _http;

    public TranscriptReplayTests() => _http = new HttpClient(_handler);

    public void Dispose() => _http.Dispose();

    public static TheoryData<string> Names() => [.. TranscriptRequests.Names];

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Each_recorded_request_is_written_byte_for_byte(string name)
    {
        var request = RequestFor(name);
        if (name.StartsWith("stream-", StringComparison.Ordinal))
            request = request with { Stream = true };

        await using var output = new MemoryStream();
        await new SystemTextJsonSerializer(AnthropicJson.Context).SerializeAsync(output, request);

        // Red: swap the order of Model and MaxTokens in MessageRequest; every name then differs.
        // Red: rename [JsonPropertyName("stream")] to "streaming" in MessageRequest; the two stream names then differ.
        Assert.Equal(Read($"{name}.request.json"), Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData("text", "Pong", "end_turn", 18, 5, 0, 0)]
    [InlineData("tool-use", null, "tool_use", 386, 53, 0, 0)]
    [InlineData("cache-write", "pong", "end_turn", 16, 4, 14402, 0)]
    [InlineData("cache-read", "pong", "end_turn", 16, 4, 0, 14402)]
    public async Task Each_recorded_response_parses_with_its_usage(
        string name, string? text, string stop, int input, int output, int created, int read)
    {
        _handler.Always(() => Responses.Json(HttpStatusCode.OK, Read($"{name}.response.json")));
        var client = TestClients.Create(_http);

        var result = await client.CreateMessageAsync(RequestFor(name), CancellationToken.None).WaitAsync(Bound);

        // Red: return Result.Failure from CreateMessageAsync for every response; this is then false.
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);
        var message = result.Value;
        // Red: rename [JsonPropertyName("max_tokens")] to "max" in MessageRequest; the sent body then differs.
        Assert.Equal(Read($"{name}.request.json"), Assert.Single(_handler.Requests).Body);
        // Red: rename [JsonPropertyName("stop_reason")] to "stop" in MessageResponse; the reason then reads null.
        Assert.Equal(stop, message.StopReason);
        // Red: rename [JsonPropertyName("model")] to "mod" in MessageResponse; the model then reads null.
        Assert.Equal(TranscriptRequests.Model, message.Model);
        var usage = message.Usage!;
        // Red: rename [JsonPropertyName("input_tokens")] to "input" in Usage; this then reads 0.
        Assert.Equal(input, usage.InputTokens);
        // Red: rename [JsonPropertyName("output_tokens")] to "output" in Usage; this then reads 0.
        Assert.Equal(output, usage.OutputTokens);
        // Red: rename [JsonPropertyName("cache_creation_input_tokens")] in Usage; this then reads null.
        Assert.Equal(created, usage.CacheCreationInputTokens);
        // Red: rename [JsonPropertyName("cache_read_input_tokens")] in Usage; this then reads null.
        Assert.Equal(read, usage.CacheReadInputTokens);
        // Red: rename [JsonPropertyName("ephemeral_5m_input_tokens")] in CacheCreation; this then reads null. All four
        // cases fail, since the others expect 0, but only cache-write, where `created` is 14402, shows the value is read.
        Assert.Equal(created, usage.CacheCreation!.Ephemeral5mInputTokens);
        if (text is not null)
        {
            // Red: rename [JsonPropertyName("text")] in TextBlock; the text then reads null.
            Assert.Equal(text, Assert.IsType<TextBlock>(Assert.Single(message.Content)).Text);
        }
        else
        {
            var tool = Assert.IsType<ToolUseBlock>(Assert.Single(message.Content));
            // Red: rename [JsonPropertyName("name")] in ToolUseBlock; the name then reads null.
            Assert.Equal("get_weather", tool.Name);
            // Red: rename [JsonPropertyName("input")] in ToolUseBlock; the input then reads undefined.
            Assert.Equal("Amsterdam", tool.Input.GetProperty("location").GetString());
        }
    }

    [Fact]
    public async Task The_recorded_stream_yields_start_deltas_stop_in_order()
    {
        _handler.Always(() => EventStream(Read("stream-text.response.sse")));
        var client = TestClients.Create(_http);

        var seen = await CollectAsync(client, RequestFor("stream-text"));

        // Red: drop `with { Stream = true }` from StreamMessageAsync; the body then lacks "stream":true.
        Assert.Equal(Read("stream-text.request.json"), Assert.Single(_handler.Requests).Body);
        // Red: drop [EventType("ping", typeof(PingEvent))]; the ping is then skipped and the list is shorter.
        // This one Red covers every lambda below on purpose: the missing ping shifts each later event one place up,
        // so the lambda at the ping position and every one after it fails.
        Assert.Collection(
            seen,
            e => Assert.Equal("msg_011CgUNz2Qov5XApN6RXx56p", Assert.IsType<MessageStartEvent>(e).Message.Id),
            e => Assert.Equal(0, Assert.IsType<ContentBlockStartEvent>(e).Index),
            e => Assert.IsType<PingEvent>(e),
            e => Assert.IsType<ContentBlockDeltaEvent>(e),
            e => Assert.IsType<ContentBlockDeltaEvent>(e),
            e => Assert.IsType<ContentBlockStopEvent>(e),
            e => Assert.IsType<MessageDeltaEvent>(e),
            e => Assert.IsType<MessageStopEvent>(e));

        var text = string.Concat(seen.OfType<ContentBlockDeltaEvent>().Select(d => Assert.IsType<TextDelta>(d.Delta).Text));
        // Red: rename [JsonPropertyName("text")] to "txt" in TextDelta; the text then reads null and the concat is empty.
        Assert.Equal("1 2 3 4 5", text);

        var start = seen.OfType<MessageStartEvent>().Single().Message.Usage!;
        // Red: rename [JsonPropertyName("input_tokens")] in Usage; this then reads 0.
        Assert.Equal(25, start.InputTokens);
        var delta = seen.OfType<MessageDeltaEvent>().Single();
        // Red: rename [JsonPropertyName("output_tokens")] in MessageDeltaUsage; this then reads null.
        Assert.Equal(11, delta.Usage!.OutputTokens);
        // Red: rename [JsonPropertyName("stop_reason")] in MessageDeltaBody; this then reads null.
        Assert.Equal("end_turn", delta.Delta.StopReason);
    }

    [Fact]
    public async Task The_recorded_tool_stream_reassembles_the_tool_input()
    {
        _handler.Always(() => EventStream(Read("stream-tool-use.response.sse")));
        var client = TestClients.Create(_http);

        var seen = await CollectAsync(client, RequestFor("stream-tool-use"));

        // Red: drop `with { Stream = true }` from StreamMessageAsync; the body then lacks "stream":true.
        Assert.Equal(Read("stream-tool-use.request.json"), Assert.Single(_handler.Requests).Body);
        var start = Assert.IsType<ToolUseBlock>(seen.OfType<ContentBlockStartEvent>().Single().ContentBlock);
        // Red: rename [JsonPropertyName("name")] in ToolUseBlock; the name then reads null.
        Assert.Equal("get_weather", start.Name);
        var json = string.Concat(seen.OfType<ContentBlockDeltaEvent>().Select(d => Assert.IsType<InputJsonDelta>(d.Delta).PartialJson));
        using var input = JsonDocument.Parse(json);
        // Red: rename [JsonPropertyName("partial_json")] to "partial" in InputJsonDelta; the joined text is then empty and JsonDocument.Parse throws.
        Assert.Equal("Amsterdam", input.RootElement.GetProperty("location").GetString());
        // Red: rename [JsonPropertyName("stop_reason")] in MessageDeltaBody; this then reads null.
        Assert.Equal("tool_use", seen.OfType<MessageDeltaEvent>().Single().Delta.StopReason);
    }

    [Fact]
    public async Task Cache_read_follows_cache_write()
    {
        var client = TestClients.Create(_http);
        _handler.Then(() => Responses.Json(HttpStatusCode.OK, Read("cache-write.response.json")))
            .Then(() => Responses.Json(HttpStatusCode.OK, Read("cache-read.response.json")));

        var write = await client.CreateMessageAsync(RequestFor("cache-write"), CancellationToken.None).WaitAsync(Bound);
        var read = await client.CreateMessageAsync(RequestFor("cache-read"), CancellationToken.None).WaitAsync(Bound);

        // Red: rename [JsonPropertyName("cache_creation_input_tokens")] in Usage; this then reads null.
        Assert.True(write.Value.Usage!.CacheCreationInputTokens > 0);
        // Red: rename [JsonPropertyName("cache_read_input_tokens")] in Usage; this then reads null.
        Assert.True(read.Value.Usage!.CacheReadInputTokens > 0);
        // Red: rename [JsonPropertyName("cache_read_input_tokens")] in Usage; this then reads null, not 0.
        Assert.Equal(0, write.Value.Usage!.CacheReadInputTokens);
        // Red: rename [JsonPropertyName("cache_creation_input_tokens")] in Usage; this then reads null, not 0.
        Assert.Equal(0, read.Value.Usage!.CacheCreationInputTokens);
    }

    [Fact]
    public async Task An_injected_unknown_event_and_block_are_skipped()
    {
        var recorded = Read("stream-text.response.sse");
        const string Marker = "event: ping\n";
        // Guard of the injected input, not of production code: no production change reaches it. If a re-recording
        // changed the stream so it holds no ping, the Replace below would inject nothing and the test would pass
        // without testing anything. Red: change Marker to "event: pong\n"; this then fails.
        Assert.Contains(Marker, recorded, StringComparison.Ordinal);
        const string Injected =
            "event: future_event\ndata: {\"type\":\"future_event\",\"x\":1}\n\n"
            + "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"future_block\",\"payload\":{\"a\":[1,2]}}}\n\n";
        var altered = recorded.Replace(Marker, Injected + Marker, StringComparison.Ordinal);
        _handler.Always(() => EventStream(altered));
        var client = TestClients.Create(_http);

        var seen = await CollectAsync(client, RequestFor("stream-text"));

        var text = string.Concat(seen.OfType<ContentBlockDeltaEvent>().Select(d => Assert.IsType<TextDelta>(d.Delta).Text));
        // Red: add [EventType("future_event", typeof(ErrorEvent))] to IAnthropicMessagesApi; the injected event then ends the stream in an error.
        Assert.Equal("1 2 3 4 5", text);
        // Red: drop IgnoreUnrecognizedTypeDiscriminators from ContentBlock; the future block then fails the stream.
        var starts = seen.OfType<ContentBlockStartEvent>().ToList();
        Assert.Equal(2, starts.Count);
        // Red: rename [JsonPropertyName("index")] to "idx" in ContentBlockStartEvent; the injected block then reads 0.
        Assert.Equal(1, starts[1].Index);
        // Red: add [JsonDerivedType(typeof(ToolUseBlock), "future_block")] to ContentBlock; the block then reads as ToolUseBlock.
        Assert.Equal(typeof(ContentBlock), starts[1].ContentBlock.GetType());
        // Red: two changes, as the stream checks one before this: map message_stop to PingEvent in
        // IAnthropicMessagesApi and replace `stopped |= current is MessageStopEvent;` with `stopped = true;` in
        // AnthropicClient; the last event is then a PingEvent.
        Assert.IsType<MessageStopEvent>(seen[^1]);
    }

    private static MessageRequest RequestFor(string name) => name switch
    {
        "text" => TranscriptRequests.Text(),
        "stream-text" => TranscriptRequests.StreamText(),
        "tool-use" => TranscriptRequests.ToolUse(),
        "stream-tool-use" => TranscriptRequests.StreamToolUse(),
        "tool-result" => ToolResult(),
        "cache-write" => TranscriptRequests.CacheWrite(TranscriptRequests.InitialPaddingLines),
        "cache-read" => TranscriptRequests.CacheRead(TranscriptRequests.InitialPaddingLines),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static MessageRequest ToolResult()
    {
        var response = JsonSerializer.Deserialize(Read("tool-use.response.json"), AnthropicJson.MessageResponse)!;
        var call = Assert.IsType<ToolUseBlock>(Assert.Single(response.Content));
        return TranscriptRequests.ToolResult(call.Id, call.Name, call.Input);
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Folder, file));

    private static HttpResponseMessage EventStream(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    private static async Task<List<MessageStreamEvent>> CollectAsync(AnthropicClient client, MessageRequest request)
    {
        using var cts = new CancellationTokenSource(Bound);
        var seen = new List<MessageStreamEvent>();
        await foreach (var e in client.StreamMessageAsync(request, CancellationToken.None).WithCancellation(cts.Token).ConfigureAwait(false))
            seen.Add(e);
        return seen;
    }
}
