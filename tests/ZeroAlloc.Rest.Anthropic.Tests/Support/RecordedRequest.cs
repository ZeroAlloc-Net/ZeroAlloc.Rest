namespace ZeroAlloc.Rest.Anthropic.Tests.Support;

/// <summary>A request as the handler saw it, read before the client disposed it.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    long Timestamp);
