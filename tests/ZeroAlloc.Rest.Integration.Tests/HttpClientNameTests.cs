using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests.Shipping
{
    [ZeroAllocRestClient]
    public interface ITrackingApi
    {
        [Delete("/shipping/{id}")]
        Task DeleteAsync(int id, CancellationToken ct = default);
    }
}

namespace ZeroAlloc.Rest.Integration.Tests.Billing
{
    [ZeroAllocRestClient]
    public interface ITrackingApi
    {
        [Delete("/billing/{id}")]
        Task DeleteAsync(int id, CancellationToken ct = default);
    }
}

namespace ZeroAlloc.Rest.Integration.Tests
{
    using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;

    // Add{I} registers a named HttpClient. Two client interfaces with one name in different
    // namespaces each get a namespace-qualified one, so each keeps its own settings; a client
    // whose name no other client shares keeps it (#395).
    public sealed class HttpClientNameTests
    {
        [Fact]
        public async Task SameNamedClients_InDifferentNamespaces_ReachTheirOwnHosts()
        {
            var requests = new List<Uri?>();
            HttpMessageHandler Recorder() => new StubHandler((request, _) =>
            {
                lock (requests) requests.Add(request.RequestUri);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            });

            var services = new ServiceCollection();
            services.AddRestSerializer(new ConfiguredSerializer("application/json"));
            Shipping.GeneratedRestClientExtensions.AddITrackingApi(services, o => o.BaseAddress = new Uri("http://shipping.local/"))
                .ConfigurePrimaryHttpMessageHandler(Recorder);
            Billing.GeneratedRestClientExtensions.AddITrackingApi(services, o => o.BaseAddress = new Uri("http://billing.local/"))
                .ConfigurePrimaryHttpMessageHandler(Recorder);
            using var provider = services.BuildServiceProvider();

            await provider.GetRequiredService<Shipping.ITrackingApi>().DeleteAsync(1);
            await provider.GetRequiredService<Billing.ITrackingApi>().DeleteAsync(2);

            Assert.Equal(
                [new Uri("http://shipping.local/shipping/1"), new Uri("http://billing.local/billing/2")],
                requests);
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            Assert.Equal(
                new Uri("http://shipping.local/"),
                factory.CreateClient("ZeroAlloc.Rest.Integration.Tests.Shipping.ITrackingApi").BaseAddress);
        }

        [Fact]
        public void ClientWithAUniqueName_KeepsIt()
        {
            var services = new ServiceCollection();
            services.AddRestSerializer(new ConfiguredSerializer("application/json"));
            services.AddIUserApi(o => o.BaseAddress = new Uri("http://users.local/"));
            using var provider = services.BuildServiceProvider();

            var factory = provider.GetRequiredService<IHttpClientFactory>();
            Assert.Equal(new Uri("http://users.local/"), factory.CreateClient("IUserApi").BaseAddress);
        }
    }
}
