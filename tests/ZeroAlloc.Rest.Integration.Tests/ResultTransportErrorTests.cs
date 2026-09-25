using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MemoryPack;
using MessagePack;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.MemoryPack;
using ZeroAlloc.Rest.MessagePack;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

// Issue #299: a method returning Result<T, HttpError> returns a failure for transport, timeout and
// response-deserialization errors instead of throwing. Caller cancellation still throws, and methods
// that do not return a Result keep throwing.

[MemoryPackable]
public sealed partial record MemoryPackUserDto(int Id, string Name);

[MessagePackObject]
public sealed class MessagePackUserDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

[ZeroAllocRestClient]
public interface IBinaryUserApi
{
    [Get("/users/{id}/memorypack")]
    [Serializer(typeof(MemoryPackRestSerializer))]
    Task<Result<MemoryPackUserDto, HttpError>> GetMemoryPackUserAsync(int id, CancellationToken ct = default);

    [Get("/users/{id}/messagepack")]
    [Serializer(typeof(MessagePackRestSerializer))]
    Task<Result<MessagePackUserDto, HttpError>> GetMessagePackUserAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient]
public interface INoTokenApi
{
    [Get("/users/{id}/result")]
    Task<Result<UserDto, HttpError>> GetUserResultAsync(int id);
}

public sealed class ResultTransportErrorTests
{
    private static readonly Uri s_baseAddress = new("http://stub.local/");

    [Fact]
    public async Task ResultMethod_TransportFailure_ReturnsTransportError()
    {
        var cause = new HttpRequestException("connection refused");
        using var httpClient = CreateHttpClient((_, _) => throw cause);
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
        Assert.Same(cause, result.Error.Exception);
        Assert.Equal((HttpStatusCode)0, result.Error.StatusCode);
        Assert.Equal("connection refused", result.Error.Message);
        Assert.Empty(result.Error.Headers);
    }

    [Fact]
    public async Task ResultMethod_TransportFailureWithStatusCode_KeepsThatStatusCode()
    {
        var cause = new HttpRequestException("bad gateway", inner: null, HttpStatusCode.BadGateway);
        using var httpClient = CreateHttpClient((_, _) => throw cause);
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
        Assert.Equal(HttpStatusCode.BadGateway, result.Error.StatusCode);
    }

