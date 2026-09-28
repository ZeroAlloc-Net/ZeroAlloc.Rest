using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.DependencyInjection.Tests;

// Minimal stub serializer for tests
file sealed class StubSerializer : IRestSerializer
{
    public string ContentType => "application/json";

    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default) => ValueTask.FromResult(default(T));

    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default) => ValueTask.CompletedTask;
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddZeroAllocClient_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddZeroAllocClient(options =>
        {
            options.BaseAddress = new Uri("https://example.com");
            options.UseSerializer<StubSerializer>();
        });
        Assert.NotNull(builder);
    }

    [Fact]
    public void AddZeroAllocClient_RegistersSerializerInDI()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocClient(options =>
        {
            options.BaseAddress = new Uri("https://example.com");
            options.UseSerializer<StubSerializer>();
        });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IRestSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<StubSerializer>(serializer);
    }

    [Fact]
    public void AddZeroAllocClient_SetsBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocClient(options =>
        {
            options.BaseAddress = new Uri("https://example.com");
            options.UseSerializer<StubSerializer>();
        });
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(ServiceCollectionExtensions.DefaultClientName);
        Assert.Equal(new Uri("https://example.com"), client.BaseAddress);
    }

    [Fact]
    public void ZeroAllocClientOptions_DefaultBaseAddressIsNull()
    {
        var options = new ZeroAllocClientOptions();
        Assert.Null(options.BaseAddress);
    }

    [Fact]
    public void ZeroAllocClientBuilder_ConfigureHttpClient_Works()
    {
        var services = new ServiceCollection();
        var builder = services.AddZeroAllocClient(options =>
        {
            options.BaseAddress = new Uri("https://example.com");
            options.UseSerializer<StubSerializer>();
        });
        // Should not throw
        builder.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
        Assert.NotNull(builder);
    }
}
