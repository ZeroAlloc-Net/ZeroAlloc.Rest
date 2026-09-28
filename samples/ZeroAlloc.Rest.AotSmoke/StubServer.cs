using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.AotSmoke;

// A minimal HTTP/1.1 server on loopback: it answers each connection with the next canned response
// and records the request line and body it received.
internal sealed class StubServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public StubServer() => _listener.Start();

    public Uri BaseAddress => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    public string LastRequest { get; private set; } = "";

    public Task ServeAsync(int status, string json, CancellationToken ct) => ServeAsync(status, json, chunked: false, ct);

    // With `chunked`, the body is sent with Transfer-Encoding: chunked and no Content-Length, so the
    // client cannot know its length before reading it.
    public async Task ServeAsync(int status, string json, bool chunked, CancellationToken ct)
    {
        using var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        var stream = client.GetStream();
        LastRequest = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
        var body = Encoding.UTF8.GetBytes(json);
        var framing = chunked ? "Transfer-Encoding: chunked" : $"Content-Length: {body.Length}";
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Stub\r\nContent-Type: application/json\r\n{framing}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        if (!chunked)
        {
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
            return;
        }

        if (body.Length > 0)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"{body.Length:X}\r\n"), ct).ConfigureAwait(false);
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
            await stream.WriteAsync("\r\n"u8.ToArray(), ct).ConfigureAwait(false);
        }
        await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), ct).ConfigureAwait(false);
    }

    // Reads the head, then Content-Length bytes of body.
    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var text = new StringBuilder();
        int headEnd;
        while ((headEnd = text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        var head = headEnd < 0 ? text.ToString() : text.ToString(0, headEnd);
        var length = 0;
        foreach (var line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line.AsSpan(15).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        while (headEnd >= 0 && text.Length - (headEnd + 4) < length)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        return text.ToString();
    }

    public void Dispose() => _listener.Stop();
}
