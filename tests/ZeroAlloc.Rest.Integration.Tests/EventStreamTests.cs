using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

// [EventStream] methods: the response is read as text/event-stream, one event at a time, from a
// request sent when enumeration starts. The response is disposed however the enumeration ends.
public sealed class EventStreamTests
{
    private const string TypedBody =
        "event: start\ndata: {\"id\":\"m1\"}\n\n"
        + "event: delta\ndata: {\"text\":\"Hel\"}\n\n"
        + "event: delta\ndata: {\"text\":\"lo\"}\n\n"
        + "event: stop\ndata: {\"reason\":\"end\"}\n\n";

    // The first event of TypedBody alone, so a stream that blocks at its end blocks after it.
    private static readonly string FirstEventOnly = TypedBody[..TypedBody.IndexOf("event: delta", StringComparison.Ordinal)];

    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Shape_one_yields_each_event_with_its_name_and_data()
    {
        const string body = "event: a\nid: 1\ndata: {\"text\":\"hi\"}\n\ndata: {\"text\":\"yo\"}\n\n";
        var content = new SseContent(body);
        HttpRequestMessage? sent = null;
        var api = Client((request, _) =>
        {
            sent = request;
            return Ok(content);
        });

        var events = await ToListAsync(api.StreamDeltasAsync());

        // Red: swap EventName and LastEventId in the emitted ServerSentEvent constructor call.
        Assert.Equal(
            new[] { new ServerSentEvent<Delta>("a", "1", new Delta("hi")), new ServerSentEvent<Delta>(null, "1", new Delta("yo")) },
            events);
        // Red: send the serializer's Accept instead of text/event-stream.
        Assert.Equal("text/event-stream", Assert.Single(sent!.Headers.GetValues("Accept")));
        // Red: send without ResponseHeadersRead; HttpClient then buffers the content first.
        Assert.Equal(0, content.BufferedCount);
        // Red: drop the response's Dispose from the iterator's finally.
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Shape_two_yields_the_mapped_type_per_event_name()
    {
        var content = new SseContent(TypedBody);
        string? requestBody = null;
        var api = Client(async (request, ct) =>
        {
            requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return await Ok(content).ConfigureAwait(false);
        });

        var events = await ToListAsync(api.StreamAsync(new EventRequest("hi")));

        // Red: read every named event as the first [EventType]'s type.
        Assert.Equal(
            new StreamEvent[] { new StartEvent("m1"), new DeltaEvent("Hel"), new DeltaEvent("lo"), new StopEvent("end") },
            events);
        // Red: drop the [Body] statement from the iterator.
        Assert.Equal("{\"prompt\":\"hi\"}", requestBody);
    }

    [Fact]
    public async Task An_unknown_event_name_is_skipped()
    {
        const string body =
            "event: ping\ndata: {}\n\n"
            + "event: start\ndata: {\"id\":\"m1\"}\n\n"
            + "data: {\"text\":\"unnamed\"}\n\n"
            + "event: delta2\ndata: {\"text\":\"near miss\"}\n\n"
            + "event: delta\ndata: {\"text\":\"Hel\"}\n\n";
        var api = Client((_, _) => Ok(new SseContent(body)));

        var events = await ToListAsync(api.StreamAsync(new EventRequest("hi")));

        // Red: end the stream at an unknown event, `else break;` for `else continue;`.
        Assert.Equal(new StreamEvent[] { new StartEvent("m1"), new DeltaEvent("Hel") }, events);
    }

    [Fact]
    public async Task A_non_success_status_before_the_stream_throws_HttpErrorException()
    {
        const string errorBody = "{\"error\":\"overloaded\"}";
        var content = new SseContent(errorBody);
        var api = Client((_, _) => Respond(HttpStatusCode.ServiceUnavailable, content));

        // Red: check the status with EnsureSuccessStatusCode, which throws a plain HttpRequestException.
        var ex = await Assert.ThrowsAsync<HttpErrorException>(() => ToListAsync(api.StreamAsync(new EventRequest("hi"))));

        // Red: build the HttpError without the response.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        // Red: skip reading the error body.
        Assert.Equal(errorBody, Encoding.UTF8.GetString(ex.Error.Body.Span));
        // Red: build the HttpError as HttpErrorKind.Transport.
        Assert.Equal(HttpErrorKind.Status, ex.Error.Kind);
        // Red: dispose the response after the loop instead of in the finally.
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task A_non_success_status_with_ErrorType_throws_the_mapped_HttpErrorException()
    {
        const string errorBody = "{\"error\":\"overloaded\"}";
        var content = new SseContent(errorBody);
        var api = Client((_, _) => Respond(HttpStatusCode.ServiceUnavailable, content));

        // Red: leave event streams out of MapsError, so the plain HttpErrorException is thrown.
        var ex = await Assert.ThrowsAsync<HttpErrorException<EventApiError>>(() => ToListAsync(api.StreamMappedAsync(new EventRequest("hi"))));

        // Red: skip reading the error body, so the mapper sees an empty one.
        Assert.Equal(new EventApiError(503, errorBody, "mapper"), ex.MappedError);
        // Red: dispose the response after the loop instead of in the finally.
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Cancellation_mid_stream_disposes_the_response()
    {
        var content = new SseContent(FirstEventOnly, blockAtEnd: true);
        var api = Client((_, _) => Ok(content));
        using var cts = new CancellationTokenSource();

        // Disposed at the end, not by await using: disposing an enumerator whose MoveNextAsync is
        // still pending throws, and that would hide a failed assertion.
        var enumerator = api.StreamAsync(new EventRequest("hi"), cts.Token).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();
        var next = enumerator.MoveNextAsync().AsTask();
        await content.Stream.Blocked.WaitAsync(Hang);
        await cts.CancelAsync();

        // Red: read the events with CancellationToken.None; the read then never ends.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(Hang));
        // Red: dispose the response after the loop instead of in the finally.
        Assert.True(content.Disposed);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task Cancellation_through_WithCancellation_reaches_the_read()
    {
        var content = new SseContent(FirstEventOnly, blockAtEnd: true);
        var api = Client((_, _) => Ok(content));
        using var cts = new CancellationTokenSource();

        var read = Task.Run(async () =>
        {
            await foreach (var _ in api.StreamAsync(new EventRequest("hi")).WithCancellation(cts.Token).ConfigureAwait(false))
            {
            }
        });
        await content.Stream.Blocked.WaitAsync(Hang);
        await cts.CancelAsync();

        // Red: read with the method's token, and give [EnumeratorCancellation] to a token the
        // iterator ignores; the read then never ends.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(Hang));
        // Red: dispose the response after the loop instead of in the finally.
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Breaking_out_of_the_loop_disposes_the_response()
    {
        var content = new SseContent(TypedBody, blockAtEnd: true);
        var api = Client((_, _) => Ok(content));

        await foreach (var _ in api.StreamAsync(new EventRequest("hi")))
        {
            // Red: dispose the response once the first event is read, before it is yielded.
            Assert.False(content.Disposed);
            break;
        }

        // Red: dispose the response after the loop instead of in the finally.
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task A_failure_mid_stream_disposes_the_response()
    {
        const string body = "event: start\ndata: {\"id\":\"m1\"}\n\nevent: delta\ndata: not json\n\n";
        var content = new SseContent(body);
        var api = Client((_, _) => Ok(content));

        // Red: skip an event whose data does not deserialize.
        await Assert.ThrowsAsync<JsonException>(() => ToListAsync(api.StreamAsync(new EventRequest("hi"))));
        // Red: dispose the response after the loop instead of in the finally.
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task The_request_is_not_sent_until_enumeration_starts()
    {
        var sends = 0;
        var api = Client((_, _) =>
        {
            sends++;
            return Ok(new SseContent(TypedBody));
        });

        var stream = api.StreamAsync(new EventRequest("hi"));

        // Red: start the first enumeration in the public method.
        Assert.Equal(0, sends);

        await ToListAsync(stream);
        var again = await Record.ExceptionAsync(() => ToListAsync(stream));

        // Red: build the request in the public method, so a second enumeration resends that one
        // message, which HttpClient refuses. A retry enumerates again and needs a fresh request.
        Assert.Null(again);
    }

    internal static IEventApi Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var http = new HttpClient(new StubHandler(send)) { BaseAddress = new Uri("http://localhost/") };
        return new EventApiClient(http, new SystemTextJsonSerializer(), new EventApiErrorMapper());
    }

    internal static Task<HttpResponseMessage> Ok(HttpContent content) => Respond(HttpStatusCode.OK, content);

    internal static Task<HttpResponseMessage> Respond(HttpStatusCode status, HttpContent content)
        => Task.FromResult(new HttpResponseMessage(status) { Content = content });

    internal static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source.ConfigureAwait(false))
            list.Add(item);
        return list;
    }

    // A text/event-stream body that records buffering and disposal, and can block at its end
    // until the read is cancelled, as an open connection does.
    internal sealed class SseContent : HttpContent
    {
        public SseContent(string body, bool blockAtEnd = false)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            Stream = new StreamResponsesTests.TrackingStream(bytes);
            if (blockAtEnd)
                Stream.BlockAfter = bytes.Length;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        }

        public StreamResponsesTests.TrackingStream Stream { get; }

        public int BufferedCount { get; private set; }

        public bool Disposed { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            BufferedCount++;
            await Stream.CopyToAsync(stream).ConfigureAwait(false);
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(Stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing)
                Stream.Dispose();
            base.Dispose(disposing);
        }
    }
}


// The span of an event stream covers the whole enumeration: it starts when enumeration starts and
// stops when the enumeration ends, is disposed, or fails. The duration is recorded once, when the
// response headers arrive, as for StreamResponses.
[Collection("rest-telemetry-non-parallel")]
public sealed class EventStreamTelemetryTests
{
    private const string Body = "event: start\ndata: {\"id\":\"m1\"}\n\nevent: delta\ndata: {\"text\":\"Hel\"}\n\n";

    // One event, so a stream that blocks at its end blocks in the read right after it.
    private const string OneEvent = "event: start\ndata: {\"id\":\"m1\"}\n\n";

    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task The_span_stays_open_until_the_enumeration_ends()
    {
        using var capture = new TelemetryCapture();
        var api = EventStreamTests.Client((_, _) => EventStreamTests.Ok(new EventStreamTests.SseContent(Body, blockAtEnd: true)));

        await capture.RunAsync(async () =>
        {
            await foreach (var _ in api.StreamAsync(new EventRequest("hi")).ConfigureAwait(false))
            {
                // Red: dispose the span once the response headers arrive.
                Assert.Equal(0, capture.StoppedSpanCount);
                break;
            }
        });

        // Red: declare the span without using, so disposing the enumerator never stops it.
        Assert.Equal(1, capture.StoppedSpanCount);
        // Red: record the duration a second time when the enumeration ends.
        capture.AssertCompleted();
    }

    [Fact]
    public async Task Caller_cancellation_mid_stream_leaves_the_span_unset()
    {
        using var capture = new TelemetryCapture();
        var content = new EventStreamTests.SseContent(OneEvent, blockAtEnd: true);
        var api = EventStreamTests.Client((_, _) => EventStreamTests.Ok(content));
        using var cts = new CancellationTokenSource();

        async Task Enumerate()
        {
            await foreach (var _ in api.StreamAsync(new EventRequest("hi"), cts.Token).ConfigureAwait(false))
                await cts.CancelAsync().ConfigureAwait(false);
        }

        // Red: read the events with CancellationToken.None; the read then never ends.
        await capture.RunAsync(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => Enumerate().WaitAsync(Hang)));

        // Red: drop the cancellation catch from the read loop, so the generic catch marks it Error.
        capture.AssertCancelled(expectResponseTags: true);
    }

    [Fact]
    public async Task An_error_status_marks_the_span_failed()
    {
        using var capture = new TelemetryCapture();
        var api = EventStreamTests.Client((_, _) => EventStreamTests.Respond(HttpStatusCode.ServiceUnavailable, new EventStreamTests.SseContent("{}")));

        // Red: check the status with EnsureSuccessStatusCode, which throws a plain HttpRequestException.
        await capture.RunAsync(() => Assert.ThrowsAsync<HttpErrorException>(() => EventStreamTests.ToListAsync(api.StreamAsync(new EventRequest("hi")))));

        // Red: exclude HttpErrorException from the catch that marks the span.
        capture.AssertError(expectResponseTags: true);
    }
}
