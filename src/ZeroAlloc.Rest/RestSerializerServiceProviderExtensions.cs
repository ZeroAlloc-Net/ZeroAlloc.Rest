using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Rest;

/// <summary>
/// Resolves the serializer a REST client should use. Generated clients call it, and it is public
/// so hand-written registrations, such as a manually registered resilience proxy, can apply the
/// same rules.
/// </summary>
public static class RestSerializerServiceProviderExtensions
{
    /// <summary>
    /// Resolves the serializer for the client <typeparamref name="TClient"/>: the per-client
    /// serializer registered by <c>UseSerializer</c> first, keyed by <c>typeof(TClient)</c>, then the
    /// app-wide <see cref="IRestSerializer"/>.
    /// </summary>
    /// <remarks>
    /// Per-client serializers need a service provider that implements
    /// <see cref="IKeyedServiceProvider"/>. Without one, only the app-wide serializer is available.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No serializer is configured for the client, or it has a per-client serializer and
    /// <paramref name="services"/> does not support keyed services.
    /// </exception>
    public static IRestSerializer GetRequiredRestSerializer<TClient>(this IServiceProvider services)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services is IKeyedServiceProvider keyed)
        {
            if (keyed.GetKeyedService(typeof(IRestSerializer), typeof(TClient)) is IRestSerializer perClient)
                return perClient;
        }
        else if (services.GetService(typeof(PerClientRestSerializer<TClient>)) is not null)
        {
            throw new InvalidOperationException(
                $"The REST client '{typeof(TClient).FullName}' has a per-client serializer from UseSerializer, " +
                $"but the service provider '{services.GetType().FullName}' does not support keyed services. " +
                "Per-client serializers need a container whose service provider implements IKeyedServiceProvider. " +
                "Use such a container, or register an app-wide default with services.AddRestSerializer<T>() " +
                "instead of calling UseSerializer.");
        }

        return services.GetService<IRestSerializer>()
            ?? throw new InvalidOperationException(
                $"No IRestSerializer is configured for the REST client '{typeof(TClient).FullName}'. " +
                "Call options.UseSerializer<T>() or options.UseSerializer(instance) when registering this client, " +
                "or register an app-wide default with services.AddRestSerializer<T>().");
    }
}

/// <summary>
/// Registered next to a per-client serializer, so a provider without keyed-service support can
/// report that the client's serializer is unreachable instead of silently using another one.
/// </summary>
internal sealed class PerClientRestSerializer<TClient>
    where TClient : class;
