using System.Net;

namespace ZeroAlloc.Rest;

/// <summary>
/// The failure value of a generated method that returns <c>Result&lt;T, HttpError&gt;</c>.
/// </summary>
/// <param name="StatusCode">
/// The response status code. It is <c>0</c> when no response was received, except for a
/// <see cref="HttpErrorKind.Transport"/> failure whose <see cref="System.Net.Http.HttpRequestException"/>
/// carries a status code.
/// </param>
/// <param name="Headers">The response headers, or an empty dictionary when no response was received.</param>
/// <param name="Message">A description of the failure. It is <see langword="null"/> for a <see cref="HttpErrorKind.Status"/> failure.</param>
public sealed record HttpError(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Message = null)
{
    /// <summary>What went wrong. Defaults to <see cref="HttpErrorKind.Status"/>.</summary>
    public HttpErrorKind Kind { get; init; } = HttpErrorKind.Status;

    /// <summary>
    /// The exception that caused the failure, for <see cref="HttpErrorKind.Transport"/>,
    /// <see cref="HttpErrorKind.Timeout"/> and <see cref="HttpErrorKind.Deserialization"/>.
    /// It is <see langword="null"/> for a <see cref="HttpErrorKind.Status"/> failure.
    /// </summary>
    public Exception? Exception { get; init; }
}
