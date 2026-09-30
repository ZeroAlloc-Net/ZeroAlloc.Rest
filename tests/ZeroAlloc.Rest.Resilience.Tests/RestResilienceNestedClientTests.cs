using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Resilience.Tests;

public static class ResilientOrders
{
    [ZeroAllocRestClient]
    public interface IApi
    {
        [Get("/orders/{id}")]
        Task<string> GetAsync(int id, CancellationToken ct = default);
    }

    // Stands in for a Resilience-generated proxy: the bridge only needs a TInterface wrapper.
    public sealed class Proxy(IApi inner) : IApi
    {
        public Task<string> GetAsync(int id, CancellationToken ct = default) => inner.GetAsync(id, ct);
    }
}

public static class ResilientCustomers
{
    [ZeroAllocRestClient]
    public interface IApi
    {
        [Get("/customers/{id}")]
        Task<string> GetAsync(int id, CancellationToken ct = default);
    }

    public sealed class Proxy(IApi inner) : IApi
    {
        public Task<string> GetAsync(int id, CancellationToken ct = default) => inner.GetAsync(id, ct);
    }
}

// The bridge names its HttpClient as the generated Add{I} does, with the containing types for a
// nested interface, so two nested interfaces with one name keep their own settings (#394).
public sealed class RestResilienceNestedClientTests
{
    [Fact]
    public async Task SameNamedNestedClients_KeepTheirOwnBaseAddress()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, "\"ok\"");
        var services = new ServiceCollection();
        services.AddRestResilience<ResilientOrders.IApi, ResilientOrders_ApiClient, ResilientOrders.Proxy>(
            (inner, _) => new ResilientOrders.Proxy(inner),
            o =>
            {
                o.BaseAddress = new Uri("http://orders.local/");
                o.UseSerializer<SystemTextJsonSerializer>();
            }).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddRestResilience<ResilientCustomers.IApi, ResilientCustomers_ApiClient, ResilientCustomers.Proxy>(
            (inner, _) => new ResilientCustomers.Proxy(inner),
            o =>
            {
                o.BaseAddress = new Uri("http://customers.local/");
                o.UseSerializer<SystemTextJsonSerializer>();
            }).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ResilientOrders.IApi>().GetAsync(1);
        await provider.GetRequiredService<ResilientCustomers.IApi>().GetAsync(2);

        Assert.Equal(new Uri("http://orders.local/orders/1"), handler.Requests[0].RequestUri);
        Assert.Equal(new Uri("http://customers.local/customers/2"), handler.Requests[1].RequestUri);
    }
}
