using System.Net;

namespace ZeroAlloc.Rest;

// RCS1194 asks for the parameterless and message-only constructors. They would build an exception
// whose Error is null, and the only producer is generated code that always has the HttpError.
#pragma warning disable RCS1194
/// <summary>
/// Thrown by an event-stream method when the server answers with a non-success status code, before
/// the stream starts. A stream has no <c>Result</c> to carry the failure, so it throws.
/// </summary>
public class HttpErrorException : HttpRequestException
{
    /// <summary>Creates the exception for <paramref name="error"/>.</summary>
    /// <param name="error">The failure, including the status code, headers and the cut response body.</param>
    public HttpErrorException(HttpError error)
        : base(
            (error ?? throw new ArgumentNullException(nameof(error))).Message
                ?? $"Response status code does not indicate success: {(int)error.StatusCode} ({error.StatusCode}).",
            error.Exception,
            (int)error.StatusCode == 0 ? null : error.StatusCode)
    {
        Error = error;
    }

    /// <summary>The failure. <see cref="HttpRequestException.StatusCode"/> is its status code.</summary>
    public HttpError Error { get; }
}
#pragma warning restore RCS1194

// RCS1194 asks for the parameterless and message-only constructors. They would build an exception
// whose Error is null, and the only producer is generated code that always has the HttpError.
#pragma warning disable RCS1194
/// <summary>
/// An <see cref="HttpErrorException"/> whose <see cref="HttpError"/> was mapped to <typeparamref name="TError"/>.
/// </summary>
/// <typeparam name="TError">The mapped error type of the method's <c>ErrorType</c>.</typeparam>
public sealed class HttpErrorException<TError> : HttpErrorException
{
    /// <summary>Creates the exception for <paramref name="error"/> and its mapped value.</summary>
    /// <param name="error">The failure.</param>
    /// <param name="mappedError">What the error mapper made of <paramref name="error"/>.</param>
    public HttpErrorException(HttpError error, TError mappedError)
        : base(error)
    {
        MappedError = mappedError;
    }

    /// <summary>The mapped error.</summary>
    public TError MappedError { get; }
}
#pragma warning restore RCS1194
