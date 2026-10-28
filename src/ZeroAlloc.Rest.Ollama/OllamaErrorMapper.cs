using System.Text.Json;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>
/// Maps a failed response to an <see cref="OllamaApiError"/>. It reads the body <c>{"error":"..."}</c> with a
/// forward-only reader: no reflection and no document, so it is trim and AOT safe.
/// </summary>
internal sealed class OllamaErrorMapper : IHttpErrorMapper<OllamaApiError>
{
    public OllamaApiError Map(HttpError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = (int)error.StatusCode;
        return error.Kind switch
        {
            HttpErrorKind.Timeout => new OllamaApiError(0, error.Exception?.Message ?? "The request timed out."),
            HttpErrorKind.Transport => new OllamaApiError(status, error.Exception?.Message ?? "The request could not be sent."),
            HttpErrorKind.Deserialization => new OllamaApiError(status, error.Exception?.Message ?? "The response body could not be read."),
            _ => new OllamaApiError(status, ReadMessage(error.Body) ?? $"Ollama answered HTTP {status} without an error body."),
        };
    }

    private static string? ReadMessage(ReadOnlyMemory<byte> body)
    {
        try
        {
            var reader = new Utf8JsonReader(body.Span);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isError = reader.ValueTextEquals("error"u8);
                reader.Read();
                if (isError && reader.TokenType == JsonTokenType.String)
                    return reader.GetString();
                reader.Skip();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
