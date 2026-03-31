using System.Net;

namespace ZeroAlloc.Rest;

public sealed class ApiResponse<T>(
    T? content,
    HttpStatusCode statusCode,
    IReadOnlyDictionary<string, IEnumerable<string>> headers)
{
    public T? Content { get; } = content;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public bool IsSuccessStatusCode { get; } = (int)statusCode >= 200 && (int)statusCode <= 299;
    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; } = headers;
}
