using System.Collections.Generic;
using System.Text;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

public sealed record EventRequest(string Prompt);

public sealed record Delta(string Text);

public abstract record StreamEvent;

public sealed record StartEvent(string Id) : StreamEvent;

public sealed record DeltaEvent(string Text) : StreamEvent;

public sealed record StopEvent(string Reason) : StreamEvent;

public sealed record EventApiError(int Status, string Body, string Source);

public sealed class EventApiErrorMapper : IHttpErrorMapper<EventApiError>
{
    public EventApiError Map(HttpError error) => new((int)error.StatusCode, Encoding.UTF8.GetString(error.Body.Span), "mapper");
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(EventApiErrorMapper))]
public interface IEventApi
{
    // Shape one: every event, its data read as Delta.
    [Get("/events")]
    [EventStream]
    IAsyncEnumerable<ServerSentEvent<Delta>> StreamDeltasAsync(CancellationToken ct = default);

    // Shape one without a CancellationToken: the iterator declares its own, which only
    // WithCancellation can set.
    [Get("/events")]
    [EventStream]
    IAsyncEnumerable<ServerSentEvent<Delta>> StreamDeltasWithoutTokenAsync();

    // Shape two with "message", the type of an event that has no event: line.
    [Get("/events")]
    [EventStream]
    [EventType("message", typeof(DeltaEvent))]
    [EventType("start", typeof(StartEvent))]
    IAsyncEnumerable<StreamEvent> StreamMessagesAsync(CancellationToken ct = default);

    // Shape two: one type per event name; other names are skipped.
    [Post("v1/x")]
    [EventStream]
    [EventType("start", typeof(StartEvent))]
    [EventType("delta", typeof(DeltaEvent))]
    [EventType("stop", typeof(StopEvent))]
    IAsyncEnumerable<StreamEvent> StreamAsync([Body] EventRequest body, CancellationToken ct = default);

    // Shape two whose error status is mapped before the stream starts.
    [Post("v1/x")]
    [EventStream(ErrorType = typeof(EventApiError))]
    [EventType("start", typeof(StartEvent))]
    [EventType("delta", typeof(DeltaEvent))]
    IAsyncEnumerable<StreamEvent> StreamMappedAsync([Body] EventRequest body, CancellationToken ct = default);

    // A Result method over the same error type, so the client takes the mapper whether or not the
    // event stream above uses it, and a test of that use fails at its assertion, not at compile time.
    [Get("/status")]
    Task<Result<Delta, EventApiError>> GetStatusAsync(CancellationToken ct = default);
}
