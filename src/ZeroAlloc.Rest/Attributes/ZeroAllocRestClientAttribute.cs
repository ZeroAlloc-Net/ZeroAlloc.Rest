namespace ZeroAlloc.Rest.Attributes;

/// <summary>Generates a REST client for the interface it is applied to.</summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class ZeroAllocRestClientAttribute : Attribute
{
    /// <summary>
    /// The most bytes of an error response body that a <c>Result&lt;T, HttpError&gt;</c> method keeps
    /// in <see cref="HttpError.Body"/>. A longer body is cut to this size and
    /// <see cref="HttpError.BodyTruncated"/> is set. A value of 0 or less means no read: the
    /// generated client then contains no body-reading code. A value above
    /// <c>Array.MaxLength - 1</c> is clamped to <c>Array.MaxLength - 1</c>, the largest body a
    /// single array can hold alongside the byte that detects truncation. Read at compile time.
    /// Defaults to 65536.
    /// </summary>
    public int MaxErrorBodyBytes { get; set; } = 65536;

    /// <summary>
    /// Whether the client's methods read responses as streams. Off by default. A method's HTTP
    /// attribute, such as <c>[Get("/users", StreamResponses = false)]</c>, overrides it for that
    /// method. Read at compile time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default a generated call sends with <see cref="System.Net.Http.HttpCompletionOption.ResponseContentRead"/>:
    /// <see cref="System.Net.Http.HttpClient"/> copies the whole body into its own unpooled buffer, and
    /// the serializer then reads that copy. When streaming, the call sends with
    /// <see cref="System.Net.Http.HttpCompletionOption.ResponseHeadersRead"/> and the serializer reads
    /// straight from the connection, so that copy is never made. The response and its stream are
    /// disposed when the call ends, however it ends.
    /// </para>
    /// <para>Streaming changes these behaviours:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="System.Net.Http.HttpClient.Timeout"/> covers only the wait for the response
    /// headers, not reading the body. Only the method's <see cref="System.Threading.CancellationToken"/>
    /// can stop a slow body; pass one with a deadline, for example from a
    /// <see cref="System.Threading.CancellationTokenSource"/> with a timeout, to bound the body read.
    /// That token is the caller's, so when it fires the call throws
    /// <see cref="System.OperationCanceledException"/>, from a <c>Result</c> method too, rather than
    /// returning <see cref="HttpErrorKind.Timeout"/>. A timeout while waiting for the headers is
    /// still <see cref="HttpErrorKind.Timeout"/>.
    /// </description></item>
    /// <item><description>
    /// A connection that fails while the body is read surfaces from the serializer as an
    /// <see cref="System.IO.IOException"/>, not as an <see cref="System.Net.Http.HttpRequestException"/>
    /// from the send. A <c>Result</c> method reports it as <see cref="HttpErrorKind.Deserialization"/>
    /// rather than <see cref="HttpErrorKind.Transport"/>; a failure before the headers arrive is still
    /// <see cref="HttpErrorKind.Transport"/>.
    /// </description></item>
    /// <item><description>
    /// <see cref="System.Net.Http.HttpClient.MaxResponseContentBufferSize"/> does not apply, since
    /// nothing is buffered. A serializer reads a body of any size.
    /// </description></item>
    /// <item><description>
    /// Empty bodies behave as they do without streaming. A buffered body is a seekable stream, so an
    /// empty one is detected before the serializer runs. A streamed body is not seekable, so the
    /// client detects an empty one from a <c>Content-Length</c> of 0 or, when the length is not sent,
    /// by reading its first byte; an empty body is then handed to the serializer as an empty seekable
    /// stream, exactly as a buffered one is.
    /// </description></item>
    /// <item><description>
    /// The <c>rest.request_duration_ms</c> metric is recorded when the headers arrive, so it excludes
    /// the body download.
    /// </description></item>
    /// </list>
    /// <para>
    /// The error body a <c>Result</c> method keeps in <see cref="HttpError.Body"/> is read from the
    /// stream in the same way, capped at <see cref="MaxErrorBodyBytes"/>.
    /// </para>
    /// </remarks>
    public bool StreamResponses { get; set; }
}
