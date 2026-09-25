namespace ZeroAlloc.Rest;

/// <summary>The kind of failure an <see cref="HttpError"/> describes.</summary>
public enum HttpErrorKind
{
    /// <summary>The server answered with a non-success status code.</summary>
    Status = 0,

    /// <summary>
    /// The request failed before a response arrived, such as a DNS, connection or TLS failure.
    /// The <see cref="HttpError.Exception"/> is the <see cref="System.Net.Http.HttpRequestException"/>.
    /// </summary>
    Transport = 1,

    /// <summary>
    /// The request was cancelled without the caller asking for it, which is how
    /// <see cref="System.Net.Http.HttpClient.Timeout"/> surfaces. Caller cancellation still throws.
    /// </summary>
    Timeout = 2,

    /// <summary>
    /// The server answered with a success status code, but the serializer could not read the body.
    /// <see cref="HttpError.StatusCode"/> and <see cref="HttpError.Headers"/> hold the real response values.
    /// </summary>
    Deserialization = 3,
}
