using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Resilience.Tests.Shipping
{
    [ZeroAllocRestClient]
    public interface IStatusApi
    {
        [Get("/shipping/{id}")]
        Task<string> GetAsync(int id, CancellationToken ct = default);
    }

    public sealed class Proxy(IStatusApi inner) : IStatusApi
    {
        public Task<string> GetAsync(int id, CancellationToken ct = default) => inner.GetAsync(id, ct);
    }
}

namespace ZeroAlloc.Rest.Resilience.Tests.Billing
{
    [ZeroAllocRestClient]
    public interface IStatusApi
    {
        [Get("/billing/{id}")]
        Task<string> GetAsync(int id, CancellationToken ct = default);
    }

    public sealed class Proxy(IStatusApi inner) : IStatusApi
    {
        public Task<string> GetAsync(int id, CancellationToken ct = default) => inner.GetAsync(id, ct);
    }
}

namespace ZeroAlloc.Rest.Resilience.Tests
{
    // AddRestResilience uses the named HttpClient the generated client declares, so two same-named
    // interfaces in different namespaces keep their own settings, as with the generated Add{I} (#395).
    public sealed class RestResilienceHttpClientNameTests
    {
        [Fact]
        public async Task SameNamedClients_InDifferentNamespaces_ReachTheirOwnHosts()
        {
            var handler = new FakeMessageHandler();
            handler.SetResponse(HttpStatusCode.OK, "\"ok\"");
            var services = new ServiceCollection();
            services.AddRestResilience<Shipping.IStatusApi, Shipping.StatusApiClient, Shipping.Proxy>(
                (inner, _) => new Shipping.Proxy(inner),
                o =>
                {
                    o.BaseAddress = new Uri("http://shipping.local/");
                    o.UseSerializer<SystemTextJsonSerializer>();
                }).ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddRestResilience<Billing.IStatusApi, Billing.StatusApiClient, Billing.Proxy>(
                (inner, _) => new Billing.Proxy(inner),
                o =>
                {
                    o.BaseAddress = new Uri("http://billing.local/");
                    o.UseSerializer<SystemTextJsonSerializer>();
                }).ConfigurePrimaryHttpMessageHandler(() => handler);
            using var provider = services.BuildServiceProvider();

            await provider.GetRequiredService<Shipping.IStatusApi>().GetAsync(1);
            await provider.GetRequiredService<Billing.IStatusApi>().GetAsync(2);

            Assert.Equal(new Uri("http://shipping.local/shipping/1"), handler.Requests[0].RequestUri);
            Assert.Equal(new Uri("http://billing.local/billing/2"), handler.Requests[1].RequestUri);
            Assert.Equal(
                new Uri("http://billing.local/"),
                provider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("ZeroAlloc.Rest.Resilience.Tests.Billing.IStatusApi").BaseAddress);
        }
    }
}
