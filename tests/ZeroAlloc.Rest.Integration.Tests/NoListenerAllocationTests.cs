using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Rest.Integration.Tests;

public sealed record AllocationPayload(string Name, int Count);

// Issue #428: calls with route, query and body parameters, to compare with hand-written ones.
[ZeroAllocRestClient]
public interface IParameterAllocationApi
{
    [Get("users/{id}/items/{name}")]
    Task PathAsync(int id, string name, CancellationToken ct = default);

    [Get("search")]
    Task QueryAsync([Query] string q, [Query] int page, [Query] double? ratio, [Query] int? size, CancellationToken ct = default);

    [Post("users/{id}")]
    Task BodyAsync(long id, [Body] AllocationPayload body, CancellationToken ct = default);
}

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
    private const int Iterations = 1_000;

    // The bytes each whole generated call allocates, measured on .NET 10 x64, Release, through a
    // synchronous stub that returns an empty 200: HttpClient, the request and its Accept header,
    // the response, and what the generated code adds. Each equals the same call written by hand.
    // net8.0 adds what its HttpClient costs more; see RuntimeAllocation.
    private const int PingBytesPerCall = 1_136 + RuntimeAllocation.HttpClientSendExtraBytes;
    private const int PathBytesPerCall = 1_352 + RuntimeAllocation.HttpClientSendExtraBytes;
    private const int BodyBytesPerCall = 1_848 + RuntimeAllocation.HttpClientSendExtraBytes;

    [OptimizedBuildFact]
    public void GeneratedCall_AllocatesNoMoreThanHandWritten_WithNothingListening()
    {
        var serializer = new SystemTextJsonSerializer();
        using var http = new HttpClient(new StubHandler(static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        IPingApi api = new PingApiClient(http, serializer);

        AssertBudgetAndNoMoreThanHandWritten(
            PingBytesPerCall,
            () => api.PingAsync(),
            () => HandWrittenAsync(http, serializer),
            "generated GET with no parameters");
    }

    // A hand-written call formats its values straight into one interpolated string, invariantly,
    // and escapes the text values: one string for the URL, and one per value that needs escaping.
    [OptimizedBuildFact]
    public void GeneratedCallWithPathParameters_AllocatesNoMoreThanHandWritten()
    {
        var (http, serializer, api) = CreateParameterClient();
        using var _ = http;

        AssertBudgetAndNoMoreThanHandWritten(
            PathBytesPerCall,
            () => api.PathAsync(12345, "widget"),
            () => SendAsync(http, serializer, HttpMethod.Get,
                string.Create(CultureInfo.InvariantCulture, $"users/{12345}/items/{Uri.EscapeDataString("widget")}"), null),
            "generated GET with path parameters");
    }

    // The value that needs escaping costs its escaped string on both sides.
    [OptimizedBuildTheory]
    [InlineData("plain", 1_400 + RuntimeAllocation.HttpClientSendExtraBytes)]
    [InlineData("needs escaping/ü", 2_280)]
    public void GeneratedCallWithQueryParameters_AllocatesNoMoreThanHandWritten(string q, int bytesPerCall)
    {
        var (http, serializer, api) = CreateParameterClient();
        using var _ = http;

        AssertBudgetAndNoMoreThanHandWritten(
            bytesPerCall,
            () => api.QueryAsync(q, 3, 0.25, null),
            () => SendAsync(http, serializer, HttpMethod.Get,
                string.Create(CultureInfo.InvariantCulture, $"search?q={Uri.EscapeDataString(q)}&page={3}&ratio={0.25}"), null),
            $"generated GET with query parameters, q = \"{q}\"");
    }

    [OptimizedBuildFact]
    public void GeneratedCallWithBodyParameter_AllocatesNoMoreThanHandWritten()
    {
        var (http, serializer, api) = CreateParameterClient();
        using var _ = http;
        var payload = new AllocationPayload("widget", 3);

        AssertBudgetAndNoMoreThanHandWritten(
            BodyBytesPerCall,
            () => api.BodyAsync(9876543210L, payload),
            () => SendAsync(http, serializer, HttpMethod.Post,
                string.Create(CultureInfo.InvariantCulture, $"users/{9876543210L}"), payload),
            "generated POST with a JSON body");
    }

    private static (HttpClient Http, SystemTextJsonSerializer Serializer, IParameterAllocationApi Api) CreateParameterClient()
    {
        var serializer = new SystemTextJsonSerializer();
        var http = new HttpClient(new StubHandler(static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        return (http, serializer, new ParameterAllocationApiClient(http, serializer));
    }

    // The whole generated call is held to its measured bytes, and to no more than the same call
    // written by hand, both measured in this run.
    private static void AssertBudgetAndNoMoreThanHandWritten(int bytesPerCall, Func<Task> generatedCall, Func<Task> handWrittenCall, string label)
    {
        AllocationGate.AssertBudget(bytesPerCall, Iterations, () => SynchronousCall.Require(generatedCall()), label);
        AllocationGate.AssertNoMoreThan(
            Iterations,
            baseline: () => SynchronousCall.Require(handWrittenCall()),
            candidate: () => SynchronousCall.Require(generatedCall()),
            label + " against the same call written by hand");
    }

    // The body is serialized into a MemoryStream and sent from its buffer, without a copy.
    private static async Task SendAsync(HttpClient http, SystemTextJsonSerializer serializer, HttpMethod method, string url, AllocationPayload? body)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Accept", serializer.ContentType);
        if (body is not null)
        {
            var stream = new MemoryStream();
            await serializer.SerializeAsync(stream, body).ConfigureAwait(false);
            request.Content = new ByteArrayContent(stream.GetBuffer(), 0, (int)stream.Length)
            {
                Headers = { ContentType = new MediaTypeHeaderValue(serializer.ContentType) },
            };
        }
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static async Task HandWrittenAsync(HttpClient http, SystemTextJsonSerializer serializer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "ping");
        request.Headers.TryAddWithoutValidation("Accept", serializer.ContentType);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
