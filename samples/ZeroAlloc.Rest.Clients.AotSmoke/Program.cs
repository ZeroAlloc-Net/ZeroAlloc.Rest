using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Rest.Clients.AotSmoke;
using ZeroAlloc.Rest.Anthropic;
using ZeroAlloc.Rest.Ollama;

// Verify that the provider clients publish and run under PublishAot=true with no trim or AOT warning.
// Each client is driven over a loopback socket against an in-process stub, through its generated
// client and its source-generated JSON context.

const string OllamaDocumentedResponse =
    """{"model":"all-minilm","embeddings":[[0.010071029,-0.0017594862],[-0.0098027075,0.06042469]],"total_duration":14143917,"load_duration":1019500,"prompt_eval_count":8}""";

using (var stub = new StubServer())
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var serving = stub.ServeAsync(200, OllamaDocumentedResponse, cts.Token);

    using var http = new HttpClient { BaseAddress = stub.BaseAddress };
    var client = OllamaClient.Create(http);
    var result = await client.EmbedAsync(new EmbedRequest("all-minilm", ["a", "b"]), cts.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);

    // Red: return Result.Failure from OllamaClient.EmbedAsync whatever the answer.
    if (!result.IsSuccess)
        return Fail($"Ollama embed returned {result.Error}");

    var response = result.Value;
    // Red: build the response with Model = "x" in EmbedAsync.
    if (!string.Equals(response.Model, "all-minilm", StringComparison.Ordinal))
        return Fail("Ollama embed read the wrong model");

    // Red: keep only the first embedding in EmbedAsync.
    if (response.Embeddings.Count != 2)
        return Fail("Ollama embed read the wrong number of embeddings");

    // Red: reverse the Embeddings list in EmbedAsync.
    if (response.Embeddings[1][1] != 0.06042469f)
        return Fail("Ollama embed read the wrong vector values");

    // Red: build the response with PromptEvalCount = 0 in EmbedAsync.
    if (response.PromptEvalCount != 8)
        return Fail("Ollama embed read the wrong prompt_eval_count");

    // Red: change [Post("api/embed")] to [Post("api/embeddings")] in IOllamaApi.
    if (!stub.LastRequest.StartsWith("POST /api/embed ", StringComparison.Ordinal))
        return Fail($"Ollama embed sent the wrong request line: {stub.LastRequest}");

    // Red: rename [JsonPropertyName("input")] to "inputs" in EmbedRequest.
    if (!stub.LastRequest.EndsWith("""{"model":"all-minilm","input":["a","b"]}""", StringComparison.Ordinal))
        return Fail($"Ollama embed wrote the wrong body: {stub.LastRequest}");
}

using (var stub = new StubServer())
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var serving = stub.ServeAsync(404, """{"error":"model \"x\" not found"}""", cts.Token);

    var services = new ServiceCollection();
    services.AddOllamaClient(o => o.BaseAddress = stub.BaseAddress);
    using var provider = services.BuildServiceProvider();
    var client = provider.GetRequiredService<OllamaClient>();
    var result = await client.EmbedAsync(new EmbedRequest("x", ["a"]), cts.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);

    // Red: have EmbedAsync turn every failure into Result.Success of an empty response.
    if (!result.IsFailure)
        return Fail("Ollama error answer was not a failure");

    // Red: build the fallback error with status 0 in OllamaErrorMapper.Map.
    if (result.Error.Status != 404)
        return Fail($"Ollama error status was {result.Error.Status}");

    // Red: compare ValueTextEquals to "message" in OllamaErrorMapper.ReadMessage.
    if (!string.Equals(result.Error.Message, "model \"x\" not found", StringComparison.Ordinal))
        return Fail($"Ollama error message was {result.Error.Message}");
}

