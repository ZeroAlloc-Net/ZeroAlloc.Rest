using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Rest.Ollama;

/// <summary>Registers <see cref="OllamaClient"/> with a dependency injection container.</summary>
public static class OllamaServiceCollectionExtensions
{
    /// <summary>Registers <see cref="OllamaClient"/> over a named HTTP client with the configured base address.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options; it runs once, here.</param>
    /// <returns>The builder of the HTTP client, to add handlers to it.</returns>
    public static IHttpClientBuilder AddOllamaClient(this IServiceCollection services, Action<OllamaClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OllamaClientOptions();
        configure(options);

        var builder = services.AddIOllamaApi(o =>
        {
            o.BaseAddress = options.BaseAddress;
            o.UseSerializer(OllamaClient.Serializer);
        });
        services.TryAddTransient(sp => new OllamaClient(sp.GetRequiredService<IOllamaApi>()));
        return builder;
    }
}
