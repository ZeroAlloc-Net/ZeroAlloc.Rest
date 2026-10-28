using System.Net;
using System.Text;
using Xunit;
using ZeroAlloc.Rest.Anthropic.Tests.Support;

namespace ZeroAlloc.Rest.Anthropic.Tests;

public sealed class ErrorMappingTests
{
    private static readonly AnthropicErrorMapper Mapper = new();

    private static HttpError Status(int status, string body, params (string Name, string Value)[] headers) =>
        new((HttpStatusCode)status, Headers(headers))
        {
            Body = Encoding.UTF8.GetBytes(body),
            ContentType = "application/json",
        };

    private static Dictionary<string, IReadOnlyList<string>> Headers((string Name, string Value)[] headers) =>
        headers.ToDictionary(h => h.Name, h => (IReadOnlyList<string>)[h.Value], StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void The_error_body_maps_to_type_message_status_and_request_id()
    {
        var error = Mapper.Map(Status(
            529,
            """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"},"request_id":"req_1"}"""));

        // Red: stop ReadError from taking error.type; the body then reads as no error, Type "http_529".
        // Red: drop request_id from the body read; RequestId is then null.
        // Red: take Status from a constant 500 instead of the response; the record then differs.
        Assert.Equal(new AnthropicApiError("overloaded_error", "Overloaded", 529, "req_1", null), error);
    }

    [Fact]
    public void The_request_id_header_is_used_when_the_body_has_none()
    {
        var error = Mapper.Map(Status(
            500,
            Responses.ErrorBody("api_error", "Internal server error"),
            ("request-id", "req_header")));

        // Red: read the request id from the body only; RequestId is then null.
        Assert.Equal("req_header", error.RequestId);
    }

    [Fact]
    public void The_body_request_id_wins_over_the_header()
    {
        var error = Mapper.Map(Status(
            500,
            Responses.ErrorBody("api_error", "Internal server error", "req_body"),
            ("request-id", "req_header")));

        // Red: read the header before the body; RequestId is then "req_header".
        Assert.Equal("req_body", error.RequestId);
    }

    [Fact]
    public void An_unparsable_error_body_maps_to_http_status()
    {
        var error = Mapper.Map(Status(502, "<html>Bad gateway</html>", ("request-id", "req_gw")));

        // Red: name the fallback "unknown_error"; Type then differs.
        Assert.Equal("http_502", error.Type);
        // Red: give the fallback status 0; this then differs.
        Assert.Equal(502, error.Status);
        // Red: drop the header fallback for an unparsable body; RequestId is then null.
        Assert.Equal("req_gw", error.RequestId);
    }

    [Theory]
    [InlineData("""{"type":"error"}""")]
    [InlineData("""{"type":"error","error":{"message":"no type"}}""")]
    [InlineData("""[1,2]""")]
    [InlineData("")]
    public void A_body_without_an_error_type_maps_to_http_status(string body)
    {
        var error = Mapper.Map(Status(503, body));

        // Red: accept an error object without a type, as Type ""; this then differs.
        Assert.Equal("http_503", error.Type);
    }

    [Fact]
    public void A_timeout_maps_to_timeout_with_status_zero()
    {
        var error = Mapper.Map(new HttpError(default(HttpStatusCode), Headers([]))
        {
            Kind = HttpErrorKind.Timeout,
            Exception = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        });

        // Red: map Kind Timeout like Transport; Type is then "connection_error".
        Assert.Equal("timeout", error.Type);
        // Red: give a timeout the status 408; this then differs.
        Assert.Equal(0, error.Status);
    }

    [Fact]
    public void A_transport_failure_maps_to_connection_error_with_status_zero()
    {
        var error = Mapper.Map(new HttpError(default(HttpStatusCode), Headers([]))
        {
            Kind = HttpErrorKind.Transport,
            Exception = new HttpRequestException("Connection refused"),
        });

        // Red: map Kind Transport to "http_0"; this then differs.
        Assert.Equal("connection_error", error.Type);
        // Red: give a transport failure the status 503; this then differs.
        Assert.Equal(0, error.Status);
    }

    [Fact]
    public void An_unreadable_success_body_maps_to_invalid_response()
    {
        var error = Mapper.Map(new HttpError(HttpStatusCode.OK, Headers([]))
        {
            Kind = HttpErrorKind.Deserialization,
            Exception = new System.Text.Json.JsonException("bad"),
        });

        // Red: map Kind Deserialization through the body read; Type is then "http_200".
        Assert.Equal("invalid_response", error.Type);
        // Red: give it the status 0; this then differs.
        Assert.Equal(200, error.Status);
    }

    [Fact]
    public void Retry_after_is_read_in_seconds()
    {
        var error = Mapper.Map(Status(429, Responses.ErrorBody("rate_limit_error", "Slow down"), ("retry-after", "7")));

        // Red: read retry-after as milliseconds; this is then 7 ms.
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        // Red: leave RetryAfter null when the body parses; RetryAfter(error) is then null too.
        Assert.Equal(TimeSpan.FromSeconds(7), IAnthropicMessagesApi.RetryAfter(error));
    }

    [Fact]
    public void Retry_after_hints_reach_the_exception_overload()
    {
        var mapped = Mapper.Map(Status(529, Responses.ErrorBody("overloaded_error", "Overloaded"), ("retry-after", "3")));
        var thrown = new HttpErrorException<AnthropicApiError>(Status(529, string.Empty), mapped);

        // Red: return null from RetryAfter(Exception); this then differs.
        Assert.Equal(TimeSpan.FromSeconds(3), IAnthropicMessagesApi.RetryAfter(thrown));
        // Red: read the hint from any exception; an IOException then gets one.
        Assert.Null(IAnthropicMessagesApi.RetryAfter(new IOException("dropped")));
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(529, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(599, true)]
    [InlineData(400, false)]
    [InlineData(408, false)]
    [InlineData(409, false)]
    [InlineData(413, false)]
    [InlineData(0, false)]
    public void IsTransient_holds_for_429_529_and_every_5xx(int status, bool transient)
    {
        var error = new AnthropicApiError("t", "m", status, null, null);
        var thrown = new HttpErrorException<AnthropicApiError>(Status(status == 0 ? 500 : status, string.Empty), error);

        // Red: make IsTransient(AnthropicApiError) "status is 429 or 529"; the 500, 502 and 599 rows then fail.
        // Red: make it "status >= 400"; the 400, 408, 409 and 413 rows then fail.
        Assert.Equal(transient, IAnthropicMessagesApi.IsTransient(error));
        // Red: make IsTransient(Exception) read the HTTP status of the exception instead of the mapped error;
        // the status 0 row, built over a 500, then reads true.
        Assert.Equal(transient, IAnthropicMessagesApi.IsTransient(thrown));
    }

    [Fact]
    public void IsTransient_declines_transport_exceptions()
    {
        // Red: make IsTransient(Exception) true for every HttpRequestException; this is then true.
        Assert.False(IAnthropicMessagesApi.IsTransient(new HttpRequestException("refused")));
        // Red: make it true for IOException; this is then true.
        Assert.False(IAnthropicMessagesApi.IsTransient(new IOException("dropped")));
    }

    [Fact]
    public void The_exception_message_names_type_status_request_id_and_message()
    {
        var exception = new AnthropicApiException(new AnthropicApiError("overloaded_error", "Overloaded", 529, "req_1", null));

        // Red: drop the Type from the message format; this then fails.
        Assert.Contains("overloaded_error", exception.Message, StringComparison.Ordinal);
        // Red: drop the Status from the message format; this then fails.
        Assert.Contains("529", exception.Message, StringComparison.Ordinal);
        // Red: drop the RequestId from the message format; this then fails.
        Assert.Contains("req_1", exception.Message, StringComparison.Ordinal);
        // Red: drop the Message from the message format; this then fails.
        Assert.Contains(": Overloaded", exception.Message, StringComparison.Ordinal);
        // Red: build the exception with a fresh error instead of the one passed; this then differs.
        Assert.Equal("req_1", exception.Error.RequestId);
    }

    [Fact]
    public async Task A_failed_create_returns_the_mapped_error_body()
    {
        using var handler = new FakeAnthropicHandler();
        handler.Always(() => Responses.Json(
            HttpStatusCode.BadRequest,
            Responses.ErrorBody("invalid_request_error", "max_tokens: required", "req_7")));
        using var http = new HttpClient(handler);

        var result = await TestClients.Create(http).CreateMessageAsync(TestClients.Request(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Red: make the mapper ignore the body and return Type "http_400"; the record then differs.
        Assert.Equal(new AnthropicApiError("invalid_request_error", "max_tokens: required", 400, "req_7", null), result.Error);
    }
}
