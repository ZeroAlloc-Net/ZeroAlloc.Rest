namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// A failed Messages API call: the error the API returned, or the failure that kept it from answering.
/// </summary>
/// <param name="Type">
/// The API's error type, such as <c>overloaded_error</c> or <c>rate_limit_error</c>. For a failure the API did not
/// describe: <c>http_&lt;status&gt;</c> for an error body that is not an Anthropic error, <c>timeout</c>,
/// <c>connection_error</c>, <c>invalid_response</c> for a success body that could not be read, and
/// <c>stream_incomplete</c> for a stream that ended before <c>message_stop</c>.
/// </param>
/// <param name="Message">The API's human readable message, or a description of the failure.</param>
/// <param name="Status">The HTTP status of the failed response; 0 when no response status describes the failure.</param>
/// <param name="RequestId">The API's request id, from the body or the <c>request-id</c> header, when there is one.</param>
/// <param name="RetryAfter">The wait the API asked for in its <c>retry-after</c> header, when it sent one.</param>
public sealed record AnthropicApiError(string Type, string Message, int Status, string? RequestId, TimeSpan? RetryAfter);
