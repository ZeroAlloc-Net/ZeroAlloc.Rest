using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

/// <summary>
/// Verifies that TypedId-decorated structs (from ZeroAlloc.ValueObjects) work correctly
/// as route parameters and query-string parameters in generated REST client methods.
/// TypedId.ToString() produces the ULID string representation which is used for URL building.
/// </summary>
public sealed class TypedIdUserApiTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly ITypedIdUserApi _client;
    private readonly ServiceProvider _provider;

    public TypedIdUserApiTests()
    {
        _server = WireMockServer.Start();

        var services = new ServiceCollection();
        services.AddITypedIdUserApi(options =>
        {
            options.BaseAddress = new Uri(_server.Url!);
            options.UseSerializer<SystemTextJsonSerializer>();
        });

        _provider = services.BuildServiceProvider();
        _client = _provider.GetRequiredService<ITypedIdUserApi>();
    }

    [Fact]
    public async Task GetUser_TypedIdRouteParam_SubstitutedAsUlidString()
    {
        var userId = UserId.New();
        var ulidString = userId.ToString();

        _server.Given(Request.Create().WithPath($"/users/{ulidString}").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody($"\"user-{ulidString}\""));

        var result = await _client.GetUserAsync(userId);

        Assert.Equal($"user-{ulidString}", result);
    }

    [Fact]
    public async Task FindUser_TypedIdQueryParam_AppendedAsUlidString()
    {
        var userId = UserId.New();
        var ulidString = userId.ToString();

        _server.Given(Request.Create()
                    .WithPath("/users")
                    .WithParam("id", ulidString)
                    .UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("\"found\""));

        var result = await _client.FindUserAsync(userId);

        Assert.Equal("found", result);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _server.Dispose();
    }
}
