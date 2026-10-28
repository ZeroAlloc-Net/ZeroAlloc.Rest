using System.Net;

namespace ZeroAlloc.Rest.Anthropic.Tests.Support;

/// <summary>A body that sends <paramref name="first"/>, then fails the way a closed connection does.</summary>
internal sealed class DroppingContent(string first) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new NotSupportedException();

    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new DroppingStream(first));

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
