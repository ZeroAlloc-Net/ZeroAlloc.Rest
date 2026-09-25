using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests;

// Runtime coverage for serializer combinations: interface-level, method-level, per-client and
// app-wide serializers together, internal serializer types, AddZeroAllocClient, and containers
// without keyed-service support.

public sealed class UploadSerializer : ProbeSerializer
{
    public override string ContentType => "application/x-upload";
}

// Internal, as a library would keep its serializer; the interface and generated client are public.
internal sealed class InternalLibrarySerializer : ProbeSerializer
{
    public override string ContentType => "application/x-internal";
}

[ZeroAllocRestClient]
[Serializer(typeof(JevSerializer))]
public interface IAttributedOverrideApi
{
    [Post("/default")]
    Task PostDefaultAsync([Body] string body, CancellationToken ct = default);

    [Post("/same")]
    [Serializer(typeof(JevSerializer))]
    Task PostSameAsync([Body] string body, CancellationToken ct = default);

    [Post("/upload")]
    [Serializer(typeof(UploadSerializer))]
    Task PostUploadAsync([Body] string body, CancellationToken ct = default);
}

[ZeroAllocRestClient]
public interface IOverrideApi
{
    [Post("/default")]
    Task PostDefaultAsync([Body] string body, CancellationToken ct = default);

    [Post("/upload")]
    [Serializer(typeof(UploadSerializer))]
    Task PostUploadAsync([Body] string body, CancellationToken ct = default);
}

[ZeroAllocRestClient]
public interface IInternalMethodSerializerApi
{
    [Post("/default")]
    Task PostDefaultAsync([Body] string body, CancellationToken ct = default);

    [Post("/answers")]
    [Serializer(typeof(InternalLibrarySerializer))]
    Task PostInternalAsync([Body] string body, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[Serializer(typeof(InternalLibrarySerializer))]
public interface IInternalSerializerApi
{
    [Post("/answers")]
    Task PostAsync([Body] string body, CancellationToken ct = default);
}

// Forwards only IServiceProvider, like a third-party container adapter without keyed services.
internal sealed class NonKeyedServiceProvider(IServiceProvider inner) : IServiceProvider
{
    public object? GetService(Type serviceType) => inner.GetService(serviceType);
}

public sealed class SerializerCombinationTests
{
    private static readonly Uri BaseAddress = new("http://fake.local/");

    [Fact]
    public async Task AttributeWithMethodOverrides_EachMethodUsesItsSerializer()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddRestSerializer<HostSerializer>();
        services.AddIAttributedOverrideApi(o => o.BaseAddress = BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<IAttributedOverrideApi>();
        await api.PostDefaultAsync("a");
        await api.PostSameAsync("b");
        await api.PostUploadAsync("c");

        Assert.Equal(["application/x-jev", "application/x-jev", "application/x-upload"], handler.ContentTypes);
    }

    [Theory]
    [InlineData(true, false, "application/x-jev")]
    [InlineData(false, true, "application/x-host")]
    [InlineData(true, true, "application/x-jev")]
    public async Task MethodOverride_WithPerClientAndOrGlobal_EachMethodUsesItsSerializer(
        bool perClient, bool global, string expectedDefault)
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        if (global) services.AddRestSerializer<HostSerializer>();
        services.AddIOverrideApi(o =>
        {
            o.BaseAddress = BaseAddress;
            if (perClient) o.UseSerializer<JevSerializer>();
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<IOverrideApi>();
        await api.PostDefaultAsync("a");
        await api.PostUploadAsync("b");

        Assert.Equal([expectedDefault, "application/x-upload"], handler.ContentTypes);
    }

    [Fact]
    public async Task InternalInterfaceLevelSerializer_OnPublicInterface_IsUsed()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddIInternalSerializerApi(o => o.BaseAddress = BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IInternalSerializerApi>().PostAsync("a");

        Assert.Equal("application/x-internal", Assert.Single(handler.ContentTypes));
    }

    [Fact]
    public async Task InternalMethodLevelSerializer_OnPublicInterface_IsUsed()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddRestSerializer<HostSerializer>();
        services.AddIInternalMethodSerializerApi(o => o.BaseAddress = BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<IInternalMethodSerializerApi>();
        await api.PostDefaultAsync("a");
        await api.PostInternalAsync("b");

        Assert.Equal(["application/x-host", "application/x-internal"], handler.ContentTypes);
    }

    [Fact]
    public void AddZeroAllocClient_InstanceOverload_RegistersThatInstanceAppWide()
    {
        var instance = new ConfiguredSerializer("application/x-configured");
        var services = new ServiceCollection();
        services.AddZeroAllocClient(o => o.UseSerializer(instance));

        using var provider = services.BuildServiceProvider();

        Assert.Same(instance, provider.GetRequiredService<IRestSerializer>());
    }

    [Fact]
    public void NonKeyedContainer_WithAppWideDefault_Works()
    {
        var services = new ServiceCollection();
        services.AddRestSerializer<HostSerializer>();
        services.AddIHostApi(o => o.BaseAddress = BaseAddress);

        using var provider = services.BuildServiceProvider();
        var client = GeneratedRestClient.Create<HostApiClient>(new HttpClient(), new NonKeyedServiceProvider(provider));

        Assert.NotNull(client);
    }

    [Fact]
    public void NonKeyedContainer_WithPerClientSerializer_FailsSayingKeyedServicesAreNeeded()
    {
        var services = new ServiceCollection();
        services.AddRestSerializer<HostSerializer>();
        services.AddIHostApi(o => o.UseSerializer<JevSerializer>());

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GeneratedRestClient.Create<HostApiClient>(new HttpClient(), new NonKeyedServiceProvider(provider)));

        Assert.Contains(typeof(IHostApi).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("keyed services", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IKeyedServiceProvider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonKeyedContainer_WithoutAnySerializer_FailsWithTheUsualMessage()
    {
        var services = new ServiceCollection();
        services.AddIHostApi(o => o.BaseAddress = BaseAddress);

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GeneratedRestClient.Create<HostApiClient>(new HttpClient(), new NonKeyedServiceProvider(provider)));

        Assert.Contains("AddRestSerializer", ex.Message, StringComparison.Ordinal);
    }
}
