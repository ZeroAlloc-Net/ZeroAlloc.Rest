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

    // Captures the generated client's spans under a parent the test starts, so spans from tests
    // running in parallel elsewhere are ignored, plus every rest.request_duration_ms sample.
    private sealed class TelemetryCapture : IDisposable
    {
        private const string TestSourceName = "ZeroAlloc.Rest.Integration.Tests.Cancellation";
        private static readonly ActivitySource s_testSource = new(TestSourceName);

        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;
        private readonly List<Activity> _stopped = new();
        private readonly List<Dictionary<string, object?>> _durations = new();
        private ActivityTraceId _traceId;

        public TelemetryCapture()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, "ZeroAlloc.Rest", StringComparison.Ordinal)
                    || string.Equals(source.Name, TestSourceName, StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (string.Equals(activity.Source.Name, "ZeroAlloc.Rest", StringComparison.Ordinal))
                    {
                        lock (_stopped)
                            _stopped.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Rest", StringComparison.Ordinal)
                        && string.Equals(instrument.Name, "rest.request_duration_ms", StringComparison.Ordinal))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            {
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < tags.Length; i++)
                    dict[tags[i].Key] = tags[i].Value;
                lock (_durations)
                    _durations.Add(dict);
            });
            _meterListener.Start();
        }

        public async Task RunAsync(Func<Task> call)
        {
            using var parent = s_testSource.StartActivity("test") ?? throw new InvalidOperationException("The test activity was not sampled.");
            _traceId = parent.TraceId;
            await call().ConfigureAwait(false);
        }

        public void AssertCancelled(bool expectResponseTags)
        {
            var span = SingleSpan();
            Assert.Equal(ActivityStatusCode.Unset, span.Status);
            Assert.Equal(true, span.GetTagItem("rest.cancelled"));
            AssertOneDuration(expectResponseTags);
        }

        public void AssertError()
        {
            var span = SingleSpan();
            Assert.Equal(ActivityStatusCode.Error, span.Status);
            Assert.Null(span.GetTagItem("rest.cancelled"));
            AssertOneDuration(expectResponseTags: false);
        }

        private Activity SingleSpan()
        {
            lock (_stopped)
                return Assert.Single(_stopped, a => a.TraceId == _traceId);
        }

        // Metrics carry no trace id. The collection disables parallelization, so no other test
        // records a duration while this one runs.
        private void AssertOneDuration(bool expectResponseTags)
        {
            lock (_durations)
            {
                var tags = Assert.Single(_durations);
                Assert.Equal(expectResponseTags, tags.ContainsKey("http.status_code"));
            }
        }

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }
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
