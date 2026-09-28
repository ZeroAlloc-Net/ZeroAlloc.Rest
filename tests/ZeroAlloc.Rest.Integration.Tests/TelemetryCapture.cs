using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace ZeroAlloc.Rest.Integration.Tests;

// Captures the generated client's spans under a parent the test starts, so spans from tests
// running in parallel elsewhere are ignored, plus every rest.request_duration_ms sample.
internal sealed class TelemetryCapture : IDisposable
{
    private const string TestSourceName = "ZeroAlloc.Rest.Integration.Tests.TelemetryCapture";
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

    public void AssertError(string? description = null)
    {
        var span = SingleSpan();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        if (description is not null)
            Assert.Equal(description, span.StatusDescription);
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
