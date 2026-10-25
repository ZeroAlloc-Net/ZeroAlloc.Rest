using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// The typed Messages API. It is internal so that <see cref="AnthropicClient"/> is the only way in: the facade sets
/// the headers, the timeout and the retry count, and turns a failed stream into an <see cref="AnthropicApiException"/>.
/// The generated client and its registration are internal with it.
/// </summary>
/// <remarks>
/// A 429, a 529 and every other 5xx is retried, after the <c>retry-after</c> wait when the API sends one. Any other
/// status, a timeout and a transport failure are not. A stream is retried only before its first event.
/// </remarks>
[ZeroAllocRestClient]
[ErrorMapper(typeof(AnthropicErrorMapper))]
internal interface IAnthropicMessagesApi
{
    /// <summary>Creates a message.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The message, or the error.</returns>
    [Post("v1/messages")]
    [Retry(
        MaxAttempts = 3,
        BackoffMs = 500,
        Jitter = true,
        MaxDelayMs = 60_000,
        RetryWhen = nameof(IsTransient),
        RetryOnException = nameof(IsTransient),
        RethrowDeclined = true,
        DelayHint = nameof(RetryAfter))]
    Task<Result<MessageResponse, AnthropicApiError>> CreateMessageAsync([Body] MessageRequest request, CancellationToken ct = default);

    /// <summary>
    /// Streams a message. The request must have <c>stream</c> set, which <see cref="AnthropicClient"/> does.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the stream.</param>
    /// <returns>The events, in order.</returns>
    [Post("v1/messages")]
    [EventStream(ErrorType = typeof(AnthropicApiError))]
    [EventType("message_start", typeof(MessageStartEvent))]
    [EventType("content_block_start", typeof(ContentBlockStartEvent))]
    [EventType("content_block_delta", typeof(ContentBlockDeltaEvent))]
    [EventType("content_block_stop", typeof(ContentBlockStopEvent))]
    [EventType("message_delta", typeof(MessageDeltaEvent))]
    [EventType("message_stop", typeof(MessageStopEvent))]
    [EventType("ping", typeof(PingEvent))]
    [EventType("error", typeof(ErrorEvent))]
    [Retry(
        MaxAttempts = 3,
        BackoffMs = 500,
        Jitter = true,
        MaxDelayMs = 60_000,
        RetryOnException = nameof(IsTransient),
        RethrowDeclined = true,
        DelayHint = nameof(RetryAfter))]
    IAsyncEnumerable<MessageStreamEvent> StreamMessageAsync([Body] MessageRequest request, CancellationToken ct = default);

    /// <summary>Whether <paramref name="error"/> is worth another attempt: a 429, a 529 or another 5xx.</summary>
    /// <param name="error">The error.</param>
    /// <returns><see langword="true"/> for a transient status.</returns>
    static bool IsTransient(AnthropicApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Status is 429 or (>= 500 and <= 599);
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is worth another attempt: only a transient mapped error status. A
    /// timeout and a transport failure are not.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> for a transient mapped error.</returns>
    static bool IsTransient(Exception exception) =>
        exception is HttpErrorException<AnthropicApiError> { MappedError: { } error } && IsTransient(error);

    /// <summary>The wait <paramref name="error"/> asked for in its <c>retry-after</c> header.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The wait, or null for the backoff.</returns>
    static TimeSpan? RetryAfter(AnthropicApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.RetryAfter;
    }

    /// <summary>The wait the mapped error of <paramref name="exception"/> asked for.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The wait, or null for the backoff.</returns>
    static TimeSpan? RetryAfter(Exception exception) =>
        exception is HttpErrorException<AnthropicApiError> { MappedError: { } error } ? error.RetryAfter : null;
}
