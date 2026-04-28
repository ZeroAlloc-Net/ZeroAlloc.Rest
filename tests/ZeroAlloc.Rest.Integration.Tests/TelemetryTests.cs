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

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated transport failure");
    }
}

[CollectionDefinition("rest-telemetry-non-parallel", DisableParallelization = true)]
public sealed class RestTelemetryNonParallelCollection { }
