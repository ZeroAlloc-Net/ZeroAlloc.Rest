using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

/// <summary>
/// Issue #378: serializing a [Body] parameter is part of the call. A serializer failure marks the
/// span Error and records one duration; a Result method returns it as a Deserialization failure
/// with no response, and a Task of T method still throws. A caller cancellation during
/// serialization leaves the span Unset with rest.cancelled = true. The pooled buffer the serializer
/// wrote into is released on every one of these paths.
/// </summary>
[Collection("rest-telemetry-non-parallel")]
public sealed class RequestBodySerializationTests
{
    private const string UserJson = "{\"id\":1,\"name\":\"Alice\"}";

    [Fact]
    public async Task TaskOfT_SerializerThrows_MarksSpanErrorAndThrows()
    {
        using var capture = new TelemetryCapture();
        var serializer = new ThrowingBodySerializer(new NotSupportedException("cannot write CreateUserRequest"));
        var handler = new CountingHandler();
        IUserApi client = Client(handler, serializer);

        var thrown = await CaptureAsync<NotSupportedException>(capture, () => client.CreateUserAsync(new CreateUserRequest("Bob")));

        Assert.Same(serializer.ToThrow, thrown);
        capture.AssertError("cannot write CreateUserRequest");
        Assert.Equal(0, handler.Calls);
        serializer.AssertBufferReleased();
    }

    [Fact]
    public async Task ResultMethod_SerializerThrows_ReturnsDeserializationFailure()
    {
        using var capture = new TelemetryCapture();
        var serializer = new ThrowingBodySerializer(new NotSupportedException("cannot write CreateUserRequest"));
        var handler = new CountingHandler();
        IUserApi client = Client(handler, serializer);

        Result<UserDto, HttpError> result = default;
        await capture.RunAsync(async () => result = await client.CreateUserResultAsync(new CreateUserRequest("Bob")).ConfigureAwait(false));

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Equal((HttpStatusCode)0, result.Error.StatusCode);
        Assert.Empty(result.Error.Headers);
        Assert.Null(result.Error.ContentType);
        Assert.True(result.Error.Body.IsEmpty);
        Assert.Same(serializer.ToThrow, result.Error.Exception);
        Assert.Equal("cannot write CreateUserRequest", result.Error.Message);
        capture.AssertError("cannot write CreateUserRequest");
        Assert.Equal(0, handler.Calls);
        serializer.AssertBufferReleased();
    }

    [Fact]
    public async Task MappedResultMethod_SerializerThrows_MapsTheFailure()
    {
        using var capture = new TelemetryCapture();
        var serializer = new ThrowingBodySerializer(new NotSupportedException("cannot write CreateUserRequest"));
        var handler = new CountingHandler();
        IMappedApi client = new MappedApiClient(HttpClientFor(handler), serializer, new DomainErrorMapper());

        Result<UserDto, DomainError> result = default;
        await capture.RunAsync(async () => result = await client.CreateThingAsync(new CreateUserRequest("Bob")).ConfigureAwait(false));

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Equal((HttpStatusCode)0, result.Error.Status);
        Assert.Equal("library", result.Error.Source);
        capture.AssertError("cannot write CreateUserRequest");
        Assert.Equal(0, handler.Calls);
        serializer.AssertBufferReleased();
    }

    [Fact]
    public async Task ResultMethod_SerializerThrowsHttpRequestException_IsStillASerializationFailure()
    {
        // A serializer's exception is classified by where it happened, not by its type, so it is
        // never mistaken for a Transport failure, and a status code it carries is not a response's.
        using var capture = new TelemetryCapture();
        var serializer = new ThrowingBodySerializer(
            new HttpRequestException("serializer looked like transport", null, HttpStatusCode.BadGateway));
        IUserApi client = Client(new CountingHandler(), serializer);

        Result<UserDto, HttpError> result = default;
        await capture.RunAsync(async () => result = await client.CreateUserResultAsync(new CreateUserRequest("Bob")).ConfigureAwait(false));

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Equal((HttpStatusCode)0, result.Error.StatusCode);
        Assert.Same(serializer.ToThrow, result.Error.Exception);
        capture.AssertError();
    }

    [Fact]
    public async Task TaskOfT_CallerCancelsDuringSerialization_LeavesSpanUnset()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var serializer = new CancelingBodySerializer(cts);
        var handler = new CountingHandler();
        IUserApi client = Client(handler, serializer);

