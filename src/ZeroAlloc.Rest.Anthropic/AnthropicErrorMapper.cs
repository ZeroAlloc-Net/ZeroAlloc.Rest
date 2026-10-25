using System.Text.Json;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// Maps a failed response to an <see cref="AnthropicApiError"/>. It reads the documented error body
/// <c>{"type":"error","error":{"type","message"},"request_id"}</c>, takes the request id from the <c>request-id</c>
/// header when the body has none, and the wait from the <c>retry-after</c> header.
/// </summary>
public sealed class AnthropicErrorMapper : IHttpErrorMapper<AnthropicApiError>
{
    private const string RequestIdHeader = "request-id";

    /// <inheritdoc />
    public AnthropicApiError Map(HttpError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = (int)error.StatusCode;
        var headerRequestId = FirstHeader(error.Headers, RequestIdHeader);
        return error.Kind switch
        {
            HttpErrorKind.Timeout => new AnthropicApiError(
                "timeout", error.Exception?.Message ?? "The request timed out.", 0, headerRequestId, null),
            HttpErrorKind.Transport => new AnthropicApiError(
                "connection_error", error.Exception?.Message ?? "The request could not be sent.", status, headerRequestId, null),
            HttpErrorKind.Deserialization => new AnthropicApiError(
                "invalid_response", error.Exception?.Message ?? "The response body could not be read.", status, headerRequestId, null),
            _ => FromBody(error, status, headerRequestId),
        };
    }

    private static AnthropicApiError FromBody(HttpError error, int status, string? headerRequestId)
    {
        var retryAfter = error.GetRetryAfter();
        if (TryReadBody(error.Body, out var type, out var message, out var bodyRequestId))
            return new AnthropicApiError(type, message, status, bodyRequestId ?? headerRequestId, retryAfter);

        return new AnthropicApiError(
            $"http_{status}",
            $"The API answered HTTP {status} without an Anthropic error body.",
            status,
            headerRequestId,
            retryAfter);
    }

    // A forward-only read of the error body: no reflection and no document, so it is trim and AOT safe.
    private static bool TryReadBody(ReadOnlyMemory<byte> body, out string type, out string message, out string? requestId)
    {
        type = string.Empty;
        message = string.Empty;
        requestId = null;
        string? errorType = null;
        string? errorMessage = null;
        try
        {
            var reader = new Utf8JsonReader(body.Span);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("error"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject)
                        ReadError(ref reader, ref errorType, ref errorMessage);
                    else
                        reader.Skip();
                }
                else if (reader.ValueTextEquals("request_id"u8))
                {
                    reader.Read();
                    requestId = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            requestId = null;
            return false;
        }

        if (errorType is null)
        {
            requestId = null;
            return false;
        }

        type = errorType;
        message = errorMessage ?? string.Empty;
        return true;
    }

    private static void ReadError(ref Utf8JsonReader reader, ref string? type, ref string? message)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isType = reader.ValueTextEquals("type"u8);
            var isMessage = !isType && reader.ValueTextEquals("message"u8);
            reader.Read();
            if (reader.TokenType == JsonTokenType.String && (isType || isMessage))
            {
                if (isType)
                    type = reader.GetString();
                else
                    message = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }
    }

    private static string? FirstHeader(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string name)
    {
        if (headers.TryGetValue(name, out var values))
            return values.Count > 0 ? values[0] : null;

        // The generated client's dictionary ignores case; an HttpError built by hand may not.
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                return header.Value.Count > 0 ? header.Value[0] : null;
        }

        return null;
    }
}
