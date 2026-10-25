using System.Net;
using System.Text;

namespace ZeroAlloc.Rest.Anthropic.Tests.Support;

/// <summary>Builds Messages API responses in the documented wire format.</summary>
internal static class Responses
{
    public const string MessageJson =
        """{"id":"msg_1","type":"message","role":"assistant","content":[{"type":"text","text":"Hello!"}],"model":"claude-opus-5-5","stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":6}}""";

    public const string MessageStart =
        """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","content":[],"model":"claude-opus-5-5","stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":25,"output_tokens":1}}}""";

    public const string BlockStart = """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""";
    public const string BlockDelta = """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hi"}}""";
    public const string BlockStop = """{"type":"content_block_stop","index":0}""";
    public const string MessageDelta = """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":15}}""";
    public const string MessageStop = """{"type":"message_stop"}""";
    public const string Ping = """{"type":"ping"}""";

    public static HttpResponseMessage Message() => Json(HttpStatusCode.OK, MessageJson);

    public static HttpResponseMessage Json(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
            response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    public static string ErrorBody(string type, string message, string? requestId = null) =>
        requestId is null
            ? $$$"""{"type":"error","error":{"type":"{{{type}}}","message":"{{{message}}}"}}"""
            : $$$"""{"type":"error","error":{"type":"{{{type}}}","message":"{{{message}}}"},"request_id":"{{{requestId}}}"}""";

    public static HttpResponseMessage Error(int status, string type, string message, string? retryAfter = null)
    {
        var headers = retryAfter is null ? [] : new[] { ("retry-after", retryAfter) };
        return Json((HttpStatusCode)status, ErrorBody(type, message), headers);
    }

    public static HttpResponseMessage Overloaded(string? retryAfter = "0") => Error(529, "overloaded_error", "Overloaded", retryAfter);

    /// <summary>A text/event-stream body of the given events, each with its event: line.</summary>
    public static HttpResponseMessage Stream(params (string Event, string Data)[] events) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(Sse(events), Encoding.UTF8, "text/event-stream"),
    };

    public static string Sse(params (string Event, string Data)[] events)
    {
        var sb = new StringBuilder();
        foreach (var (name, data) in events)
            sb.Append("event: ").Append(name).Append('\n').Append("data: ").Append(data).Append("\n\n");
        return sb.ToString();
    }

    public static (string, string)[] FullStream() =>
    [
        ("message_start", MessageStart),
        ("content_block_start", BlockStart),
        ("ping", Ping),
        ("content_block_delta", BlockDelta),
        ("content_block_stop", BlockStop),
        ("message_delta", MessageDelta),
        ("message_stop", MessageStop),
    ];
}
