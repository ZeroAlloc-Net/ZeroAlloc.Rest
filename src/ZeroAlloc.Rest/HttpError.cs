using System.Net;

namespace ZeroAlloc.Rest;

public sealed record HttpError(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Message = null);
