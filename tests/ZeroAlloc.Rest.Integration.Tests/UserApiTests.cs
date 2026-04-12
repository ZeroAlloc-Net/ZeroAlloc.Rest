using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

public sealed class UserApiTests : IDisposable
{
    private static readonly JsonSerializerOptions s_camelCase = new(JsonSerializerDefaults.Web);

    private readonly WireMockServer _server;
    private readonly IUserApi _client;
    private readonly ServiceProvider _provider;

    public UserApiTests()
    {
        _server = WireMockServer.Start();

        var services = new ServiceCollection();
        // Use the generated AddIUserApi extension method
        services.AddIUserApi(options =>
        {
            options.BaseAddress = new Uri(_server.Url!);
            options.UseSerializer<SystemTextJsonSerializer>();
        });

        _provider = services.BuildServiceProvider();
        _client = _provider.GetRequiredService<IUserApi>();
    }

    [Fact]
    public async Task GetUser_ReturnsDeserializedUser()
    {
        _server.Given(Request.Create().WithPath("/users/1").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(1, "Alice"), s_camelCase)));

        var user = await _client.GetUserAsync(1);
        Assert.Equal(1, user.Id);
        Assert.Equal("Alice", user.Name);
    }

    [Fact]
    public async Task CreateUser_SerializesBody_ReturnsCreatedUser()
    {
        _server.Given(Request.Create().WithPath("/users").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(2, "Bob"), s_camelCase)));

        var result = await _client.CreateUserAsync(new CreateUserRequest("Bob"));
        Assert.Equal(2, result.Id);
        Assert.Equal("Bob", result.Name);
    }

    [Fact]
    public async Task ListUsers_WithQueryParam_AppendsToUrl()
    {
        _server.Given(Request.Create().WithPath("/users").WithParam("name", "Alice").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new List<UserDto> { new(1, "Alice") }, s_camelCase)));

        var result = await _client.ListUsersAsync("Alice");
        Assert.Single(result);
        Assert.Equal("Alice", result[0].Name);
    }

    [Fact]
    public async Task DeleteUser_SendsDeleteRequest()
    {
        _server.Given(Request.Create().WithPath("/users/5").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(204));

        await _client.DeleteUserAsync(5); // Should not throw
    }

    [Fact]
    public async Task GetUserResult_Success_ReturnsSuccessResult()
    {
        _server.Given(Request.Create().WithPath("/users/42/result").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(42, "Charlie"), s_camelCase)));

        var result = await _client.GetUserResultAsync(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.Id);
        Assert.Equal("Charlie", result.Value.Name);
    }

    [Fact]
    public async Task GetUserResult_NotFound_ReturnsFailureResult()
    {
        _server.Given(Request.Create().WithPath("/users/99/result").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(404));

        var result = await _client.GetUserResultAsync(99);
        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.StatusCode);
    }

    [Fact]
    public async Task StaticHeader_SentWithRequest()
    {
        _server.Given(Request.Create()
                    .WithPath("/users/1/raw")
                    .WithHeader("Accept", "*application/octet-stream*")
                    .UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("\"raw-data\""));

        var result = await _client.GetUserRawAsync(1);
        Assert.Equal("raw-data", result);

        var logEntry = _server.LogEntries.Single(e => string.Equals(e.RequestMessage?.Path, "/users/1/raw", StringComparison.Ordinal));
        var acceptValues = string.Join(", ", logEntry.RequestMessage!.Headers!["Accept"]);
        Assert.Contains("application/octet-stream", acceptValues, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListUsers_WithCollectionQuery_RepeatsKey()
    {
        _server.Given(Request.Create()
                    .WithPath("/users")
                    .WithParam("tags", "admin", "active")
                    .UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new List<UserDto> { new(1, "Alice") }, s_camelCase)));

        var result = await _client.ListUsersByTagsAsync(new List<string> { "admin", "active" });
        Assert.Single(result);
        Assert.Equal("Alice", result[0].Name);
    }

    [Fact]
    public async Task FormBody_SendsFormEncodedContent()
    {
        _server.Given(Request.Create()
                    .WithPath("/oauth/token")
                    .WithBody(b => b != null && b.Contains("grant_type=client_credentials", StringComparison.Ordinal))
                    .UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(new UserDto(1, "token"), s_camelCase)));

        var result = await _client.GetTokenAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "my-app"
        });
        Assert.Equal(1, result.Id);
        var logEntry = _server.LogEntries.Single(e =>
            string.Equals(e.RequestMessage?.Path, "/oauth/token", StringComparison.Ordinal));
        var contentType = string.Join(", ", logEntry.RequestMessage!.Headers!["Content-Type"]);
        Assert.Contains("application/x-www-form-urlencoded", contentType, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _server.Dispose();
    }
}