// The Anthropic calls replay two recordings made against the real API: the request each call must write, and
// the answer the stub serves back. They are embedded in this assembly, see the project file.
using (var stub = new StubServer())
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var serving = stub.ServeAsync(200, Embedded("text.response.json"), cts.Token);

    using var http = new HttpClient { BaseAddress = stub.BaseAddress };
    var client = AnthropicClient.Create(http, new AnthropicClientOptions { ApiKey = "sk-ant-smoke" });
    var request = new MessageRequest
    {
        Model = "claude-haiku-5-5",
        MaxTokens = 16,
        Messages = [new MessageParam(MessageRole.User, [new TextBlockParam("Reply with the single word pong.")])],
    };
    var result = await client.CreateMessageAsync(request, cts.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);

    // Red: return Result.Failure from AnthropicClient.CreateMessageAsync whatever the answer. The stub is then never
    // called, so the run ends in a 60 second timeout exception instead of this line, and still exits non-zero.
    if (!result.IsSuccess)
        return Fail($"Anthropic create returned {result.Error}");

    var message = result.Value;
    // Red: rename [JsonPropertyName("id")] to "ident" in MessageResponse.
    if (!string.Equals(message.Id, "msg_011CgUNyxuU5h61r3ritjz7R", StringComparison.Ordinal))
        return Fail("Anthropic create read the wrong id");

    // Red: rename [JsonPropertyName("text")] to "txt" in TextBlock.
    if (message.Content.Count != 1 || message.Content[0] is not TextBlock { Text: "Pong" })
        return Fail("Anthropic create read the wrong content");

    // Red: rename [JsonPropertyName("output_tokens")] to "output" in Usage.
    if (message.Usage is not { InputTokens: 18, OutputTokens: 5 })
        return Fail("Anthropic create read the wrong usage");

    // Red: change [Post("v1/messages")] to [Post("v1/message")] in IAnthropicMessagesApi.
    if (!stub.LastRequest.StartsWith("POST /v1/messages ", StringComparison.Ordinal))
        return Fail($"Anthropic create sent the wrong request line: {stub.LastRequest}");

    // Red: change the ApiKeyHeader constant to "x-key" in AnthropicClient.
    if (!stub.LastRequest.Contains("x-api-key: sk-ant-smoke", StringComparison.OrdinalIgnoreCase))
        return Fail("Anthropic create sent no api key");

    // Red: rename [JsonPropertyName("max_tokens")] to "max" in MessageRequest.
    if (!stub.LastRequest.EndsWith(Embedded("text.request.json"), StringComparison.Ordinal))
        return Fail($"Anthropic create wrote the wrong body: {stub.LastRequest}");
}

using (var stub = new StubServer())
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var serving = stub.ServeAsync(200, Embedded("stream-text.response.sse"), cts.Token, "text/event-stream");

    using var http = new HttpClient { BaseAddress = stub.BaseAddress };
    var client = AnthropicClient.Create(http, new AnthropicClientOptions { ApiKey = "sk-ant-smoke" });
    var request = new MessageRequest
    {
        Model = "claude-haiku-5-5",
        MaxTokens = 32,
        Messages = [new MessageParam(MessageRole.User, [new TextBlockParam("Count from 1 to 5, separated by spaces.")])],
    };
    var events = new List<MessageStreamEvent>();
    await foreach (var streamed in client.StreamMessageAsync(request, cts.Token).ConfigureAwait(false))
        events.Add(streamed);
    await serving.ConfigureAwait(false);

    // Red: drop [EventType("ping", typeof(PingEvent))] from IAnthropicMessagesApi; the count is then 7.
    // Red: drop the message_stop EventType there too; the stream then throws stream_incomplete before this check.
    if (events.Count != 8 || events[^1] is not MessageStopEvent)
        return Fail($"Anthropic stream yielded {events.Count} events");

    // Red: rename [JsonPropertyName("text")] to "txt" in TextDelta.
    var text = string.Concat(events.OfType<ContentBlockDeltaEvent>().Select(e => (e.Delta as TextDelta)?.Text));
    if (!string.Equals(text, "1 2 3 4 5", StringComparison.Ordinal))
        return Fail($"Anthropic stream read the text {text}");

    // Red: rename [JsonPropertyName("output_tokens")] to "output" in MessageDeltaUsage.
    if (events.OfType<MessageDeltaEvent>().Single().Usage?.OutputTokens != 11)
        return Fail("Anthropic stream read the wrong output tokens");

    // Red: drop the `with { Stream = true }` in StreamMessageAsync.
    if (!stub.LastRequest.EndsWith(Embedded("stream-text.request.json"), StringComparison.Ordinal))
        return Fail($"Anthropic stream wrote the wrong body: {stub.LastRequest}");
}

Console.WriteLine("AOT smoke: OK");
return 0;

static string Embedded(string name)
{
    using var stream = typeof(StubServer).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"The embedded resource {name} is missing.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
