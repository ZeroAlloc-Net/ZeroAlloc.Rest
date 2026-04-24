using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Resilience;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Rest.Resilience.Tests;

/// <summary>
/// Integration tests for ZeroAlloc.Rest.Resilience bridge.
///
/// Both source generators run at compile time in this project:
///   - ZeroAlloc.Rest.Generator  → emits TestApiClient : ITestApi
///   - ZeroAlloc.Resilience.Generator → emits ITestApiResilienceProxy : ITestApi
///
/// AddRestResilience wires them: ITestApi resolves to
/// ITestApiResilienceProxy(inner: TestApiClient(httpClient)).
/// </summary>
public sealed class RestResilienceTests : IDisposable
{
    private readonly FakeMessageHandler _handler;
    private readonly ServiceProvider _provider;

    public RestResilienceTests()
    {
        _handler = new FakeMessageHandler();

        var services = new ServiceCollection();

        // Register retry policy (normally auto-registered by the Resilience generator's DI extension,
        // but here we register manually because we bypass it to avoid the AddTransient<TImpl> conflict).
        services.AddSingleton(new RetryPolicy(maxAttempts: 3, backoffMs: 10, jitter: false, perAttemptTimeoutMs: 0));

        services.AddRestResilience<ITestApi, TestApiClient, ITestApiResilienceProxy>(
            resilienceFactory: (inner, sp) => new ITestApiResilienceProxy(
                inner,
                sp.GetRequiredService<RetryPolicy>()),
            configure: options =>
            {
                options.BaseAddress = new Uri("http://fake.local/");
                options.UseSerializer<SystemTextJsonSerializer>();
            })
            // Inject our fake HTTP handler so no real network calls are made.
            .ConfigurePrimaryHttpMessageHandler(() => _handler);

        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvedInstance_IsResilienceProxy_NotDirectClient()
    {
        var api = _provider.GetRequiredService<ITestApi>();

        // The interface should resolve to the proxy, not directly to TestApiClient.
        Assert.IsNotType<TestApiClient>(api);
    }

    [Fact]
    public async Task GetItem_HappyPath_ReturnsDeserializedResponse()
    {
        var api = _provider.GetRequiredService<ITestApi>();
        _handler.SetResponse(HttpStatusCode.OK, "\"hello-world\"");

        var result = await api.GetItemAsync(42, CancellationToken.None);

        Assert.Equal("hello-world", result);
        Assert.Single(_handler.Requests);
        Assert.Contains("/items/42", _handler.Requests[0].RequestUri!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetItem_TransientFailure_RetriesAndSucceeds()
    {
        var api = _provider.GetRequiredService<ITestApi>();

        // First two calls fail, third succeeds.
        int callCount = 0;
        _handler.SetDynamicResponse(_ =>
        {
            callCount++;
            if (callCount < 3)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("\"retry-success\"", Encoding.UTF8, "application/json")
            };
        });

        var result = await api.GetItemAsync(1, CancellationToken.None);

        Assert.Equal("retry-success", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task GetItem_AllAttemptsExhausted_ThrowsResilienceException()
    {
        var api = _provider.GetRequiredService<ITestApi>();
        _handler.SetResponse(HttpStatusCode.InternalServerError, null);

        await Assert.ThrowsAsync<ResilienceException>(
            () => api.GetItemAsync(99, CancellationToken.None));
    }

    [Fact]
    public async Task CreateItem_HappyPath_SendsPostAndReturnsResponse()
    {
        var api = _provider.GetRequiredService<ITestApi>();
        _handler.SetResponse(HttpStatusCode.OK, "\"created-ok\"");

        var result = await api.CreateItemAsync("my-item", CancellationToken.None);

        Assert.Equal("created-ok", result);
        Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, _handler.Requests[0].Method);
        Assert.Contains("/items", _handler.Requests[0].RequestUri!.PathAndQuery, StringComparison.Ordinal);
    }

    public void Dispose() => _provider.Dispose();
}