        await CaptureAsync<OperationCanceledException>(capture, () => client.CreateUserAsync(new CreateUserRequest("Bob"), cts.Token));

        capture.AssertCancelled(expectResponseTags: false);
        Assert.Equal(0, handler.Calls);
        serializer.AssertBufferReleased();
    }

    [Fact]
    public async Task ResultMethod_CallerCancelsDuringSerialization_LeavesSpanUnsetAndThrows()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var serializer = new CancelingBodySerializer(cts);
        var handler = new CountingHandler();
        IUserApi client = Client(handler, serializer);

        await CaptureAsync<OperationCanceledException>(capture, () => client.CreateUserResultAsync(new CreateUserRequest("Bob"), cts.Token));

        capture.AssertCancelled(expectResponseTags: false);
        Assert.Equal(0, handler.Calls);
        serializer.AssertBufferReleased();
    }

    [Fact]
    public async Task ResultMethod_SerializerCancelsWithoutTheCaller_IsATimeout()
    {
        // Cancellation the caller did not ask for counts as a Timeout wherever it happens.
        using var capture = new TelemetryCapture();
        var serializer = new ThrowingBodySerializer(new OperationCanceledException("serializer gave up"));
        IUserApi client = Client(new CountingHandler(), serializer);

        Result<UserDto, HttpError> result = default;
        await capture.RunAsync(async () => result = await client.CreateUserResultAsync(new CreateUserRequest("Bob"), CancellationToken.None).ConfigureAwait(false));

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Timeout, result.Error.Kind);
        capture.AssertError("serializer gave up");
        serializer.AssertBufferReleased();
    }

    [Fact]
    public async Task ResultMethod_SerializerSucceeds_SendsTheBody()
    {
        // The success path is unchanged: the body is sent and the response read.
        byte[]? sent = null;
        using var httpClient = HttpClientFor(new StubHandler(async (request, ct) =>
        {
            sent = await request.Content!.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(UserJson, Encoding.UTF8, "application/json"),
            };
        }));
        IUserApi client = new UserApiClient(httpClient, new ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer());

        var result = await client.CreateUserResultAsync(new CreateUserRequest("Bob"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Alice", result.Value.Name);
        Assert.Equal("{\"name\":\"Bob\"}", Encoding.UTF8.GetString(sent!));
    }

    private static async Task<TException> CaptureAsync<TException>(TelemetryCapture capture, Func<Task> call)
        where TException : Exception
    {
        TException? thrown = null;
        await capture.RunAsync(async () => thrown = await Assert.ThrowsAnyAsync<TException>(call).ConfigureAwait(false)).ConfigureAwait(false);
        return thrown!;
    }

    private static UserApiClient Client(HttpMessageHandler handler, IRestSerializer serializer)
        => new(HttpClientFor(handler), serializer);

    private static HttpClient HttpClientFor(HttpMessageHandler handler)
        => new(handler) { BaseAddress = new Uri("http://serialize.local/") };

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(UserJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    // Writes part of a body into the pooled stream, then fails, and keeps the stream so the test
    // can check the buffer behind it was released.
    private abstract class BodySerializer : IRestSerializer
    {
        private Stream? _written;

        public string ContentType => "application/json";

        public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
            => throw new InvalidOperationException("No response is read after a serialization failure.");

        public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        {
            _written = stream;
            await stream.WriteAsync("{\"name\":"u8.ToArray(), ct).ConfigureAwait(false);
            await FailAsync(ct).ConfigureAwait(false);
        }

        protected abstract Task FailAsync(CancellationToken ct);

        // The helper releases its stream when the serializer fails, and a released stream refuses
        // writes. The buffer's return to the pool itself is covered by RequestBodyContentTests.
        public void AssertBufferReleased()
        {
            Assert.NotNull(_written);
            Assert.Throws<ObjectDisposedException>(() => _written.WriteByte(0));
        }
    }

    private sealed class ThrowingBodySerializer(Exception toThrow) : BodySerializer
    {
        public Exception ToThrow { get; } = toThrow;

        protected override Task FailAsync(CancellationToken ct) => Task.FromException(ToThrow);
    }

    private sealed class CancelingBodySerializer(CancellationTokenSource cts) : BodySerializer
    {
        protected override async Task FailAsync(CancellationToken ct)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
    }
}
