using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

// Issue #362: a [Body] parameter is serialized into a pooled buffer, which goes back to the pool
// when the request is disposed. These tests check what reaches the handler, and that the request
// content is disposed, which returns the buffer, on success, on a failed send and on cancellation.
public sealed class RequestBodyTests
{
    private static readonly Uri s_baseAddress = new("http://stub.local/");
    private static readonly JsonSerializerOptions s_camelCase = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Body_IsSentWithItsContentTypeAndLength()
    {
        byte[]? sent = null;
        long? contentLength = null;
        string? contentType = null;
        using var httpClient = CreateHttpClient(async (request, ct) =>
        {
            contentLength = request.Content!.Headers.ContentLength;
            contentType = request.Content.Headers.ContentType?.MediaType;
            sent = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return UserResponse();
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await client.CreateUserAsync(new CreateUserRequest("Bob"));

        var expected = JsonSerializer.SerializeToUtf8Bytes(new CreateUserRequest("Bob"), s_camelCase);
        Assert.Equal(expected, sent);
        Assert.Equal(expected.Length, contentLength);
        Assert.Equal("application/json", contentType);
    }

    [Fact]
    public async Task Body_IsCopiedToTheWire_ByTheTransport()
    {
        byte[]? sent = null;
        using var httpClient = CreateHttpClient(async (request, ct) =>
        {
            // CopyToAsync is how a transport writes a body to the socket.
            using var wire = new MemoryStream();
            await request.Content!.CopyToAsync(wire, ct).ConfigureAwait(false);
            sent = wire.ToArray();
            return UserResponse();
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await client.CreateUserAsync(new CreateUserRequest("Bob"));

        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(new CreateUserRequest("Bob"), s_camelCase), sent);
    }

    [Fact]
    public async Task Body_CanBeSentAgain_ByAHandlerThatRetries()
    {
        var attempts = new List<byte[]>();
        using var httpClient = CreateHttpClient(async (request, ct) =>
        {
            // A retrying DelegatingHandler sends the same content more than once.
            attempts.Add(await CopyAsync(request.Content!, ct).ConfigureAwait(false));
            attempts.Add(await CopyAsync(request.Content!, ct).ConfigureAwait(false));
            return UserResponse();
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await client.CreateUserAsync(new CreateUserRequest("Bob"));

        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0], attempts[1]);
        Assert.NotEmpty(attempts[0]);
    }

    [Fact]
    public async Task Content_IsDisposed_AfterASuccessfulCall()
    {
        HttpContent? content = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            content = request.Content;
            return Task.FromResult(UserResponse());
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await client.CreateUserAsync(new CreateUserRequest("Bob"));

        await AssertDisposedAsync(content);
    }

    [Fact]
    public async Task Content_IsDisposed_WhenTheSendThrows()
    {
        HttpContent? content = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            content = request.Content;
            throw new HttpRequestException("connection refused");
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateUserAsync(new CreateUserRequest("Bob")));

        await AssertDisposedAsync(content);
    }

    [Fact]
    public async Task Content_IsDisposed_WhenTheCallerCancels()
    {
        HttpContent? content = null;
        using var cts = new CancellationTokenSource();
        using var httpClient = CreateHttpClient(async (request, ct) =>
        {
            content = request.Content;
            await cts.CancelAsync().ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return UserResponse();
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CreateUserAsync(new CreateUserRequest("Bob"), cts.Token));

        await AssertDisposedAsync(content);
    }

    [Fact]
    public async Task CancelledBeforeTheCall_SendsNothing()
    {
        var sent = false;
        using var httpClient = CreateHttpClient((_, _) =>
        {
            sent = true;
            return Task.FromResult(UserResponse());
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CreateUserAsync(new CreateUserRequest("Bob"), new CancellationToken(canceled: true)));

        Assert.False(sent);
    }

    private static async Task AssertDisposedAsync(HttpContent? content)
    {
        Assert.NotNull(content);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => content.ReadAsByteArrayAsync()).ConfigureAwait(false);
    }

    private static async Task<byte[]> CopyAsync(HttpContent content, CancellationToken ct)
    {
        using var wire = new MemoryStream();
        await content.CopyToAsync(wire, ct).ConfigureAwait(false);
        return wire.ToArray();
    }

    private static HttpResponseMessage UserResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new UserDto(2, "Bob"), s_camelCase), Encoding.UTF8, "application/json"),
    };

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new StubHandler(send)) { BaseAddress = s_baseAddress };
}
