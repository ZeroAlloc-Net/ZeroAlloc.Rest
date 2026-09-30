using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.AotSmoke;

// Issue #358: raw Stream bodies on methods that throw, beside the generated PetStore client's
// Result methods.
[ZeroAllocRestClient]
public interface IFileApi
{
    [Put("/files/{name}")]
    Task UploadAsync(string name, [Body(ContentType = "text/plain")] Stream body, CancellationToken ct = default);

    [Get("/files/{name}")]
    Task<Stream> DownloadAsync(string name, CancellationToken ct = default);
}
