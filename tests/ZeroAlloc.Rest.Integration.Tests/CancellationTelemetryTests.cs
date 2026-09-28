using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

/// <summary>
/// A call the caller cancels is not an error: its span keeps status Unset and carries
/// rest.cancelled = true. Any other cancellation, such as a timeout, still marks the span Error.
/// Each case records the request duration exactly once.
/// </summary>
[Collection("rest-telemetry-non-parallel")]
public sealed class CancellationTelemetryTests
{
    private const string UserJson = "{\"id\":1,\"name\":\"Alice\"}";

    [Fact]
    public async Task TaskOfT_CallerCancelsBeforeResponse_LeavesSpanUnset()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var client = Client(new CancelBeforeResponseHandler(cts));

        await capture.RunAsync(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserAsync(1, cts.Token)));

        capture.AssertCancelled(expectResponseTags: false);
    }

    [Fact]
    public async Task ResultMethod_CallerCancelsBeforeResponse_LeavesSpanUnset()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var client = Client(new CancelBeforeResponseHandler(cts));

        await capture.RunAsync(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserResultAsync(1, cts.Token)));

        capture.AssertCancelled(expectResponseTags: false);
    }

    [Fact]
    public async Task TaskOfT_CallerCancelsDuringBodyRead_LeavesSpanUnset()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var client = Client(new OkHandler(), new CancelingSerializer(cts));

        await capture.RunAsync(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserAsync(1, cts.Token)));

        capture.AssertCancelled(expectResponseTags: true);
    }

    [Fact]
    public async Task ResultMethod_CallerCancelsDuringBodyRead_LeavesSpanUnset()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var client = Client(new OkHandler(), new CancelingSerializer(cts));

        await capture.RunAsync(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserResultAsync(1, cts.Token)));

        capture.AssertCancelled(expectResponseTags: true);
    }

    [Fact]
    public async Task TaskOfT_TimeoutWithoutCallerCancellation_MarksSpanError()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var client = Client(new HangingHandler(), timeout: TimeSpan.FromMilliseconds(50));

        await capture.RunAsync(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserAsync(1, cts.Token)));

        Assert.False(cts.IsCancellationRequested);
        capture.AssertError();
    }

    [Fact]
    public async Task ResultMethod_TimeoutWithoutCallerCancellation_MarksSpanError()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        var client = Client(new HangingHandler(), timeout: TimeSpan.FromMilliseconds(50));

        Result<UserDto, HttpError> result = default;
        await capture.RunAsync(async () => result = await client.GetUserResultAsync(1, cts.Token).ConfigureAwait(false));

        Assert.False(cts.IsCancellationRequested);
        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Timeout, result.Error.Kind);
        capture.AssertError();
    }

    private static UserApiClient Client(HttpMessageHandler handler, IRestSerializer? serializer = null, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://cancel.local/") };
        if (timeout is { } t)
            httpClient.Timeout = t;
        return new UserApiClient(httpClient, serializer ?? new SystemTextJsonSerializer());
    }

    // The caller cancels while the request is in flight, before any response arrives.
    private sealed class CancelBeforeResponseHandler(CancellationTokenSource cts) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("HttpClient did not observe the caller's cancellation.");
        }
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(UserJson, Encoding.UTF8, "application/json"),
            });
    }

    // Never answers, so only HttpClient.Timeout ends the call.
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    // Cancels the caller's token as it starts reading the body, after the response arrived.
    private sealed class CancelingSerializer(CancellationTokenSource cts) : IRestSerializer
    {
        public string ContentType => "application/json";

        public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return default;
        }

        public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
