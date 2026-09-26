using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

[ZeroAllocRestClient(MaxErrorBodyBytes = 16)]
public interface ISmallCapApi
{
    [Get("/users/{id}/result")]
    Task<Result<UserDto, HttpError>> GetUserResultAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient(MaxErrorBodyBytes = 0)]
public interface INoErrorBodyApi
{
    [Get("/users/{id}/result")]
    Task<Result<UserDto, HttpError>> GetUserResultAsync(int id, CancellationToken ct = default);
}

// Issue #298: an HttpError carries the error response's headers, content headers included, its
// body and its media type.
public sealed class ResultErrorBodyTests
{
    private const string ProblemJson = """{"type":"validation","code":"field_required","field":"name"}""";

    private static readonly Uri s_baseAddress = new("http://stub.local/");

    [Fact]
    public async Task StatusError_Headers_IncludeContentHeaders()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal("application/problem+json; charset=utf-8", result.Error.Headers["Content-Type"][0]);
        Assert.Equal("nl-NL", result.Error.Headers["content-language"][0]);
        Assert.Equal("req-9", result.Error.Headers["X-Request-Id"][0]);
    }

    [Fact]
    public async Task DeserializationError_Headers_IncludeContentHeaders()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.OK, "{ not json", "application/json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Equal("application/json; charset=utf-8", result.Error.Headers["Content-Type"][0]);
        Assert.Equal("nl-NL", result.Error.Headers["Content-Language"][0]);
    }

    [Fact]
    public async Task StatusError_CarriesTheBody_AndItsMediaType()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(Encoding.UTF8.GetBytes(ProblemJson), result.Error.Body.ToArray());
        Assert.Equal("application/problem+json", result.Error.ContentType);
        Assert.False(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_WithoutABody_HasAnEmptyBody()
    {
        using var httpClient = CreateHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.Error.Body.IsEmpty);
        Assert.Null(result.Error.ContentType);
        Assert.False(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_BodyOverTheCap_IsTruncatedToTheCap()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        ISmallCapApi client = new SmallCapApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(Encoding.UTF8.GetBytes(ProblemJson)[..16], result.Error.Body.ToArray());
        Assert.True(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_BodyAtTheCap_IsNotTruncated()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.BadRequest, "0123456789abcdef", "text/plain"));
        ISmallCapApi client = new SmallCapApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(16, result.Error.Body.Length);
        Assert.False(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_WithoutContentLength_StillCarriesTheBody()
    {
        var bytes = Encoding.UTF8.GetBytes(ProblemJson);
        using var httpClient = CreateHttpClient((_, _) =>
        {
            var content = new StreamContent(new UnseekableStream(bytes));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/problem+json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = content });
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(bytes, result.Error.Body.ToArray());
        Assert.Equal("application/problem+json", result.Error.ContentType);
    }

    [Fact]
    public async Task CapZero_ReadsNoBody_ButKeepsTheMediaType()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        INoErrorBodyApi client = new NoErrorBodyApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.Error.Body.IsEmpty);
        Assert.False(result.Error.BodyTruncated);
        Assert.Equal("application/problem+json", result.Error.ContentType);
    }

    [Fact]
    public async Task CallerCancellationBeforeTheResponse_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, _) =>
        {
            cts.Cancel();
            return Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json");
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserResultAsync(1, cts.Token));
    }

    [Fact]
    public async Task TransportAndTimeoutErrors_HaveNoBodyAndNoContentType()
    {
        using var refusing = CreateHttpClient((_, _) => throw new HttpRequestException("refused"));
        using var hanging = CreateHttpClient(HangAsync, TimeSpan.FromMilliseconds(50));

        IUserApi refusingClient = new UserApiClient(refusing, new SystemTextJsonSerializer());
        IUserApi hangingClient = new UserApiClient(hanging, new SystemTextJsonSerializer());

        var transport = await refusingClient.GetUserResultAsync(1);
        var timeout = await hangingClient.GetUserResultAsync(1);

        Assert.True(transport.Error.Body.IsEmpty);
        Assert.Null(transport.Error.ContentType);
        Assert.True(timeout.Error.Body.IsEmpty);
        Assert.Null(timeout.Error.ContentType);
    }

    [Fact]
    public async Task StatusError_RetryAfterDeltaSeconds_IsReadable()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            RespondWithRetryAfter(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(TimeSpan.FromSeconds(120), result.Error.GetRetryAfter());
    }

    [Fact]
    public async Task StatusError_RetryAfterHttpDate_IsReadable()
    {
        var now = new DateTimeOffset(2015, 10, 21, 7, 27, 0, TimeSpan.Zero);
        var retryAt = new DateTimeOffset(2015, 10, 21, 7, 28, 0, TimeSpan.Zero);
        using var httpClient = CreateHttpClient((_, _) =>
            RespondWithRetryAfter(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(retryAt)));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(TimeSpan.FromMinutes(1), result.Error.GetRetryAfter(new FixedTimeProvider(now)));
    }

    [Fact]
    public async Task DeserializationError_HasNoBody_ButHasTheMediaType()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.OK, "{ not json", "application/json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.True(result.Error.Body.IsEmpty);
        Assert.Equal("application/json", result.Error.ContentType);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHandler(send)) { BaseAddress = s_baseAddress };
        if (timeout is { } t)
            httpClient.Timeout = t;
        return httpClient;
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body, string mediaType)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
        response.Content.Headers.ContentLanguage.Add("nl-NL");
        response.Headers.Add("X-Request-Id", "req-9");
        return Task.FromResult(response);
    }

    private static async Task<HttpResponseMessage> HangAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        throw new InvalidOperationException("unreachable");
    }

    private static Task<HttpResponseMessage> RespondWithRetryAfter(HttpStatusCode status, RetryConditionHeaderValue retryAfter)
    {
        var response = new HttpResponseMessage(status) { Headers = { RetryAfter = retryAfter } };
        return Task.FromResult(response);
    }

    // StreamContent reports no Content-Length for a stream it cannot seek.
    private sealed class UnseekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
