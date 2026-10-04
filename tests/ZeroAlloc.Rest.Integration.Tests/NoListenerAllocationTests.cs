using System.Net;
using System.Net.Http;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

/// <summary>
/// With nothing listening, a generated call allocates no more than the same call written by hand
/// against <see cref="HttpClient"/> (#406). Both go through one client and one synchronous stub, so
/// what HttpClient, the request and the response cost is the same on both sides, and the difference
/// is what the generated code adds: before #406 an Accept header object, a params tag array per
/// instrument and a boxed status code, 272 bytes a call.
/// </summary>
/// <remarks>
/// In the telemetry collection, so no other test's MeterListener enables the instruments while
/// this measures.
/// </remarks>
[Collection("rest-telemetry-non-parallel")]
public sealed class NoListenerAllocationTests
{
    [Fact]
    public async Task GeneratedCall_AllocatesNoMoreThanHandWritten_WithNothingListening()
    {
        var serializer = new SystemTextJsonSerializer();
        using var http = new HttpClient(new StubHandler(static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        IPingApi api = new PingApiClient(http, serializer);

        // Warm both paths, so one-time costs such as statics and JIT tiers are not measured.
        for (var i = 0; i < 50; i++)
        {
            await api.PingAsync();
            await HandWrittenAsync(http, serializer);
        }

        var generated = await AllocatedByAsync(() => api.PingAsync());
        var handWritten = await AllocatedByAsync(() => HandWrittenAsync(http, serializer));

        Assert.True(
            generated <= handWritten,
            $"The generated call allocated {generated} bytes, the hand-written one {handWritten}.");
    }

    private static async Task HandWrittenAsync(HttpClient http, SystemTextJsonSerializer serializer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "ping");
        request.Headers.TryAddWithoutValidation("Accept", serializer.ContentType);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // Every step of these calls completes synchronously, so the whole call runs on this thread.
    private static async Task<long> AllocatedByAsync(Func<Task> call)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var task = call();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(task.IsCompletedSuccessfully, "The call did not complete synchronously, so its allocations were not all on this thread.");
        await task.ConfigureAwait(false);
        return allocated;
    }
}
