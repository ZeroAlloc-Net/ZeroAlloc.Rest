using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Rest.Integration.Tests;

/// <summary>
/// The bytes one whole generated GET call allocates, as the caller sees it: the generated client,
/// HttpClient, the request and the stub's response, and System.Text.Json reading a small DTO.
/// The README states this figure, so a change to it updates both.
/// </summary>
/// <remarks>
/// In the telemetry collection, so no other test's MeterListener enables the instruments while
/// this measures.
/// </remarks>
[Collection("rest-telemetry-non-parallel")]
public sealed class GetCallAllocationTests
{
    private const int Iterations = 1_000;

    // Measured on .NET 10 x64, Release. Against HttpClient.GetAsync with a literal URL and
    // JsonSerializer, 1,512 B, the generated call adds 176 B: 136 B for the per-request Accept
    // header and 40 B for the URL string built from the id. A hand-written call that sends the
    // same Accept header and builds the same URL allocates the same 1,688 B, as the second test
    // pins. The 136 B is HttpClient's per-header cost: setting Accept on DefaultRequestHeaders
    // allocates the same, because SendAsync merges the defaults into each request. Kept by
    // design, see #441.
    private const int GeneratedGetBytesPerCall = 1_688;

    private static readonly byte[] UserJson = Encoding.UTF8.GetBytes("""{"id":1,"name":"Alice"}""");

    [OptimizedBuildFact]
    public void GeneratedGet_SmallDto_AllocatesTheMeasuredBytesPerCall()
    {
        using var http = CreateHttpClient();
        IUserApi api = new UserApiClient(http, new SystemTextJsonSerializer());

        AllocationGate.AssertBudget(
            GeneratedGetBytesPerCall,
            Iterations,
            () => SynchronousCall.Require(api.GetUserAsync(1)),
            "generated GET returning a small DTO");
    }

    [OptimizedBuildFact]
    public void GeneratedGet_SmallDto_AllocatesNoMoreThanHandWritten()
    {
        using var http = CreateHttpClient();
        var serializer = new SystemTextJsonSerializer();
        IUserApi api = new UserApiClient(http, serializer);
        var id = 1;

        AllocationGate.AssertNoMoreThan(
            Iterations,
            baseline: () => SynchronousCall.Require(HandWrittenGetAsync(http, serializer, id)),
            candidate: () => SynchronousCall.Require(api.GetUserAsync(id)),
            "generated GET against the same call written by hand");
    }

    // The same request the generated client sends: the URL built from the id, the serializer's
    // Accept header, and the body read through the serializer.
    private static async Task<UserDto?> HandWrittenGetAsync(HttpClient http, SystemTextJsonSerializer serializer, int id)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/users/{id}"));
        request.Headers.TryAddWithoutValidation("Accept", serializer.ContentType);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await serializer.DeserializeAsync<UserDto>(stream).ConfigureAwait(false);
    }

    private static HttpClient CreateHttpClient() =>
        new(new StubHandler(static (_, _) =>
        {
            var content = new ByteArrayContent(UserJson);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }))
        {
            BaseAddress = new Uri("http://localhost/"),
        };
}
