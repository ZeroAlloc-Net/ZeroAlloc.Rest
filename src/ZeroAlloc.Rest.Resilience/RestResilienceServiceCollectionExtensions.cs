using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Rest.Resilience;

/// <summary>
/// Extension methods that wire a ZeroAlloc.Rest-generated typed HTTP client through a
/// ZeroAlloc.Resilience-generated proxy.  Both generators run independently at compile time;
/// this class composes their outputs in the DI container at registration time — one generator
/// pass, no runtime decorator chain.
/// </summary>
public static class RestResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers a resilience-wrapped Rest client for <typeparamref name="TInterface"/>.
    ///
    /// <para>
    /// Call order matters for the three type parameters:
    /// <list type="bullet">
    ///   <item><typeparamref name="TInterface"/> — the [ZeroAllocRestClient] interface annotated with
    ///   [Retry] / [Timeout] / [CircuitBreaker].  Both generators target this interface.</item>
    ///   <item><typeparamref name="TRestClient"/> — the concrete class emitted by the Rest generator
    ///   (e.g. <c>UserApiClient</c> for <c>IUserApi</c>). It must be a generated client: the bridge
    ///   builds it through <see cref="IGeneratedRestClient{TSelf}"/>.</item>
    ///   <item><typeparamref name="TResilienceProxy"/> — the proxy class emitted by the Resilience
    ///   generator (e.g. <c>IUserApiResilienceProxy</c>).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// The method registers:
    /// <list type="number">
    ///   <item>The client's serializers, exactly as the generated <c>AddIXxx()</c> registers them.</item>
    ///   <item>The named HTTP client pipeline for <typeparamref name="TInterface"/>, the same
    ///   named-client pipeline as the Rest generator's <c>AddIXxx()</c>.</item>
    ///   <item>A transient factory for <typeparamref name="TInterface"/> that builds a fresh
    ///   <typeparamref name="TRestClient"/> from that pipeline and wraps it in the
    ///   <typeparamref name="TResilienceProxy"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// The returned <see cref="IHttpClientBuilder"/> can be used to configure the underlying
    /// <see cref="System.Net.Http.HttpClient"/> (e.g. <c>AddHttpMessageHandler</c>,
    /// <c>ConfigureHttpClient</c>).
    /// </para>
    /// </summary>
    /// <typeparam name="TInterface">The [ZeroAllocRestClient] interface.</typeparam>
    /// <typeparam name="TRestClient">The Rest-generator-emitted concrete client class.</typeparam>
    /// <typeparam name="TResilienceProxy">The Resilience-generator-emitted proxy class.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="resilienceFactory">
    /// Factory that constructs the <typeparamref name="TResilienceProxy"/> given the inner
    /// <typeparamref name="TRestClient"/> and a service provider for resolving policy singletons
    /// (e.g. <see cref="RetryPolicy"/>, <see cref="TimeoutPolicy"/>, <see cref="CircuitBreakerPolicy"/>).
    /// </param>
    /// <param name="configure">Optional callback to configure <see cref="ZeroAllocClientOptions"/>
    /// (base address, serializer).</param>
    public static IHttpClientBuilder AddRestResilience<
        TInterface,
        TRestClient,
        TResilienceProxy>(
        this IServiceCollection services,
        Func<TRestClient, IServiceProvider, TResilienceProxy> resilienceFactory,
        Action<ZeroAllocClientOptions>? configure = null)
        where TInterface : class
        where TRestClient : class, TInterface, IGeneratedRestClient<TRestClient>
        where TResilienceProxy : class, TInterface
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resilienceFactory);

        var options = new ZeroAllocClientOptions();
        configure?.Invoke(options);

        // Registers the serializers exactly as the generated Add{I} does: the interface-level
        // [Serializer] type, rejecting a conflicting UseSerializer, or the per-client serializer
        // keyed by the interface.
        TRestClient.AddSerializers(services, options);

        // The same named HTTP client pipeline as the generated Add{I}, keyed by the interface name,
        // so ConfigurePrimaryHttpMessageHandler, AddHttpMessageHandler etc. on the returned builder
        // apply to it.
        var builder = services.AddHttpClient(typeof(TInterface).Name, client =>
        {
            if (options.BaseAddress is not null)
                client.BaseAddress = options.BaseAddress;
        });

        // TRestClient.Create picks the serializer with the same precedence as the generated
        // typed-client factory, with no reflection or ActivatorUtilities. Replace() removes an
        // existing TInterface registration, for example from an earlier AddRestResilience or Add{I}
        // call, rather than silently stacking an unused one.
        services.Replace(ServiceDescriptor.Transient<TInterface>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(typeof(TInterface).Name);
            return resilienceFactory(TRestClient.Create(httpClient, sp), sp);
        }));

        return builder;
    }
}
