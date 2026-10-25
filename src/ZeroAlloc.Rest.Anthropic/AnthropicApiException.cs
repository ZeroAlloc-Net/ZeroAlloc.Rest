namespace ZeroAlloc.Rest.Anthropic;

// RCS1194 asks for the parameterless and message-only constructors. They would build an exception whose
// Error is null, and Error is the reason this type exists; the facade always has the error. Ruling R4.
#pragma warning disable RCS1194
/// <summary>
/// Thrown by <see cref="AnthropicClient.StreamMessageAsync"/> when a stream fails: before its first event, after
/// the retries ran out, through an <c>error</c> event, or by ending without <c>message_stop</c>.
/// </summary>
public sealed class AnthropicApiException : Exception
{
    /// <summary>Creates the exception for <paramref name="error"/>.</summary>
    /// <param name="error">The error.</param>
    public AnthropicApiException(AnthropicApiError error)
        : this(error, null)
    {
    }

    /// <summary>Creates the exception for <paramref name="error"/>, caused by <paramref name="innerException"/>.</summary>
    /// <param name="error">The error.</param>
    /// <param name="innerException">The exception that caused the failure, if any.</param>
    public AnthropicApiException(AnthropicApiError error, Exception? innerException)
        : base(Describe(error), innerException)
    {
        Error = error;
    }

    /// <summary>The error.</summary>
    public AnthropicApiError Error { get; }

    // Only the error's own fields: nothing from the request, so neither the key nor its header can appear.
    private static string Describe(AnthropicApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.RequestId is null
            ? $"Anthropic API error {error.Type}, status {error.Status}: {error.Message}"
            : $"Anthropic API error {error.Type}, status {error.Status}, request {error.RequestId}: {error.Message}";
    }
}
#pragma warning restore RCS1194
