using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZeroAlloc.Rest.Resilience;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>Registers <see cref="AnthropicClient"/> with a dependency injection container.</summary>
public static class AnthropicServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnthropicClient"/> and the retrying <see cref="IAnthropicMessagesApi"/> it uses, over a
    /// named HTTP client with the configured base address, headers and timeout.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options; it runs once, here.</param>
    /// <returns>The builder of the HTTP client, to add handlers to it.</returns>
    /// <remarks>
    /// The timeout is set on the <see cref="HttpClient"/>. A resilience handler added to the returned builder brings
    /// its own timeouts, which apply on top of it.
    /// </remarks>
    public static IHttpClientBuilder AddAnthropicClient(this IServiceCollection services, Action<AnthropicClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AnthropicClientOptions();
        configure(options);
        var policies = AnthropicClient.Policies(options);

        // Replace, not TryAdd: a second call must not keep the first call's retry count under its own headers.
        services.Replace(ServiceDescriptor.Singleton(policies));
        var builder = services.AddRestResilience<IAnthropicMessagesApi, AnthropicMessagesApiClient, IAnthropicMessagesApiResilienceProxy>(
            (inner, sp) => new IAnthropicMessagesApiResilienceProxy(inner, sp.GetRequiredService<AnthropicMessagesApiResiliencePolicies>()),
            o =>
            {
                o.BaseAddress = options.BaseAddress;
                o.UseSerializer(AnthropicClient.Serializer);
            });
        builder.ConfigureHttpClient(http => AnthropicClient.ConfigureHttpClient(http, options));
        services.TryAddTransient(sp => new AnthropicClient(sp.GetRequiredService<IAnthropicMessagesApi>()));
        return builder;
    }
}
