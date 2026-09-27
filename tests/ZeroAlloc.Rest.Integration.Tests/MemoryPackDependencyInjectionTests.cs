using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.MemoryPack;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

// Rest 3.0: MemoryPackRestSerializer serves only registered types plus MemoryPack's built-ins.
// A client using a MemoryPack [Serializer] override gets it from DI; the generated TryAddSingleton
// keeps the configured instance the caller registered first, and otherwise activates the
// parameterless one, which names the missing registration at the first call.

[MemoryPackable]
public sealed partial record MemoryPackEchoDto(int Id, string Name);

[ZeroAllocRestClient]
public interface IMemoryPackEchoApi
{
    [Post("/echo")]
    [Serializer(typeof(MemoryPackRestSerializer))]
    Task<MemoryPackEchoDto> EchoAsync([Body] MemoryPackEchoDto body, CancellationToken ct = default);
}

public sealed class MemoryPackDependencyInjectionTests
{
    private static readonly Uri s_baseAddress = new("http://stub.local/");

    [Fact]
    public async Task PreRegisteredInstance_ResolvesThroughDi_AndRoundTrips()
    {
        var serializer = new MemoryPackRestSerializer(types => types.Add<MemoryPackEchoDto>());
        var services = new ServiceCollection();
        services.AddSingleton(serializer);
        AddEchoClient(services);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMemoryPackEchoApi>();
        var echoed = await client.EchoAsync(new MemoryPackEchoDto(7, "Grace"));

        Assert.Equal(new MemoryPackEchoDto(7, "Grace"), echoed);
        Assert.Same(serializer, provider.GetRequiredService<MemoryPackRestSerializer>());
    }

    [Fact]
    public async Task WithoutPreRegisteredInstance_TheAutoRegisteredSerializerNamesTheFix()
    {
        var services = new ServiceCollection();
        AddEchoClient(services);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMemoryPackEchoApi>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.EchoAsync(new MemoryPackEchoDto(7, "Grace")));
        Assert.Contains(
            $"new MemoryPackRestSerializer(types => types.Add<{nameof(MemoryPackEchoDto)}>())",
            ex.Message,
            StringComparison.Ordinal);
    }

    private static void AddEchoClient(IServiceCollection services)
        => services.AddIMemoryPackEchoApi(o =>
        {
            o.BaseAddress = s_baseAddress;
            o.UseSerializer(new SystemTextJsonSerializer());
        }).ConfigurePrimaryHttpMessageHandler(() => new StubHandler(EchoAsync));

    private static async Task<HttpResponseMessage> EchoAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-memorypack");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
