using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Rest;

public static class ServiceCollectionExtensions
{
    public const string DefaultClientName = "ZeroAllocClient";

    public static ZeroAllocClientBuilder AddZeroAllocClient(
        this IServiceCollection services,
        Action<ZeroAllocClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new ZeroAllocClientOptions();
        configure(options);

        // The default named client has no client interface to key on, so its serializer is the
        // app-wide default, as it has always been.
        if (options.SerializerInstance is not null)
            services.AddSingleton(options.SerializerInstance);
        else if (options.SerializerType is not null)
            services.AddSingleton(typeof(IRestSerializer), options.SerializerType);

        var httpClientBuilder = services.AddHttpClient(DefaultClientName, client =>
        {
            if (options.BaseAddress is not null)
                client.BaseAddress = options.BaseAddress;
        });

        return new ZeroAllocClientBuilder(httpClientBuilder);
    }

    /// <summary>
    /// Registers <typeparamref name="TSerializer"/> as the app-wide <see cref="IRestSerializer"/>,
    /// used by every generated client that does not call <c>UseSerializer</c> and whose interface
    /// has no <c>[Serializer]</c>. The last registration wins.
    /// </summary>
    public static IServiceCollection AddRestSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSerializer>(
        this IServiceCollection services)
        where TSerializer : class, IRestSerializer
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<IRestSerializer, TSerializer>();
    }

    /// <summary>
    /// Registers <paramref name="serializer"/> as the app-wide <see cref="IRestSerializer"/>,
    /// used by every generated client that does not call <c>UseSerializer</c> and whose interface
    /// has no <c>[Serializer]</c>. The last registration wins.
    /// </summary>
    public static IServiceCollection AddRestSerializer(this IServiceCollection services, IRestSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serializer);
        return services.AddSingleton(serializer);
    }
}
