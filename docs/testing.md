---
id: testing
title: Testing
slug: /testing
sidebar_position: 9
description: Testing ZeroAlloc.Rest API clients with WireMock.Net.
---

# Testing

## Recommended approach: WireMock.Net

[WireMock.Net](https://github.com/WireMock-Net/WireMock.Net) starts an in-process HTTP server that you can program with expected requests and responses. This tests the full stack — URL building, serialization, request headers — without a real network.

```sh
dotnet add package WireMock.Net
```

## Example test class

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.SystemTextJson;

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
    public async Task ListUsers_WithQueryParam_AppendsToUrl()
    {
        _server.Given(Request.Create().WithPath("/users").WithParam("name", "Alice").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(JsonSerializer.Serialize(
                       new List<UserDto> { new(1, "Alice") }, s_camelCase)));

        var result = await _client.ListUsersAsync("Alice");
        Assert.Single(result);
        Assert.Equal("Alice", result[0].Name);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _server.Dispose();
    }
}
```

## What to test

- **Path parameter substitution** — assert the correct URL path was hit
- **Query string construction** — use `WithParam` in WireMock to assert exact query parameters
- **Body serialization** — use `WithBody` matchers or inspect `_server.LogEntries`
- **Response deserialization** — assert the returned DTO has the expected values
- **Nullable query params** — assert the query string is absent when a nullable param is `null`
- **Delete / void methods** — assert no exception is thrown and the correct status code was returned

## Testing with Result&lt;T, HttpError&gt;

If your interface method returns `Result<T, HttpError>`, assert both the success and failure paths:

```csharp
// Success path
_server.Given(Request.Create().WithPath("/users/1").UsingGet())
    .RespondWith(Response.Create().WithStatusCode(200)
        .WithBodyAsJson(new { id = 1, name = "Alice" }));

var result = await _client.GetUserResultAsync(1);
Assert.True(result.IsSuccess);
Assert.Equal(1, result.Value.Id);

// Failure path
_server.Given(Request.Create().WithPath("/users/99").UsingGet())
    .RespondWith(Response.Create().WithStatusCode(404));

var notFound = await _client.GetUserResultAsync(99);
Assert.False(notFound.IsSuccess);
Assert.Equal(HttpStatusCode.NotFound, notFound.Error.StatusCode);
```

No exception is thrown on 4xx/5xx when the return type is `Result<T, HttpError>` — the error is returned as a value.
