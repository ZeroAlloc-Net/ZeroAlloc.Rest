using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Rest.Anthropic.Recorder;

/// <summary>
/// Saves the request body as sent, the response body as received and a meta file with the status and two
/// allowlisted response headers. It never reads a request header, so the key cannot reach a file.
/// </summary>
internal sealed class RecordingHandler(string directory, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    /// <summary>The transcript the next request belongs to.</summary>
    public string? Name { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var name = Name ?? throw new InvalidOperationException("Set Name before every request.");

        var requestBody = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (request.Content is not null)
        {
            // Hand the same bytes on, with the content headers, so what is saved is exactly what is sent.
            var replay = new ByteArrayContent(requestBody);
            foreach (var header in request.Content.Headers)
                replay.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = replay;
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Buffer the body and give back a content over the same bytes. Streaming is not live in the recorder,
        // and the client still parses the buffered server-sent events.
        var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var buffered = new ByteArrayContent(responseBody);
        foreach (var header in response.Content.Headers)
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content.Dispose();
        response.Content = buffered;

        var contentType = response.Content.Headers.ContentType?.ToString();
        var isSse = contentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true;
        var requestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;

        await File.WriteAllBytesAsync(Path.Combine(directory, $"{name}.request.json"), requestBody, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, isSse ? $"{name}.response.sse" : $"{name}.response.json"), responseBody, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, $"{name}.meta.json"), Meta((int)response.StatusCode, contentType, requestId), cancellationToken).ConfigureAwait(false);
        return response;
    }

    // Only the allowlist: content-type and request-id. Nothing else is ever written.
    private static byte[] Meta(int status, string? contentType, string? requestId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("status", status);
            writer.WriteStartObject("headers");
            if (contentType is not null)
                writer.WriteString("content-type", contentType);
            if (requestId is not null)
                writer.WriteString("request-id", requestId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return [.. stream.ToArray(), .. Encoding.UTF8.GetBytes("\n")];
    }
}
