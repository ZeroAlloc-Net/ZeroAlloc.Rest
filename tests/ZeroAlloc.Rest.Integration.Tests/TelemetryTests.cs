using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

/// <summary>
/// Verifies the Rest source generator emits rest.requests_total +
/// rest.request_duration_ms under the "ZeroAlloc.Rest" Meter. Each test
/// drives a real generated client against a WireMock server (or a throwing
/// HttpMessageHandler) and asserts measurements via MeterListener.
///
/// Marked non-parallel because static MeterListener subscriptions are
/// process-wide and would otherwise observe each other's measurements.
/// </summary>
[Collection("rest-telemetry-non-parallel")]
public sealed class TelemetryTests : IDisposable
{
    private static readonly JsonSerializerOptions s_camelCase = new(JsonSerializerDefaults.Web);

    private readonly WireMockServer _server;
    private readonly ServiceProvider _provider;
    private readonly IUserApi _client;

    public TelemetryTests()
    {
        _server = WireMockServer.Start();

        var services = new ServiceCollection();
        services.AddIUserApi(options =>
        {
            options.BaseAddress = new Uri(_server.Url!);
            options.UseSerializer<SystemTextJsonSerializer>();
        });

        _provider = services.BuildServiceProvider();
        _client = _provider.GetRequiredService<IUserApi>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task Send_RecordsRequestsTotalCounter_WithExpectedTags()
    {
        var measurements = new List<long>();
        var capturedTags = new List<Dictionary<string, object?>>();

        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Rest", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "rest.requests_total", StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            measurements.Add(value);
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < tags.Length; i++)
                dict[tags[i].Key] = tags[i].Value;
            capturedTags.Add(dict);
        });
        meterListener.Start();

        _server.Given(Request.Create().WithPath("/users/1").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(1, "Alice"), s_camelCase)));

        await _client.GetUserAsync(1);

        Assert.Single(measurements);
        Assert.Equal(1L, measurements[0]);

        var tags = capturedTags[0];
        Assert.Equal("GET", tags["http.method"]);
        Assert.Equal(200, tags["http.status_code"]);
        Assert.Equal("IUserApi.GetUserAsync", tags["rest.method"]);
        Assert.True(tags.ContainsKey("server.address"));
        Assert.IsType<string>(tags["server.address"]);
    }

    [Fact]
    public async Task Send_RecordsRequestDurationHistogram_OnSuccess()
    {
        var measurements = new List<double>();

        using var meterListener = new MeterListener
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
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            measurements.Add(value);
        });
        meterListener.Start();

        _server.Given(Request.Create().WithPath("/users/2").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(2, "Bob"), s_camelCase)));

        await _client.GetUserAsync(2);

        Assert.Single(measurements);
        Assert.True(measurements[0] >= 0.0, $"expected non-negative duration, got {measurements[0]}");
    }

    [Fact]
    public async Task Send_RecordsRequestDurationHistogram_OnException_AndCounterStaysUnchanged()
    {
        var counterMeasurements = new List<long>();
        var histogramMeasurements = new List<double>();

        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (!string.Equals(instrument.Meter.Name, "ZeroAlloc.Rest", StringComparison.Ordinal))
                    return;
                if (string.Equals(instrument.Name, "rest.requests_total", StringComparison.Ordinal)
                    || string.Equals(instrument.Name, "rest.request_duration_ms", StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            counterMeasurements.Add(value);
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            histogramMeasurements.Add(value);
        });
        meterListener.Start();

        // Build a fresh client backed by a throwing HttpMessageHandler so SendAsync raises.
        // We don't reuse _client here because that one targets the WireMock server.
        using var throwingHandler = new ThrowingHandler();
        using var httpClient = new HttpClient(throwingHandler) { BaseAddress = new Uri("http://throwing.local/") };

        var services = new ServiceCollection();
        services.AddIUserApi(options =>
        {
            options.BaseAddress = new Uri("http://throwing.local/");
            options.UseSerializer<SystemTextJsonSerializer>();
        })
        .ConfigurePrimaryHttpMessageHandler(() => new ThrowingHandler());

        await using var sp = services.BuildServiceProvider();
        var throwingClient = sp.GetRequiredService<IUserApi>();

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => throwingClient.GetUserAsync(7));

        Assert.Empty(counterMeasurements);
        Assert.Single(histogramMeasurements);
        Assert.True(histogramMeasurements[0] >= 0.0, $"expected non-negative duration, got {histogramMeasurements[0]}");
    }

    [Fact]
    public async Task ResultMethod_TransportFailure_StillRecordsDurationAndErrorSpan()
    {
        // A Result method returns the transport failure instead of throwing, but the failure must
        // still show up in metrics and traces exactly as a thrown one does.
        var counterMeasurements = new List<long>();
        var histogramMeasurements = new List<double>();
        var stoppedActivities = new List<System.Diagnostics.Activity>();

        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Rest", StringComparison.Ordinal))
                    l.EnableMeasurementEvents(instrument);
            },
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => counterMeasurements.Add(value));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => histogramMeasurements.Add(value));
        meterListener.Start();

        using var activityListener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "ZeroAlloc.Rest", StringComparison.Ordinal),
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _)
                => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stoppedActivities.Add,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(activityListener);

        using var httpClient = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://throwing.local/") };
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(7, default);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
        Assert.Empty(counterMeasurements);
        Assert.Single(histogramMeasurements);
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal(System.Diagnostics.ActivityStatusCode.Error, activity.Status);
        Assert.Equal("simulated transport failure", activity.StatusDescription);
    }

    // A call that gets a response and then fails records its duration once, with the response's
    // tags, so the histogram's sample count matches rest.requests_total.
    [Fact]
    public async Task TaskOfT_ErrorStatus_RecordsDurationOnce()
    {
        using var capture = new DurationCapture();
        _server.Given(Request.Create().WithPath("/users/40").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(500));

        await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetUserAsync(40));

        capture.AssertRecordedOnceWithStatus(500);
    }

    [Fact]
    public async Task TaskOfT_DeserializationFailure_RecordsDurationOnce()
    {
        using var capture = new DurationCapture();
        _server.Given(Request.Create().WithPath("/users/41").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("{ not json"));

        await Assert.ThrowsAnyAsync<JsonException>(() => _client.GetUserAsync(41));

        capture.AssertRecordedOnceWithStatus(200);
    }

    [Fact]
    public async Task Task_ErrorStatus_RecordsDurationOnce()
    {
        using var capture = new DurationCapture();
        _server.Given(Request.Create().WithPath("/users/42").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(404));

        await Assert.ThrowsAsync<HttpRequestException>(() => _client.DeleteUserAsync(42));

        capture.AssertRecordedOnceWithStatus(404);
    }

    [Fact]
    public async Task ResultMethod_DeserializationFailure_RecordsDurationOnce()
    {
        using var capture = new DurationCapture();
        _server.Given(Request.Create().WithPath("/users/43/result").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("{ not json"));

        var result = await _client.GetUserResultAsync(43);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        capture.AssertRecordedOnceWithStatus(200);
    }

    [Fact]
    public async Task TaskOfT_CallerCancelsDuringBodyRead_RecordsDurationOnce()
    {
        using var capture = new DurationCapture();
        using var cts = new System.Threading.CancellationTokenSource();
        var client = CancelingClient(cts, "/users/44");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserAsync(44, cts.Token));

        capture.AssertRecordedOnceWithStatus(200);
    }

    [Fact]
    public async Task ResultMethod_CallerCancelsDuringBodyRead_RecordsDurationOnce()
    {
        using var capture = new DurationCapture();
        using var cts = new System.Threading.CancellationTokenSource();
        var client = CancelingClient(cts, "/users/45/result");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserResultAsync(45, cts.Token));

        capture.AssertRecordedOnceWithStatus(200);
    }

    // A client whose serializer cancels the caller's token as it starts reading the body, so the
    // cancellation lands after the response arrived.
    private UserApiClient CancelingClient(System.Threading.CancellationTokenSource cts, string path)
    {
        _server.Given(Request.Create().WithPath(path).UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(1, "Alice"), s_camelCase)));
        var httpClient = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        return new UserApiClient(httpClient, new CancelingSerializer(cts));
    }

    // Captures rest.request_duration_ms samples with their tags, and counts rest.requests_total.
    private sealed class DurationCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly List<Dictionary<string, object?>> _durations = new();
        private int _requests;

        public DurationCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Rest", StringComparison.Ordinal))
                        l.EnableMeasurementEvents(instrument);
                },
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            {
                if (string.Equals(instrument.Name, "rest.requests_total", StringComparison.Ordinal))
                    _requests++;
            });
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            {
                if (!string.Equals(instrument.Name, "rest.request_duration_ms", StringComparison.Ordinal))
                    return;
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < tags.Length; i++)
                    dict[tags[i].Key] = tags[i].Value;
                _durations.Add(dict);
            });
            _listener.Start();
        }

        public void AssertRecordedOnceWithStatus(int statusCode)
        {
            Assert.Equal(1, _requests);
            var tags = Assert.Single(_durations);
            Assert.Equal(statusCode, tags["http.status_code"]);
            Assert.True(tags.ContainsKey("server.address"));
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated transport failure");
    }
    private sealed class CancelingSerializer(System.Threading.CancellationTokenSource cts) : IRestSerializer
    {
        public string ContentType => "application/json";

        public async ValueTask<T?> DeserializeAsync<T>(System.IO.Stream stream, System.Threading.CancellationToken ct = default)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return default;
        }

        public ValueTask SerializeAsync<T>(System.IO.Stream stream, T value, System.Threading.CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}

[CollectionDefinition("rest-telemetry-non-parallel", DisableParallelization = true)]
public sealed class RestTelemetryNonParallelCollection { }
