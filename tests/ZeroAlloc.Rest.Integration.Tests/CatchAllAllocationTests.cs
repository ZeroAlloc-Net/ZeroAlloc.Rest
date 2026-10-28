using System.Net;
using System.Net.Http;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.TestHelpers;

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
    private const int Iterations = 1_000;

    // The whole {**path} call, measured on .NET 10 x64, Release, through a synchronous stub that
    // returns an empty 200. It equals the constant route's call. net8.0 adds what its HttpClient
    // costs more; see RuntimeAllocation.
    private const int CatchAllBytesPerCall = 1_232 + RuntimeAllocation.HttpClientSendExtraBytes;

    [OptimizedBuildFact]
    public void CatchAllHole_AddsNoAllocation_WhenTheValueNeedsNoEscaping()
    {
        using var http = new HttpClient(new StubHandler(static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        ICatchAllAllocationApi api = new CatchAllAllocationApiClient(http, new SystemTextJsonSerializer());
        const string value = "v1/systemone"; // the constant route sends the same path

        AllocationGate.AssertBudget(
            CatchAllBytesPerCall,
            Iterations,
            () => SynchronousCall.Require(api.CatchAllAsync(value)),
            "generated GET with a {**path} value that needs no escaping");
        AllocationGate.AssertNoMoreThan(
            Iterations,
            baseline: () => SynchronousCall.Require(api.ConstantAsync()),
            candidate: () => SynchronousCall.Require(api.CatchAllAsync(value)),
            "{**path} call against the same URL as a constant route");
    }
}
