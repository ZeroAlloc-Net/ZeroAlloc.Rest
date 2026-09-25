using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Rest.Resilience.Tests;

// Regression coverage for #301 through the Resilience bridge: the bridge must pick the same
// serializer the generated Add{I} would. Precedence: an interface-level [Serializer] wins;
// otherwise the per-client UseSerializer, then the app-wide IRestSerializer.

public sealed class TaggedSerializer(string contentType) : IRestSerializer
{
    public string ContentType => contentType;

    [RequiresDynamicCode("Test serializer.")]
    [RequiresUnreferencedCode("Test serializer.")]
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        => ValueTask.FromResult<T?>(default);

    [RequiresDynamicCode("Test serializer.")]
    [RequiresUnreferencedCode("Test serializer.")]
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

public sealed class AttributeSerializer : IRestSerializer
{
    public string ContentType => "application/x-attribute";

    [RequiresDynamicCode("Test serializer.")]
    [RequiresUnreferencedCode("Test serializer.")]
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        => ValueTask.FromResult<T?>(default);

    [RequiresDynamicCode("Test serializer.")]
    [RequiresUnreferencedCode("Test serializer.")]
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

[ZeroAllocRestClient]
[Serializer(typeof(AttributeSerializer))]
[Retry(MaxAttempts = 2, BackoffMs = 1)]
public interface IAttributedApi
{
    [Post("/items")]
    Task CreateItemAsync([Body] string name, CancellationToken ct = default);
}

// Declares methods named like IGeneratedRestClient's static members: the generated client
// implements those explicitly, so this compiles and the bridge still reaches them.
[ZeroAllocRestClient]
[Retry(MaxAttempts = 2, BackoffMs = 1)]
public interface IClashApi
{
    [Get("/items/{id}")]
    Task<string> GetItemAsync(int id, CancellationToken ct = default);

    [Get("/create")]
    Task<string> Create(System.Net.Http.HttpClient httpClient, IServiceProvider services);

    [Post("/serializers")]
    Task AddSerializers(IServiceCollection services, ZeroAllocClientOptions options);
}

public sealed class RestResilienceSerializerTests
{
    private static readonly Uri BaseAddress = new("http://fake.local/");

    private static void AddTestApi(
        IServiceCollection services,
        FakeMessageHandler handler,
        Action<ZeroAllocClientOptions> configure)
    {
        services.AddTestApiResiliencePolicies((_, p) =>
            p.Retry = new RetryPolicy(maxAttempts: 1, backoffMs: 1, jitter: false, perAttemptTimeoutMs: 0));
        services.AddRestResilience<ITestApi, TestApiClient, ITestApiResilienceProxy>(
            resilienceFactory: (inner, sp) => new ITestApiResilienceProxy(
                inner,
                sp.GetRequiredService<TestApiResiliencePolicies>()),
            configure: o =>
            {
                o.BaseAddress = BaseAddress;
                configure(o);
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PerClientSerializer_WinsOverAppWideDefault(bool defaultFirst)
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, "\"ok\"");
        var perClient = new TaggedSerializer("application/x-per-client");
        var services = new ServiceCollection();
        if (defaultFirst) services.AddRestSerializer(new TaggedSerializer("application/x-default"));
        AddTestApi(services, handler, o => o.UseSerializer(perClient));
        if (!defaultFirst) services.AddRestSerializer(new TaggedSerializer("application/x-default"));

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ITestApi>().CreateItemAsync("x");

        Assert.Equal("application/x-per-client", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task WithoutPerClientSerializer_UsesAppWideDefault()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, "\"ok\"");
        var services = new ServiceCollection();
        AddTestApi(services, handler, _ => { });
        services.AddRestSerializer(new TaggedSerializer("application/x-default"));

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ITestApi>().CreateItemAsync("x");

        Assert.Equal("application/x-default", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void WithoutAnySerializer_FailsWithClearMessage()
    {
        var services = new ServiceCollection();
        AddTestApi(services, new FakeMessageHandler(), _ => { });

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ITestApi>());

        Assert.Contains(typeof(ITestApi).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("AddRestSerializer", ex.Message, StringComparison.Ordinal);
    }

    private static IHttpClientBuilder AddAttributedApi(
        IServiceCollection services,
        FakeMessageHandler handler,
        Action<ZeroAllocClientOptions> configure)
    {
        services.AddAttributedApiResiliencePolicies((_, p) =>
            p.Retry = new RetryPolicy(maxAttempts: 1, backoffMs: 1, jitter: false, perAttemptTimeoutMs: 0));
        return services.AddRestResilience<IAttributedApi, AttributedApiClient, IAttributedApiResilienceProxy>(
            resilienceFactory: (inner, sp) => new IAttributedApiResilienceProxy(
                inner,
                sp.GetRequiredService<AttributedApiResiliencePolicies>()),
            configure: o =>
            {
                o.BaseAddress = BaseAddress;
                configure(o);
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
    }

    [Fact]
    public async Task ClientWhoseInterfaceDeclaresCreateAndAddSerializers_WorksThroughTheBridge()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, "\"ok\"");
        var services = new ServiceCollection();
        services.AddClashApiResiliencePolicies((_, p) =>
            p.Retry = new RetryPolicy(maxAttempts: 1, backoffMs: 1, jitter: false, perAttemptTimeoutMs: 0));
        services.AddRestResilience<IClashApi, ClashApiClient, IClashApiResilienceProxy>(
            resilienceFactory: (inner, sp) => new IClashApiResilienceProxy(
                inner,
                sp.GetRequiredService<ClashApiResiliencePolicies>()),
            configure: o =>
            {
                o.BaseAddress = BaseAddress;
                o.UseSerializer(new TaggedSerializer("application/x-per-client"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IClashApi>().GetItemAsync(1);

        Assert.Contains("application/x-per-client", handler.Requests[0].Headers.Accept.ToString(), StringComparison.Ordinal);
    }

    // No manual registration of AttributeSerializer: the bridge registers the declared type.
    [Fact]
    public async Task InterfaceLevelSerializer_IsRegisteredAutomatically_AndWinsOverAppWideDefault()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, null);
        var services = new ServiceCollection();
        services.AddRestSerializer(new TaggedSerializer("application/x-default"));
        AddAttributedApi(services, handler, _ => { });

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IAttributedApi>().CreateItemAsync("x");

        Assert.Equal("application/x-attribute", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void InterfaceLevelSerializer_WithUseSerializer_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => AddAttributedApi(
            services,
            new FakeMessageHandler(),
            o => o.UseSerializer(new TaggedSerializer("application/x-per-client"))));

        Assert.Contains(typeof(IAttributedApi).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(AttributeSerializer).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("UseSerializer", ex.Message, StringComparison.Ordinal);
    }
}
