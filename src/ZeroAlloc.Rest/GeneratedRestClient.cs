using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Rest;

/// <summary>
/// Calls the <see cref="IGeneratedRestClient{TSelf}"/> static members of a generated client.
/// Generated clients implement those members explicitly, so they never clash with the client
/// interface's own methods; the generated <c>Add{I}</c> reaches them through this helper.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedRestClient
{
    /// <inheritdoc cref="IGeneratedRestClient{TSelf}.Create"/>
    public static TClient Create<TClient>(HttpClient httpClient, IServiceProvider services)
        where TClient : class, IGeneratedRestClient<TClient>
        => TClient.Create(httpClient, services);

    /// <inheritdoc cref="IGeneratedRestClient{TSelf}.AddSerializers"/>
    public static void AddSerializers<TClient>(IServiceCollection services, ZeroAllocClientOptions options)
        where TClient : class, IGeneratedRestClient<TClient>
        => TClient.AddSerializers(services, options);

    /// <summary>
    /// Registers the serializer set by <c>UseSerializer</c>, if any, as a keyed singleton under
    /// <typeparamref name="TInterface"/>. It never registers the app-wide <see cref="IRestSerializer"/>.
    /// Generated clients call this from <see cref="IGeneratedRestClient{TSelf}.AddSerializers"/>.
    /// </summary>
    public static void AddPerClientSerializer<TInterface>(IServiceCollection services, ZeroAllocClientOptions options)
        where TInterface : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (options.SerializerInstance is not null)
            services.AddKeyedSingleton<IRestSerializer>(typeof(TInterface), options.SerializerInstance);
        else if (options.SerializerType is not null)
            services.AddKeyedSingleton(typeof(IRestSerializer), typeof(TInterface), options.SerializerType);
        else
            return;

        services.TryAddSingleton(new PerClientRestSerializer<TInterface>());
    }
}
