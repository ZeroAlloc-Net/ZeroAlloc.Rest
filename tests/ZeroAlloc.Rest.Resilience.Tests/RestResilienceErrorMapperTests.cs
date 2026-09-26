using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Resilience.Tests;

// Issue #300 through the Resilience bridge: AddRestResilience registers the mapper through the
// client's AddSerializers and builds the client with the concrete mapper type.

public sealed record BridgeError(HttpStatusCode Status, int BodyLength, string Source);

public sealed class BridgeErrorMapper : IHttpErrorMapper<BridgeError>
{
    public BridgeError Map(HttpError error) => new(error.StatusCode, error.Body.Length, "library");
}

public sealed class HostBridgeErrorMapper : IHttpErrorMapper<BridgeError>
{
    public BridgeError Map(HttpError error) => new(error.StatusCode, error.Body.Length, "host");
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(BridgeErrorMapper))]
[Retry(MaxAttempts = 2, BackoffMs = 1)]
public interface IMappedBridgeApi
{
    [Get("/items/{id}")]
    Task<Result<string, BridgeError>> GetItemAsync(int id, CancellationToken ct = default);
}

public sealed class RestResilienceErrorMapperTests
{
    private const string Problem = "{\"code\":\"x\"}";

    [Fact]
    public async Task Bridge_ResolvesTheMapper_AndReturnsTheMappedError()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.UnprocessableEntity, Problem);
        using var provider = Build(handler, _ => { });

        var result = await provider.GetRequiredService<IMappedBridgeApi>().GetItemAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(new BridgeError(HttpStatusCode.UnprocessableEntity, Problem.Length, "library"), result.Error);
        // [Retry] passes a returned Result through unchanged; it retries only a thrown exception.
        // maxAttempts is 2 below, so a second request here would mean the bridge retried the
        // mapped failure, which it must not.
        Assert.Single(handler.Requests);
        Assert.NotNull(provider.GetService<BridgeErrorMapper>());
    }

    [Fact]
    public async Task Bridge_HostRegistrationOfTheMapperInterface_DoesNotReplaceTheLibraryMapper()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.UnprocessableEntity, Problem);
        using var provider = Build(handler, s => s.AddSingleton<IHttpErrorMapper<BridgeError>, HostBridgeErrorMapper>());

        var result = await provider.GetRequiredService<IMappedBridgeApi>().GetItemAsync(1);

        Assert.Equal("library", result.Error.Source);
    }

    private static ServiceProvider Build(FakeMessageHandler handler, Action<IServiceCollection> before)
    {
        var services = new ServiceCollection();
        before(services);
        services.AddMappedBridgeApiResiliencePolicies((_, p) =>
            p.Retry = new RetryPolicy(maxAttempts: 2, backoffMs: 1, jitter: false, perAttemptTimeoutMs: 0));
        services.AddRestResilience<IMappedBridgeApi, MappedBridgeApiClient, IMappedBridgeApiResilienceProxy>(
                resilienceFactory: (inner, sp) => new IMappedBridgeApiResilienceProxy(
                    inner,
                    sp.GetRequiredService<MappedBridgeApiResiliencePolicies>()),
                configure: o =>
                {
                    o.BaseAddress = new Uri("http://fake.local/");
                    o.UseSerializer<SystemTextJsonSerializer>();
                })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }
}