    [Fact]
    public async Task ResultMethod_HttpClientTimeout_ReturnsTimeoutError()
    {
        using var httpClient = CreateHttpClient(HangAsync, TimeSpan.FromMilliseconds(50));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Timeout, result.Error.Kind);
        Assert.IsAssignableFrom<OperationCanceledException>(result.Error.Exception);
        Assert.Equal((HttpStatusCode)0, result.Error.StatusCode);
        Assert.Empty(result.Error.Headers);
    }

    [Fact]
    public async Task ResultMethod_WithoutTokenParameter_HttpClientTimeout_ReturnsTimeoutError()
    {
        using var httpClient = CreateHttpClient(HangAsync, TimeSpan.FromMilliseconds(50));
        var client = new NoTokenApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Timeout, result.Error.Kind);
    }

    [Fact]
    public async Task ResultMethod_MalformedSuccessBody_ReturnsDeserializationError()
    {
        using var httpClient = CreateHttpClient((_, _) => RespondJson(HttpStatusCode.OK, "{ not json", "req-42"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.IsAssignableFrom<JsonException>(result.Error.Exception);
        Assert.Equal(HttpStatusCode.OK, result.Error.StatusCode);
        Assert.Equal("req-42", result.Error.Headers["X-Request-Id"][0]);
    }

    [Fact]
    public async Task ResultMethod_NonSuccessStatus_ReturnsStatusErrorWithoutException()
    {
        using var httpClient = CreateHttpClient((_, _) => RespondJson(HttpStatusCode.NotFound, "", "req-7"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Null(result.Error.Exception);
        Assert.Null(result.Error.Message);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.StatusCode);
        Assert.Equal("req-7", result.Error.Headers["X-Request-Id"][0]);
    }

    [Fact]
    public async Task ResultMethod_CallerCancellation_Throws()
    {
        using var httpClient = CreateHttpClient(HangAsync);
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserResultAsync(1, cts.Token));
    }

    [Fact]
    public async Task ResultMethod_CallerCancellationDuringDeserialization_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, _) => RespondJson(HttpStatusCode.OK, "{}", "req-1"));
        IUserApi client = new UserApiClient(httpClient, new CancellingSerializer(cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserResultAsync(1, cts.Token));
    }

    [Fact]
    public async Task ResultMethod_UnrequestedCancellationDuringDeserialization_ReturnsTimeoutError()
    {
        using var unrelated = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, _) => RespondJson(HttpStatusCode.OK, "{}", "req-1"));
        IUserApi client = new UserApiClient(httpClient, new CancellingSerializer(unrelated));

        var result = await client.GetUserResultAsync(1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Timeout, result.Error.Kind);
    }

    [Fact]
    public async Task ResultMethod_UnrelatedException_StillThrows()
    {
        // Only transport, timeout and response-deserialization failures become an HttpError.
        using var httpClient = CreateHttpClient((_, _) => throw new InvalidOperationException("bug"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetUserResultAsync(1));
    }

    [Fact]
    public async Task NonResultMethod_TransportFailure_StillThrows()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("connection refused"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUserAsync(1));
    }

    [Fact]
    public async Task NonResultMethod_HttpClientTimeout_StillThrows()
    {
        using var httpClient = CreateHttpClient(HangAsync, TimeSpan.FromMilliseconds(50));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserAsync(1));
    }

    [Fact]
    public async Task NonResultMethod_MalformedSuccessBody_StillThrows()
    {
        using var httpClient = CreateHttpClient((_, _) => RespondJson(HttpStatusCode.OK, "{ not json", "req-42"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<JsonException>(() => client.GetUserAsync(1));
    }

    [Fact]
    public async Task ResultMethod_MemoryPackDeserializationFailure_ReturnsDeserializationError()
    {
        // Two members announced, then the buffer ends: MemoryPack throws its own exception type.
        using var httpClient = CreateHttpClient((_, _) => RespondBytes(HttpStatusCode.OK, [2, 1]));
        var client = CreateBinaryClient(httpClient);

        var result = await client.GetMemoryPackUserAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.IsType<MemoryPackSerializationException>(result.Error.Exception);
        Assert.Equal(HttpStatusCode.OK, result.Error.StatusCode);
    }

    [Fact]
    public async Task ResultMethod_MessagePackDeserializationFailure_ReturnsDeserializationError()
    {
        // 0xC1 is the one byte the MessagePack format never uses.
        using var httpClient = CreateHttpClient((_, _) => RespondBytes(HttpStatusCode.OK, [0xC1]));
        var client = CreateBinaryClient(httpClient);

        var result = await client.GetMessagePackUserAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.IsType<MessagePackSerializationException>(result.Error.Exception);
        Assert.Equal(HttpStatusCode.OK, result.Error.StatusCode);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHandler(send)) { BaseAddress = s_baseAddress };
        if (timeout is { } t)
            httpClient.Timeout = t;
        return httpClient;
    }

    private static IBinaryUserApi CreateBinaryClient(HttpClient httpClient)
        => new BinaryUserApiClient(httpClient, new SystemTextJsonSerializer(), new MemoryPackRestSerializer(), new MessagePackRestSerializer());

    private static async Task<HttpResponseMessage> HangAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        throw new InvalidOperationException("unreachable");
    }

    private static Task<HttpResponseMessage> RespondJson(HttpStatusCode status, string body, string requestId)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        response.Headers.Add("X-Request-Id", requestId);
        return Task.FromResult(response);
    }

    private static Task<HttpResponseMessage> RespondBytes(HttpStatusCode status, byte[] body)
        => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    // Cancels the given source, then throws the OperationCanceledException a serializer throws when
    // it observes that token.
    private sealed class CancellingSerializer(CancellationTokenSource source) : IRestSerializer
    {
        public string ContentType => "application/json";

        [RequiresDynamicCode("Test serializer.")]
        [RequiresUnreferencedCode("Test serializer.")]
        public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        {
            source.Cancel();
            throw new OperationCanceledException(source.Token);
        }

        [RequiresDynamicCode("Test serializer.")]
        [RequiresUnreferencedCode("Test serializer.")]
        public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
