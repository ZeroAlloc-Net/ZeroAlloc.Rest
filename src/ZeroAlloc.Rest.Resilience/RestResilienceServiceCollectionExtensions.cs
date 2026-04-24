using System.Diagnostics.CodeAnalysis;
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
    ///   (e.g. <c>UserApiClient</c> for <c>IUserApi</c>).</item>
    ///   <item><typeparamref name="TResilienceProxy"/> — the proxy class emitted by the Resilience
    ///   generator (e.g. <c>IUserApiResilienceProxy</c>).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// The method registers:
    /// <list type="number">
    ///   <item>The typed HTTP client pipeline for <typeparamref name="TInterface"/> /
    ///   <typeparamref name="TRestClient"/> (via
    ///   <see cref="HttpClientFactoryServiceCollectionExtensions.AddHttpClient{TClient,TImplementation}"/>),
    ///   producing the same named-client pipeline as the Rest generator's <c>AddIXxx()</c>.</item>
    ///   <item>A transient factory for <typeparamref name="TInterface"/> that resolves a fresh
    ///   <typeparamref name="TRestClient"/> via <see cref="System.Net.Http.IHttpClientFactory"/>
    ///   and wraps it in the <typeparamref name="TResilienceProxy"/>.</item>
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
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRestClient,
        TResilienceProxy>(
        this IServiceCollection services,
        Func<TRestClient, IServiceProvider, TResilienceProxy> resilienceFactory,
        Action<ZeroAllocClientOptions>? configure = null)
        where TInterface : class
        where TRestClient : class, TInterface
        where TResilienceProxy : class, TInterface
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resilienceFactory);

        var options = new ZeroAllocClientOptions();
        configure?.Invoke(options);

        if (options.SerializerType is not null)
            services.TryAddSingleton(typeof(IRestSerializer), options.SerializerType);

        // Register the typed HTTP client with both type parameters, mirroring exactly the pattern
        // emitted by the Rest generator: AddHttpClient<TInterface, TRestClient>. This produces a
        // named HTTP client pipeline with key typeof(TInterface).Name — the same key the generator
        // uses — so ConfigurePrimaryHttpMessageHandler, AddHttpMessageHandler etc. on the returned
        // builder all apply to the correct pipeline.
        //
        // AddHttpClient<TInterface, TRestClient> internally calls services.AddTransient<TInterface>
        // (not TryAdd), registering TInterface → TRestClient. We immediately replace that with our
        // resilience-proxy factory so that GetRequiredService<TInterface> returns the proxy.
        //
        // The proxy factory uses IHttpClientFactory.CreateClient(typeof(TInterface).Name) to obtain
        // the HttpClient from the pipeline we just configured, then ActivatorUtilities to construct
        // TRestClient, and finally resilienceFactory to wrap it in TResilienceProxy.
        //
        // Replace() removes the first existing TInterface registration and appends the new one,
        // giving idempotent-ish semantics: a second AddRestResilience call replaces the proxy from
        // the first call rather than silently stacking an unused extra registration.
        var builder = services.AddHttpClient<TInterface, TRestClient>(client =>
        {
            if (options.BaseAddress is not null)
                client.BaseAddress = options.BaseAddress;
        });

        services.Replace(ServiceDescriptor.Transient<TInterface>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(typeof(TInterface).Name);
            var restClient = ActivatorUtilities.CreateInstance<TRestClient>(sp, httpClient);
            return resilienceFactory(restClient, sp);
        }));

        return builder;
    }
}
