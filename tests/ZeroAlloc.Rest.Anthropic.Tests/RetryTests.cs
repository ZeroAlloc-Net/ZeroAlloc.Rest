using System.Diagnostics;
using System.Net;
using Xunit;
using ZeroAlloc.Rest.Anthropic.Tests.Support;

namespace ZeroAlloc.Rest.Anthropic.Tests;

public sealed class RetryTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly FakeAnthropicHandler _handler = new();
    private readonly HttpClient _http;

    public RetryTests() => _http = new HttpClient(_handler);

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task A_429_is_retried_and_honours_retry_after()
    {
        _handler.Then(() => Responses.Error(429, "rate_limit_error", "Slow down", retryAfter: "1")).Then(Responses.Message);
        var client = TestClients.Create(_http);

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: drop 429 from IsTransient(AnthropicApiError); the 429 is then returned as the result.
        Assert.True(result.IsSuccess);
        var requests = _handler.Requests;
        // Red: the same mutation; there is then one request.
        Assert.Equal(2, requests.Count);
        // Red: drop DelayHint from [Retry]; the 500 ms backoff with at most 50% jitter is then under 950 ms.
        Assert.True(
            Stopwatch.GetElapsedTime(requests[0].Timestamp, requests[1].Timestamp) >= TimeSpan.FromMilliseconds(950),
            "The retry came before the retry-after of 1 second.");
    }

    [Theory]
    [InlineData(529, "overloaded_error")]
    [InlineData(503, "api_error")]
    [InlineData(500, "api_error")]
    public async Task A_529_and_a_503_are_retried(int status, string type)
    {
        _handler.Then(() => Responses.Error(status, type, "Try again", retryAfter: "0")).Then(Responses.Message);
        var client = TestClients.Create(_http);

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: make IsTransient(AnthropicApiError) true for 429 only; the failure is then returned.
        Assert.True(result.IsSuccess);
        // Red: the same mutation; there is then one request.
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Theory]
    [InlineData(400, "invalid_request_error")]
    [InlineData(401, "authentication_error")]
    [InlineData(404, "not_found_error")]
    public async Task A_400_is_not_retried(int status, string type)
    {
        _handler.Always(() => Responses.Error(status, type, "No", retryAfter: "0"));
        var client = TestClients.Create(_http);

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: return a success for a 4xx; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: make IsTransient(AnthropicApiError) true for every status of 400 and up; there are then 3 requests.
        Assert.Single(_handler.Requests);
        // Red: map the error with Type "http_<status>" always; this then differs.
        Assert.Equal(type, result.Error.Type);
    }

    [Fact]
    public async Task Max_retries_two_means_three_attempts()
    {
        _handler.Always(() => Responses.Overloaded());
        var client = TestClients.Create(_http);

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: return a success for a 529; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: build the policy with maxAttempts: MaxRetries instead of MaxRetries + 1; there are then 2 requests.
        Assert.Equal(3, _handler.Requests.Count);
        // Red: map the last failure to http_529; this then differs.
        Assert.Equal(529, result.Error.Status);
    }

    [Fact]
    public async Task Max_retries_zero_means_one_attempt()
    {
        _handler.Always(() => Responses.Overloaded());
        var client = TestClients.Create(_http, o => o.MaxRetries = 0);

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: return a success for a 529; IsFailure is then false.
        Assert.True(result.IsFailure);
        // Red: build the policy with a fixed maxAttempts: 3; there are then 3 requests.
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_transport_failure_is_not_retried()
    {
        _handler.Always(() => throw new HttpRequestException("Connection refused"));
        var client = TestClients.Create(_http);

        var result = await client.CreateMessageAsync(TestClients.Request(), CancellationToken.None).WaitAsync(Bound);

        // Red: make IsTransient(AnthropicApiError) true for status 0; there are then 3 requests.
        Assert.Single(_handler.Requests);
        // Guards the generated client, which returns a transport failure as a Result instead of throwing. It cannot
        // be made red from this project: ZeroAlloc.Rest's generator owns that rule.
        Assert.True(result.IsFailure);
        // Red: map HttpErrorKind.Transport to "timeout"; this then differs.
        Assert.Equal("connection_error", result.Error.Type);
        // Red: give a transport failure the status 503; this then differs.
        Assert.Equal(0, result.Error.Status);
    }

    [Fact]
    public async Task A_stream_is_retried_on_529_before_its_first_event_and_never_after()
    {
        _handler.Then(() => Responses.Overloaded()).Then(() => Responses.Stream(Responses.FullStream()));
        var client = TestClients.Create(_http);

        // Red: drop 529 from IsTransient(Exception); the 529 then ends the stream here with AnthropicApiException.
        var seen = await TestClients.CollectAsync(client).WaitAsync(Bound);

        // Red: break out of the facade's loop after message_start; the count is then 1.
        Assert.Equal(7, seen.Count);
        // Red: retry once more after a success, by enumerating the inner stream twice in the facade; the count is then 3.
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task A_stream_whose_connection_drops_after_message_start_is_not_retried()
    {
        _handler.Always(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DroppingContent(Responses.Sse(("message_start", Responses.MessageStart))),
        });
        var client = TestClients.Create(_http);
        var seen = new List<MessageStreamEvent>();

        // Red: catch the IOException in the facade and end the stream cleanly; nothing is then thrown.
        var failure = await Assert.ThrowsAsync<AnthropicApiException>(
            () => TestClients.CollectAsync(client, seen).WaitAsync(Bound));

        // Red: build the IOException's AnthropicApiException without the exception as its inner one; this is then null.
        Assert.IsType<IOException>(failure.InnerException);
        // Red: name the dropped connection "timeout"; this then differs.
        Assert.Equal("connection_error", failure.Error.Type);
        // Guards against a library regression that retries after the first item. It cannot be made red
        // from this repo: the Resilience library owns that rule.
        Assert.Single(_handler.Requests);
        // Red: send the first chunk with message_delta instead; seen then holds a MessageDeltaEvent.
        Assert.IsType<MessageStartEvent>(Assert.Single(seen));
    }

    [Fact]
    public async Task A_stream_whose_retries_run_out_throws_the_mapped_error()
    {
        _handler.Always(() => Responses.Json(
            (HttpStatusCode)529,
            Responses.ErrorBody("overloaded_error", "Overloaded", "req_9"),
            ("retry-after", "0")));
        var client = TestClients.Create(_http);

        // Red: let the ResilienceException through from the facade; ThrowsAsync then fails on its type.
        var failure = await Assert.ThrowsAsync<AnthropicApiException>(() => TestClients.CollectAsync(client).WaitAsync(Bound));

        // Red: build the policy with maxAttempts: 1; there is then one request.
        Assert.Equal(3, _handler.Requests.Count);
        // Red: map every unwrapped error to new AnthropicApiError("http_529", "", 529, null, null); this then differs.
        Assert.Equal(new AnthropicApiError("overloaded_error", "Overloaded", 529, "req_9", TimeSpan.Zero), failure.Error);
    }

    [Fact]
    public async Task A_stream_400_is_not_retried_and_throws_the_mapped_error()
    {
        _handler.Always(() => Responses.Error(400, "invalid_request_error", "Bad"));
        var client = TestClients.Create(_http);

        // Red: let the HttpErrorException through from the facade; ThrowsAsync then fails on its type.
        var failure = await Assert.ThrowsAsync<AnthropicApiException>(() => TestClients.CollectAsync(client).WaitAsync(Bound));

        // Red: make IsTransient(Exception) true for every HttpRequestException; there are then 3 requests.
        Assert.Single(_handler.Requests);
        // Red: map the 400 to http_400; this then differs.
        Assert.Equal("invalid_request_error", failure.Error.Type);
        // Red: give it the status 0; this then differs.
        Assert.Equal(400, failure.Error.Status);
    }
}
