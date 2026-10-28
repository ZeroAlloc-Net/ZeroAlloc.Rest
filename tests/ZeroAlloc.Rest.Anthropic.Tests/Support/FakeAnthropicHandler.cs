using System.Diagnostics;

namespace ZeroAlloc.Rest.Anthropic.Tests.Support;

/// <summary>
/// Serves queued responses in order, then the fallback, and records every request with its headers,
/// body and arrival time. A factory may throw to act as a transport failure.
/// </summary>
internal sealed class FakeAnthropicHandler : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _queued = new();
    private readonly List<RecordedRequest> _requests = [];
    private Func<CancellationToken, Task<HttpResponseMessage>>? _fallback;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    public FakeAnthropicHandler Then(Func<HttpResponseMessage> response)
    {
        lock (_gate)
            _queued.Enqueue(_ => Task.FromResult(response()));
        return this;
    }

    public FakeAnthropicHandler Always(Func<HttpResponseMessage> response)
    {
        lock (_gate)
            _fallback = _ => Task.FromResult(response());
        return this;
    }

    public FakeAnthropicHandler AlwaysAsync(Func<CancellationToken, Task<HttpResponseMessage>> response)
    {
        lock (_gate)
            _fallback = response;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = request.Headers.ToDictionary(
            h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

        Func<CancellationToken, Task<HttpResponseMessage>> next;
        lock (_gate)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri, headers, body, Stopwatch.GetTimestamp()));
            next = _queued.Count > 0
                ? _queued.Dequeue()
                : _fallback ?? throw new InvalidOperationException("No response was set up for this request.");
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}
