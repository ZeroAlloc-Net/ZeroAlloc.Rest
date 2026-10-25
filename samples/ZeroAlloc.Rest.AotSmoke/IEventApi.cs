using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Rest.AotSmoke;

// Server-sent events through a generated client and the Resilience bridge: both shapes, read through
// EventJsonContext, retried while the failure comes before the first event.
[ZeroAllocRestClient]
[Retry(MaxAttempts = 3, BackoffMs = 1, Jitter = false, RetryOnException = nameof(IsTransient))]
public interface IEventApi
{
    [Get("/ticks")]
    [EventStream]
    IAsyncEnumerable<ServerSentEvent<Tick>> FollowAsync(CancellationToken ct = default);

    [Get("/feed")]
    [EventStream]
    [EventType("start", typeof(FeedStart))]
    [EventType("tick", typeof(FeedTick))]
    IAsyncEnumerable<FeedEvent> FeedAsync(CancellationToken ct = default);

    static bool IsTransient(Exception e) =>
        e is IOException or HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable };
}
