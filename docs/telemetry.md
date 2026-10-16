---
id: telemetry
title: Telemetry
slug: /telemetry
description: Tracing spans and metrics every generated ZeroAlloc.Rest client emits, and how to subscribe with OpenTelemetry.
---

# Telemetry

Every generated client emits OpenTelemetry-compatible signals out of the box, with no extra dependency beyond the BCL `System.Diagnostics` primitives.

**Tracing.** An `ActivitySource("ZeroAlloc.Rest")` produces one span per call, named `Interface.Method`. Tags: `http.method`, `http.status_code`, `server.address`, `rest.method`. A failure sets `ActivityStatusCode.Error` with the exception's message. Cancellation through the method's `CancellationToken` is not a failure: the span keeps status `Unset` and gets the tag `rest.cancelled` = `true`. Any other cancellation, such as `HttpClient.Timeout`, is a failure.

**Metrics.** The generator emits `rest.requests_total` (counter) and `rest.request_duration_ms` (histogram) under the `ZeroAlloc.Rest` Meter. Tags: `http.method`, `http.status_code`, `server.address`, `rest.method` (the generated `Interface.Method` identifier).

Every call records exactly one `rest.request_duration_ms` sample. A call that gets a response increments `rest.requests_total` and records its duration with all four tags, whatever the status and even when reading the body then fails. A call that ends before any response, such as a request body the serializer cannot write, a transport error, a timeout or a cancellation, records its duration with only `http.method` and `rest.method` and leaves the counter untouched, so the histogram count minus the counter is the number of calls that never got a response.

**Subscription:**
```csharp
services.AddOpenTelemetry()
        .WithTracing(t => t.AddSource("ZeroAlloc.Rest"))
        .WithMetrics(m => m.AddMeter("ZeroAlloc.Rest"));
```
