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

        if (options.SerializerType is not null)
            services.AddSingleton(typeof(IRestSerializer), options.SerializerType);

        var httpClientBuilder = services.AddHttpClient(DefaultClientName, client =>
        {
            if (options.BaseAddress is not null)
                client.BaseAddress = options.BaseAddress;
        });

        return new ZeroAllocClientBuilder(httpClientBuilder);
    }
}
