using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Anthropic.Tests.Support;

namespace ZeroAlloc.Rest.Anthropic.Tests;

public sealed class ClientTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly FakeAnthropicHandler _handler = new();
    private readonly HttpClient _http;

    public ClientTests() => _http = new HttpClient(_handler);

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task Every_request_carries_x_api_key_and_anthropic_version()
    {
        _handler.Then(Responses.Message).Then(() => Responses.Stream(Responses.FullStream()));
        var client = TestClients.Create(_http);

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);
        await TestClients.CollectAsync(client).WaitAsync(Bound);

        // Red: return Result.Failure from CreateMessageAsync for every response; IsSuccess is then false.
        Assert.True(result.IsSuccess);
        // Red: read the response with the wrong type info, such as an empty MessageResponse; the id is then lost.
        Assert.Equal("msg_1", result.Value.Id);
        var requests = _handler.Requests;
        // Red: skip the stream call in this test; there is then one request.
        Assert.Equal(2, requests.Count);
        foreach (var request in requests)
        {
            // Red: change [Post("v1/messages")] to [Put("v1/messages")]; the method is then PUT.
            Assert.Equal(HttpMethod.Post, request.Method);
            // Red: default BaseAddress to https://api.anthropic.com/v2/; the URI then has /v2/.
            Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), request.Uri);
            // Red: drop the x-api-key header from Create; the lookup then throws KeyNotFoundException.
            Assert.Equal(TestClients.FakeKey, request.Headers["x-api-key"]);
            // Red: default AnthropicVersion to "2024-01-01"; the header then differs.
            Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
        }
    }

    [Fact]
    public async Task Only_the_stream_sends_stream_true()
    {
        _handler.Then(Responses.Message).Then(() => Responses.Stream(Responses.FullStream()));
        var client = TestClients.Create(_http);

        await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);
        await TestClients.CollectAsync(client).WaitAsync(Bound);

        var requests = _handler.Requests;
        // Red: set Stream = true in CreateMessageAsync too; the body then holds "stream".
        Assert.DoesNotContain("\"stream\"", requests[0].Body, StringComparison.Ordinal);
        // Red: drop `with { Stream = true }` from StreamMessageAsync; the body then has no stream member.
        Assert.Contains("\"stream\":true", requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stream_yields_every_event_in_order()
    {
        _handler.Then(() => Responses.Stream(Responses.FullStream()));
        var client = TestClients.Create(_http);

        var seen = await TestClients.CollectAsync(client).WaitAsync(Bound);

        // Red: drop [EventType("ping", typeof(PingEvent))]; the ping is then skipped and the list is shorter.
        Assert.Collection(
            seen,
            e => Assert.IsType<MessageStartEvent>(e),
            e => Assert.IsType<ContentBlockStartEvent>(e),
            e => Assert.IsType<PingEvent>(e),
            // Red: yield only every other event from the facade; the delta is then missing here.
            e => Assert.Equal("Hi", Assert.IsType<TextDelta>(Assert.IsType<ContentBlockDeltaEvent>(e).Delta).Text),
            e => Assert.IsType<ContentBlockStopEvent>(e),
            e => Assert.IsType<MessageDeltaEvent>(e),
            e => Assert.IsType<MessageStopEvent>(e));
    }

    [Fact]
    public void The_default_timeout_is_ten_minutes()
    {
        TestClients.Create(_http);

        // Red: drop the Timeout assignment from Create; HttpClient's default is 100 seconds.
        Assert.Equal(TimeSpan.FromMinutes(10), _http.Timeout);
        // Red: default AnthropicClientOptions.Timeout to 100 seconds; this then differs.
        Assert.Equal(TimeSpan.FromMinutes(10), new AnthropicClientOptions().Timeout);
    }

    [Fact]
    public void The_options_default_to_the_documented_values()
    {
        var options = new AnthropicClientOptions();

        // Red: default BaseAddress to https://api.anthropic.com/v1/; this then differs.
        Assert.Equal(new Uri("https://api.anthropic.com/"), options.BaseAddress);
        // Red: default AnthropicVersion to "2024-01-01"; this then differs.
        Assert.Equal("2023-06-01", options.AnthropicVersion);
        // Red: default MaxRetries to 3; this then differs.
        Assert.Equal(2, options.MaxRetries);
        // Red: default ApiKey to Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "x"; it is then set.
        Assert.Null(options.ApiKey);
    }

    [Fact]
    public async Task An_error_event_mid_stream_throws_AnthropicApiException()
    {
        _handler.Then(() => Responses.Stream(
            ("message_start", Responses.MessageStart),
            ("error", """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""")));
        var client = TestClients.Create(_http);
        var seen = new List<MessageStreamEvent>();

        // Red: yield the ErrorEvent instead of throwing; the enumeration then ends without an exception.
        var failure = await Assert.ThrowsAsync<AnthropicApiException>(
            () => TestClients.CollectAsync(client, seen).WaitAsync(Bound));

        // Red: build the error with Type "error" instead of the event's error type; this then differs.
        Assert.Equal("overloaded_error", failure.Error.Type);
        // Red: build the error with Message "" instead of the event's message; this then differs.
        Assert.Equal("Overloaded", failure.Error.Message);
        // Red: give a mid-stream error the status 200; this then differs.
        Assert.Equal(0, failure.Error.Status);
        // Red: throw before yielding message_start; seen is then empty.
        Assert.IsType<MessageStartEvent>(Assert.Single(seen));
        // Red: retry an error event; the handler then gets a second request and throws for want of a response.
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_stream_without_message_stop_throws_stream_incomplete()
    {
        _handler.Then(() => Responses.Stream(
            ("message_start", Responses.MessageStart),
            ("content_block_start", Responses.BlockStart),
            ("content_block_delta", Responses.BlockDelta)));
        var client = TestClients.Create(_http);
        var seen = new List<MessageStreamEvent>();

        // Red: drop the message_stop check after the loop; the enumeration then ends cleanly.
        var failure = await Assert.ThrowsAsync<AnthropicApiException>(
            () => TestClients.CollectAsync(client, seen).WaitAsync(Bound));

        // Red: name the error "incomplete"; this then differs.
        Assert.Equal("stream_incomplete", failure.Error.Type);
        // Red: give it the status 200; this then differs.
        Assert.Equal(0, failure.Error.Status);
        // Red: buffer the events and throw before yielding any; seen is then empty.
        Assert.Equal(3, seen.Count);
    }

    // Anthropic's real 401 body. Its message names the header, which is not a secret; a header's value is.
    private const string Real401Body =
        """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"},"request_id":"req_401"}""";

    private const string FakeBearerToken = "sk-ant-oat-fake-456";

    [Fact]
    public async Task No_exception_text_contains_the_api_key()
    {
        var texts = await FailureTextsAsync(TestClients.Create(_http));

        foreach (var text in texts)
        {
            // Red: in Translate, pass new InvalidOperationException("x-api-key: sk-ant-test-123") as the inner
            // exception, as a dump of the request headers would; failure.ToString() then holds the key.
            Assert.DoesNotContain(TestClients.FakeKey, text, StringComparison.Ordinal);
            // Red: the same mutation; the dump fragment "x-api-key: <value>" then appears.
            Assert.DoesNotMatch(@"(?i)x-api-key\s*:\s*\S", text);
        }
    }

    [Fact]
    public async Task No_exception_text_contains_a_bearer_token()
    {
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", FakeBearerToken);
        var texts = await FailureTextsAsync(TestClients.Create(_http, o => o.ApiKey = null));

        foreach (var text in texts)
        {
            // Red: in Translate, pass new InvalidOperationException("Authorization: Bearer sk-ant-oat-fake-456")
            // as the inner exception; failure.ToString() then holds the token.
            Assert.DoesNotContain(FakeBearerToken, text, StringComparison.Ordinal);
            // Red: the same mutation; the dump fragment "Authorization: <value>" then appears.
            Assert.DoesNotMatch(@"(?i)authorization\s*:\s*\S", text);
        }
    }

    // A 401 through both methods, and every text a caller could log from the failures.
    private async Task<string[]> FailureTextsAsync(AnthropicClient client)
    {
        _handler.Always(() => Responses.Json(HttpStatusCode.Unauthorized, Real401Body));

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None)
            .WaitAsync(Bound).ConfigureAwait(false);
        var failure = await Assert.ThrowsAsync<AnthropicApiException>(() => TestClients.CollectAsync(client).WaitAsync(Bound))
            .ConfigureAwait(false);

        // Red: return a success for a 401; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: map a 401 to Type "http_401", ignoring the body; this then differs.
        Assert.Equal("authentication_error", failure.Error.Type);
        // Red: drop the message from the body read; the server's message is then empty.
        Assert.Equal("invalid x-api-key", failure.Error.Message);
        return
        [
            result.Error.ToString(),
            new AnthropicApiException(result.Error).ToString(),
            failure.ToString(),
            failure.Error.ToString(),
        ];
    }

    [Fact]
    public async Task A_caller_that_breaks_early_gets_no_stream_incomplete()
    {
        _handler.Then(() => Responses.Stream(Responses.FullStream()));
        var client = TestClients.Create(_http);
        MessageStreamEvent? first = null;

        // Red: move the !stopped check into a finally around the loop; disposing after the break then throws here.
        await foreach (var e in client.StreamMessageAsync(TestClients.Request(), CancellationToken.None).ConfigureAwait(false))
        {
            first = e;
            break;
        }

        // Red: skip the first event in the facade; first is then a ContentBlockStartEvent.
        Assert.IsType<MessageStartEvent>(first);
    }

    [Fact]
    public async Task An_error_event_without_its_error_object_still_throws()
    {
        _handler.Then(() => Responses.Stream(
            ("message_start", Responses.MessageStart),
            ("error", """{"type":"error"}""")));
        var client = TestClients.Create(_http);

        // Red: match only ErrorEvent { Error: { } } in the facade; the event is then yielded and the stream ends
        // with stream_incomplete instead.
        var failure = await Assert.ThrowsAsync<AnthropicApiException>(() => TestClients.CollectAsync(client).WaitAsync(Bound));

        // Red: the same mutation; Type is then "stream_incomplete".
        Assert.Equal("error", failure.Error.Type);
        // Red: build the fallback with an empty message; this then differs.
        Assert.Equal("The stream sent an error event without details.", failure.Error.Message);
    }

    [Fact]
    public async Task A_timeout_returns_the_timeout_error_and_is_not_retried()
    {
        _handler.AlwaysAsync(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            return Responses.Message();
        });
        var client = TestClients.Create(_http, o => o.Timeout = TimeSpan.FromMilliseconds(200));

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: drop the Timeout assignment from Create; the 100 second default then trips WaitAsync.
        Assert.True(result.IsFailure);
        // Red: map HttpErrorKind.Timeout to "http_0"; this then differs.
        Assert.Equal("timeout", result.Error.Type);
        // Red: map a timeout to status 408; this then differs.
        Assert.Equal(0, result.Error.Status);
        // Red: make IsTransient(AnthropicApiError) true for status 0; there are then three requests.
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task AddAnthropicClient_resolves_a_client_that_sends_the_configured_key()
    {
        _handler.Always(Responses.Message);
        var services = new ServiceCollection();
        services.AddAnthropicClient(o =>
        {
            o.ApiKey = TestClients.FakeKey;
            o.BaseAddress = new Uri("http://fake.local/");
        }).ConfigurePrimaryHttpMessageHandler(() => _handler);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<AnthropicClient>();
        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: register the HttpClient without ConfigureHttpClient's BaseAddress; the call then fails.
        Assert.True(result.IsSuccess);
        var request = Assert.Single(_handler.Requests);
        // Red: ignore options.BaseAddress in AddAnthropicClient; the URI is then api.anthropic.com.
        Assert.Equal(new Uri("http://fake.local/v1/messages"), request.Uri);
        // Red: drop the x-api-key header from the DI path; the lookup then throws KeyNotFoundException.
        Assert.Equal(TestClients.FakeKey, request.Headers["x-api-key"]);
        // Red: drop the anthropic-version header from the DI path; the lookup then throws.
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
    }

    [Fact]
    public void AddAnthropicClient_gives_the_http_client_the_ten_minute_timeout()
    {
        var services = new ServiceCollection();
        services.AddAnthropicClient(o => o.ApiKey = TestClients.FakeKey);
        using var provider = services.BuildServiceProvider();

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IAnthropicMessagesApi));

        // Red: drop the Timeout assignment from the DI path; IHttpClientFactory's default is 100 seconds.
        Assert.Equal(TimeSpan.FromMinutes(10), http.Timeout);
    }

    [Fact]
    public async Task AddAnthropicClient_applies_max_retries()
    {
        _handler.Always(() => Responses.Overloaded());
        var services = new ServiceCollection();
        services.AddAnthropicClient(o =>
        {
            o.ApiKey = TestClients.FakeKey;
            o.MaxRetries = 0;
        }).ConfigurePrimaryHttpMessageHandler(() => _handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<AnthropicClient>()
            .CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: return a success for a 529; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: register the policies without MaxRetries, the attribute's MaxAttempts = 3 then applies.
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_second_AddAnthropicClient_call_wins_over_the_first()
    {
        _handler.Always(() => Responses.Overloaded());
        var services = new ServiceCollection();
        services.AddAnthropicClient(o => o.ApiKey = "sk-ant-first-fake").ConfigurePrimaryHttpMessageHandler(() => _handler);
        services.AddAnthropicClient(o =>
        {
            o.ApiKey = TestClients.FakeKey;
            o.MaxRetries = 0;
        }).ConfigurePrimaryHttpMessageHandler(() => _handler);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<AnthropicClient>()
            .CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: register the policies with TryAddSingleton; the first call's MaxRetries 2 then gives 3 requests.
        var request = Assert.Single(_handler.Requests);
        // Red: drop headers.Remove(ApiKeyHeader) from ConfigureHttpClient; both keys are then sent.
        Assert.Equal(TestClients.FakeKey, request.Headers["x-api-key"]);
    }
}
