using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests;

// Regression coverage for #301: a library's client must keep its own serializer no matter
// what the host registers, and in which order.

public abstract class ProbeSerializer : IRestSerializer
{
    private int _serializeCalls;

    public abstract string ContentType { get; }

    public int SerializeCalls => _serializeCalls;

    [RequiresDynamicCode("Test serializer.")]
    [RequiresUnreferencedCode("Test serializer.")]
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        => ValueTask.FromResult<T?>(default);

    [RequiresDynamicCode("Test serializer.")]
    [RequiresUnreferencedCode("Test serializer.")]
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _serializeCalls);
        return ValueTask.CompletedTask;
    }
}

public sealed class JevSerializer : ProbeSerializer
{
    public override string ContentType => "application/x-jev";
}

public sealed class HostSerializer : ProbeSerializer
{
    public override string ContentType => "application/x-host";
}

public sealed class ConfiguredSerializer(string contentType) : ProbeSerializer
{
    public override string ContentType => contentType;
}

[ZeroAllocRestClient]
public interface IJevApi
{
    [Post("/answers")]
    Task PostAsync([Body] string body, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[Serializer(typeof(JevSerializer))]
public interface IJevAttributedApi
{
    [Post("/answers")]
    Task PostAsync([Body] string body, CancellationToken ct = default);
}

[ZeroAllocRestClient]
public interface IHostApi
{
    [Post("/host")]
    Task PostAsync([Body] string body, CancellationToken ct = default);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    public List<string?> ContentTypes { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}

public sealed class SerializerIsolationTests
{
    private static readonly Uri BaseAddress = new("http://fake.local/");

    private static void AddLibrary(IServiceCollection services, RecordingHandler handler) =>
        services.AddIJevApi(o =>
        {
            o.BaseAddress = BaseAddress;
            o.UseSerializer<JevSerializer>();
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

    private static void AddHostWithOwnSerializer(IServiceCollection services, RecordingHandler handler) =>
        services.AddIHostApi(o =>
        {
            o.BaseAddress = BaseAddress;
            o.UseSerializer<HostSerializer>();
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

    private static void AddHostWithGlobalSerializer(IServiceCollection services, RecordingHandler handler)
    {
        services.AddRestSerializer<HostSerializer>();
        services.AddIHostApi(o => o.BaseAddress = BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
    }

    private static async Task<(string? Library, string? Host)> SendBoth(IServiceProvider provider, RecordingHandler library, RecordingHandler host)
    {
        await provider.GetRequiredService<IJevApi>().PostAsync("q").ConfigureAwait(false);
        await provider.GetRequiredService<IHostApi>().PostAsync("q").ConfigureAwait(false);
        return (Assert.Single(library.ContentTypes), Assert.Single(host.ContentTypes));
    }

    // The issue's probe: both clients call UseSerializer, in either order.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PerClientSerializers_DoNotOverwriteEachOther(bool hostFirst)
    {
        var library = new RecordingHandler();
        var host = new RecordingHandler();
        var services = new ServiceCollection();
        if (hostFirst) AddHostWithOwnSerializer(services, host);
        AddLibrary(services, library);
        if (!hostFirst) AddHostWithOwnSerializer(services, host);

        using var provider = services.BuildServiceProvider();
        var (libraryType, hostType) = await SendBoth(provider, library, host);

        Assert.Equal("application/x-jev", libraryType);
        Assert.Equal("application/x-host", hostType);
    }

    // The host relies on an app-wide default; the library's UseSerializer must neither
    // replace it nor be replaced by it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PerClientSerializer_AndAppWideDefault_AreIsolated(bool hostFirst)
    {
        var library = new RecordingHandler();
        var host = new RecordingHandler();
        var services = new ServiceCollection();
        if (hostFirst) AddHostWithGlobalSerializer(services, host);
        AddLibrary(services, library);
        if (!hostFirst) AddHostWithGlobalSerializer(services, host);

        using var provider = services.BuildServiceProvider();
        var (libraryType, hostType) = await SendBoth(provider, library, host);

        Assert.Equal("application/x-jev", libraryType);
        Assert.Equal("application/x-host", hostType);
    }

    [Fact]
    public void UseSerializer_DoesNotRegisterAppWideDefault()
    {
        var services = new ServiceCollection();
        AddLibrary(services, new RecordingHandler());

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IRestSerializer>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterfaceLevelSerializer_WinsOverAppWideDefault(bool defaultFirst)
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        if (defaultFirst) services.AddRestSerializer<HostSerializer>();
        services.AddIJevAttributedApi(o => o.BaseAddress = BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        if (!defaultFirst) services.AddRestSerializer<HostSerializer>();

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IJevAttributedApi>().PostAsync("q");

        Assert.Equal("application/x-jev", Assert.Single(handler.ContentTypes));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InterfaceLevelSerializer_WithUseSerializer_ThrowsAtRegistration(bool useInstance)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddIJevAttributedApi(o =>
        {
            if (useInstance) o.UseSerializer(new HostSerializer());
            else o.UseSerializer<HostSerializer>();
        }));

        Assert.Contains(typeof(IJevAttributedApi).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(JevSerializer).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("UseSerializer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseSerializerInstance_UsesThatExactInstance()
    {
        var instance = new ConfiguredSerializer("application/x-configured");
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddRestSerializer<HostSerializer>();
        services.AddIJevApi(o =>
        {
            o.BaseAddress = BaseAddress;
            o.UseSerializer(instance);
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IJevApi>().PostAsync("a");
        await provider.GetRequiredService<IJevApi>().PostAsync("b");

        Assert.Equal(2, instance.SerializeCalls);
        Assert.All(handler.ContentTypes, t => Assert.Equal("application/x-configured", t));
    }

    [Fact]
    public async Task AddRestSerializerInstance_IsTheAppWideDefault()
    {
        var instance = new ConfiguredSerializer("application/x-default");
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddRestSerializer(instance);
        services.AddIHostApi(o => o.BaseAddress = BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IHostApi>().PostAsync("a");

        Assert.Same(instance, provider.GetRequiredService<IRestSerializer>());
        Assert.Equal(1, instance.SerializeCalls);
    }

    [Fact]
    public void ClientWithoutAnySerializer_FailsWithClearMessage()
    {
        var services = new ServiceCollection();
        services.AddIHostApi(o => o.BaseAddress = BaseAddress);

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IHostApi>());

        Assert.Contains(typeof(IHostApi).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("UseSerializer", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AddRestSerializer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseSerializer_LastCallWins()
    {
        var instance = new ConfiguredSerializer("application/x-configured");
        var options = new ZeroAllocClientOptions();

        options.UseSerializer(instance);
        options.UseSerializer<JevSerializer>();
        Assert.Null(options.SerializerInstance);
        Assert.Equal(typeof(JevSerializer), options.SerializerType);

        options.UseSerializer(instance);
        Assert.Same(instance, options.SerializerInstance);
        Assert.Null(options.SerializerType);
    }
}
