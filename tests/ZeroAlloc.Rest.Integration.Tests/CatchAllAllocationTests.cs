using System.Net;
using System.Net.Http;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

[ZeroAllocRestClient]
public interface ICatchAllAllocationApi
{
    [Get("{**path}")]
    Task CatchAllAsync(string path, CancellationToken ct = default);

    [Get("v1/systemone")]
    Task ConstantAsync(CancellationToken ct = default);
}

/// <summary>
/// A {**name} value that needs no escaping costs nothing for its path (#425). A whole generated
/// call also allocates the request and the response, so the call is measured against a method with
/// the same URL as a constant route, which allocates nothing for its path. Both go through one
/// client and one synchronous stub, so the difference is what the {**name} hole adds: the escape
/// helper and the interpolated string around it.
/// </summary>
/// <remarks>
/// In the telemetry collection, so no other test's MeterListener enables the instruments while
/// this measures. Allocation figures are only meaningful in Release.
/// </remarks>
[Collection("rest-telemetry-non-parallel")]
public sealed class CatchAllAllocationTests
{
    [Fact]
    public async Task CatchAllHole_AddsNoAllocation_WhenTheValueNeedsNoEscaping()
    {
        using var http = new HttpClient(new StubHandler(static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        ICatchAllAllocationApi api = new CatchAllAllocationApiClient(http, new SystemTextJsonSerializer());
        const string value = "v1/systemone"; // the constant route sends the same path

        // Warm both paths, so one-time costs such as statics and JIT tiers are not measured.
        for (var i = 0; i < 50; i++)
        {
            await api.CatchAllAsync(value);
            await api.ConstantAsync();
        }

        var catchAll = await AllocatedByAsync(() => api.CatchAllAsync(value));
        var constant = await AllocatedByAsync(() => api.ConstantAsync());

        Assert.True(
            catchAll <= constant,
            $"The {{**path}} call allocated {catchAll} bytes, the constant route {constant}.");
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
