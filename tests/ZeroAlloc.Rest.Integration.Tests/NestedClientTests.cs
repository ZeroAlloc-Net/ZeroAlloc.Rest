using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Integration.Tests;

// Two nested client interfaces with one name, in containing types that are not partial, each get
// their own client, Add method and named HttpClient, so their settings stay apart (#394).

public static class NestedOrders
{
    [ZeroAllocRestClient]
    public interface IApi
    {
        [Delete("/orders/{id}")]
        Task DeleteAsync(int id, CancellationToken ct = default);
    }
}

public sealed class NestedCustomers
{
    public static class Inner
    {
        [ZeroAllocRestClient]
        public interface IApi
        {
            [Delete("/customers/{id}")]
            Task DeleteAsync(int id, CancellationToken ct = default);
        }
    }
}

public sealed class NestedClientTests
{
    [Fact]
    public async Task SameNamedNestedClients_KeepTheirOwnSettings()
    {
        var requests = new List<Uri?>();
        HttpMessageHandler Recorder() => new StubHandler((request, _) =>
        {
            lock (requests) requests.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        var services = new ServiceCollection();
        services.AddRestSerializer(new ConfiguredSerializer("application/json"));
        services.AddNestedOrders_IApi(o => o.BaseAddress = new Uri("http://orders.local/"))
            .ConfigurePrimaryHttpMessageHandler(Recorder);
        services.AddNestedCustomers_Inner_IApi(o => o.BaseAddress = new Uri("http://customers.local/"))
            .ConfigurePrimaryHttpMessageHandler(Recorder);
        using var provider = services.BuildServiceProvider();

        var orders = provider.GetRequiredService<NestedOrders.IApi>();
        var customers = provider.GetRequiredService<NestedCustomers.Inner.IApi>();
        await orders.DeleteAsync(1);
        await customers.DeleteAsync(2);

        Assert.IsType<NestedOrders_ApiClient>(orders);
        Assert.IsType<NestedCustomers_Inner_ApiClient>(customers);
        Assert.Equal(
            [new Uri("http://orders.local/orders/1"), new Uri("http://customers.local/customers/2")],
            requests);
    }
}
