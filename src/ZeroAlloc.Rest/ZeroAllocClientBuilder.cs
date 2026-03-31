using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Rest;

public sealed class ZeroAllocClientBuilder(IHttpClientBuilder httpClientBuilder)
{
    internal IHttpClientBuilder HttpClientBuilder { get; } = httpClientBuilder;

    public ZeroAllocClientBuilder ConfigureHttpClient(Action<HttpClient> configure)
    {
        HttpClientBuilder.ConfigureHttpClient(configure);
        return this;
    }
}
