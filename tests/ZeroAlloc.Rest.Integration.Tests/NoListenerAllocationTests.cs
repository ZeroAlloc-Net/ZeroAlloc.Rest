using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

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

    // A hand-written call formats its values straight into one interpolated string, invariantly,
    // and escapes the text values: one string for the URL, and one per value that needs escaping.
    [Fact]
    public async Task GeneratedCallWithPathParameters_AllocatesNoMoreThanHandWritten()
    {
        var (http, serializer, api) = CreateParameterClient();
        using var _ = http;

        await AssertNoMoreThanHandWrittenAsync(
            () => api.PathAsync(12345, "widget"),
            () => SendAsync(http, serializer, HttpMethod.Get,
                string.Create(CultureInfo.InvariantCulture, $"users/{12345}/items/{Uri.EscapeDataString("widget")}"), null));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("needs escaping/ü")]
    public async Task GeneratedCallWithQueryParameters_AllocatesNoMoreThanHandWritten(string q)
    {
        var (http, serializer, api) = CreateParameterClient();
        using var _ = http;

        await AssertNoMoreThanHandWrittenAsync(
            () => api.QueryAsync(q, 3, 0.25, null),
            () => SendAsync(http, serializer, HttpMethod.Get,
                string.Create(CultureInfo.InvariantCulture, $"search?q={Uri.EscapeDataString(q)}&page={3}&ratio={0.25}"), null));
    }

    [Fact]
    public async Task GeneratedCallWithBodyParameter_AllocatesNoMoreThanHandWritten()
    {
        var (http, serializer, api) = CreateParameterClient();
        using var _ = http;
        var payload = new AllocationPayload("widget", 3);

        await AssertNoMoreThanHandWrittenAsync(
            () => api.BodyAsync(9876543210L, payload),
            () => SendAsync(http, serializer, HttpMethod.Post,
                string.Create(CultureInfo.InvariantCulture, $"users/{9876543210L}"), payload));
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

    private static async Task AssertNoMoreThanHandWrittenAsync(Func<Task> generatedCall, Func<Task> handWrittenCall)
    {
        // Warm both paths, so one-time costs such as statics and JIT tiers are not measured.
        for (var i = 0; i < 50; i++)
        {
            await generatedCall().ConfigureAwait(false);
            await handWrittenCall().ConfigureAwait(false);
        }

        var generated = await AllocatedByAsync(generatedCall).ConfigureAwait(false);
        var handWritten = await AllocatedByAsync(handWrittenCall).ConfigureAwait(false);

        Assert.True(
            generated <= handWritten,
            $"The generated call allocated {generated} bytes, the hand-written one {handWritten}.");
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
