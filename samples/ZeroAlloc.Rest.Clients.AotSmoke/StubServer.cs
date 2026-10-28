using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.Clients.AotSmoke;

// A minimal HTTP/1.1 server on loopback: it answers one connection with a canned response and records
// the request line and body it received.
internal sealed class StubServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public StubServer() => _listener.Start();

    public Uri BaseAddress => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    public string LastRequest { get; private set; } = "";

    public async Task ServeAsync(int status, string json, CancellationToken ct)
    {
        using var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        var stream = client.GetStream();
        LastRequest = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
        var body = Encoding.UTF8.GetBytes(json);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Stub\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    // Buffers the bytes until the head and Content-Length bytes of body have arrived, then decodes them once,
    // so a multi-byte character split across two reads still decodes.
    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var received = new MemoryStream();
        var headEnd = -1;
        var length = 0;
        while (true)
        {
            if (headEnd < 0)
            {
                headEnd = IndexOfHeadEnd(received.GetBuffer().AsSpan(0, (int)received.Length));
                if (headEnd >= 0)
                    length = ContentLength(Encoding.ASCII.GetString(received.GetBuffer(), 0, headEnd));
            }

            if (headEnd >= 0 && received.Length - (headEnd + 4) >= length)
                break;

            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            received.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length);
    }

    private static int IndexOfHeadEnd(ReadOnlySpan<byte> bytes) => bytes.IndexOf("\r\n\r\n"u8);

    private static int ContentLength(string head)
    {
        foreach (var line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                return int.Parse(line.AsSpan(15).Trim(), CultureInfo.InvariantCulture);
        }

        return 0;
    }

    public void Dispose() => _listener.Stop();
}
