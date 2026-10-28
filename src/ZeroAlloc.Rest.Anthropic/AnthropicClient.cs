using System.Runtime.CompilerServices;
using System.Text.Json;
using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// The Anthropic Messages API client: create a message, or stream one. Build it with <see cref="Create"/>, or
/// register it with <see cref="AnthropicServiceCollectionExtensions.AddAnthropicClient"/>.
/// </summary>
public sealed class AnthropicClient
{
    private const string ApiKeyHeader = "x-api-key";
    private const string VersionHeader = "anthropic-version";

    private readonly IAnthropicMessagesApi _api;

    internal AnthropicClient(IAnthropicMessagesApi api) => _api = api;

    // The context's own options come along with its metadata; this constructor carries no trim annotation.
    internal static SystemTextJsonSerializer Serializer { get; } = new(AnthropicJson.Context);

    /// <summary>
    /// Builds a client over <paramref name="httpClient"/>. It sets the base address when the client has none, the
    /// <c>x-api-key</c> and <c>anthropic-version</c> headers and <see cref="HttpClient.Timeout"/>, so call it
    /// before the client sends anything.
    /// </summary>
    /// <param name="httpClient">The HTTP client. The caller keeps ownership of it.</param>
    /// <param name="options">The settings.</param>
    /// <returns>The client.</returns>
    public static AnthropicClient Create(HttpClient httpClient, AnthropicClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        httpClient.BaseAddress ??= options.BaseAddress;
        ConfigureHttpClient(httpClient, options);
        var inner = new AnthropicMessagesApiClient(httpClient, Serializer, new AnthropicErrorMapper());
        return new AnthropicClient(new IAnthropicMessagesApiResilienceProxy(inner, Policies(options)));
    }

    /// <summary>Creates a message.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call; cancelling throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The message, or the error after the retries: the API's error, or a <c>timeout</c> or
    /// <c>connection_error</c>, which are not retried.
    /// </returns>
    public Task<Result<MessageResponse, AnthropicApiError>> CreateMessageAsync(MessageRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _api.CreateMessageAsync(request, ct);
    }

    /// <summary>
    /// Streams a message: sends the request with <c>stream: true</c> and yields its events in order. A 429, 529 or
    /// other 5xx before the first event is retried; nothing is retried after it.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the stream; cancelling throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The events, ending with <see cref="MessageStopEvent"/>.</returns>
    /// <exception cref="AnthropicApiException">
    /// The stream failed before its first event once the retries ran out, sent an <c>error</c> event, broke off,
    /// or ended without <c>message_stop</c> (<c>stream_incomplete</c>).
    /// </exception>
    public IAsyncEnumerable<MessageStreamEvent> StreamMessageAsync(MessageRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StreamCoreAsync(request with { Stream = true }, ct);
    }

    private async IAsyncEnumerable<MessageStreamEvent> StreamCoreAsync(
        MessageRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = _api.StreamMessageAsync(request, ct).GetAsyncEnumerator(ct);
        await using (enumerator.ConfigureAwait(false))
        {
            var stopped = false;
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        break;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
                {
                    // Anything not listed in Translate, such as a bug in a handler, goes through unchanged.
                    var failure = Translate(ex);
                    if (failure is null)
                        throw;
                    throw failure;
                }

                var current = enumerator.Current;
                if (current is ErrorEvent errorEvent)
                {
                    // An error event without its error object still ends the stream as an error.
                    throw new AnthropicApiException(errorEvent.Error is { } detail
                        ? new AnthropicApiError(detail.Type, detail.Message, 0, null, null)
                        : Failure("error", "The stream sent an error event without details."));
                }

                stopped |= current is MessageStopEvent;
                yield return current;
            }

            if (!stopped)
                throw new AnthropicApiException(Failure("stream_incomplete", "The stream ended before message_stop."));
        }
    }

    // A failure from the typed client or the retry proxy, as the error a caller can act on. The original stays
    // as the inner exception; nothing in it comes from the request.
    private static AnthropicApiException? Translate(Exception exception) => exception switch
    {
        ResilienceException { InnerException: { } last } => Translate(last),
        HttpErrorException<AnthropicApiError> { MappedError: { } error } => new AnthropicApiException(error, exception),
        OperationCanceledException => new AnthropicApiException(Failure("timeout", exception.Message), exception),
        HttpRequestException http => new AnthropicApiException(
            new AnthropicApiError("connection_error", http.Message, http.StatusCode is { } s ? (int)s : 0, null, null), exception),
        IOException => new AnthropicApiException(Failure("connection_error", exception.Message), exception),
        JsonException => new AnthropicApiException(Failure("invalid_response", exception.Message), exception),
        _ => null,
    };

    private static AnthropicApiError Failure(string type, string message) => new(type, message, 0, null, null);

    internal static void ConfigureHttpClient(HttpClient httpClient, AnthropicClientOptions options)
    {
        var headers = httpClient.DefaultRequestHeaders;
        headers.Remove(ApiKeyHeader);
        if (options.ApiKey is { } key)
            headers.TryAddWithoutValidation(ApiKeyHeader, key);
        headers.Remove(VersionHeader);
        headers.TryAddWithoutValidation(VersionHeader, options.AnthropicVersion);
        httpClient.Timeout = options.Timeout;
    }

    internal static AnthropicMessagesApiResiliencePolicies Policies(AnthropicClientOptions options)
    {
        if (options.MaxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxRetries, "MaxRetries must not be negative.");
        // One policy for both slots. perAttemptTimeoutMs stays 0: HttpClient.Timeout bounds an attempt, and a
        // stream method refuses a per-attempt timeout (ZR0014).
        var retry = new RetryPolicy(
            maxAttempts: options.MaxRetries + 1, backoffMs: 500, jitter: true, perAttemptTimeoutMs: 0, maxDelayMs: 60_000);
        return new AnthropicMessagesApiResiliencePolicies
        {
            CreateMessageAsyncRetry = retry,
            StreamMessageAsyncRetry = retry,
        };
    }
}
