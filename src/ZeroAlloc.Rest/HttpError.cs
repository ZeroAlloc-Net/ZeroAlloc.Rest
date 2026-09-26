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
/// <param name="Headers">
/// The response and content headers, such as <c>Content-Type</c>, in one dictionary whose lookups
/// ignore case. It is empty when no response was received.
/// </param>
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

    /// <summary>
    /// The response body of a <see cref="HttpErrorKind.Status"/> failure, cut to the client's
    /// <c>MaxErrorBodyBytes</c>. It is empty for the other kinds, for a response without a body, when
    /// reading is turned off, and when the body could not be read.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// The media type of the response body, such as <c>application/problem+json</c>, without
    /// parameters. Set for <see cref="HttpErrorKind.Status"/> and
    /// <see cref="HttpErrorKind.Deserialization"/> failures whose response declares one.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// <see langword="true"/> when the body was longer than <c>MaxErrorBodyBytes</c>, so
    /// <see cref="Body"/> holds only its start. Do not parse a truncated body as a whole document.
    /// </summary>
    public bool BodyTruncated { get; init; }
}
