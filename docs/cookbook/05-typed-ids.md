---
id: cookbook-typed-ids
title: "05 — Strongly-typed IDs"
slug: /cookbook/typed-ids
sidebar_position: 105
description: Use ZeroAlloc.ValueObjects [TypedId] structs as route and query parameters in generated REST clients.
---

# Recipe 05 — Strongly-typed IDs

**Goal:** Use `[TypedId]` structs from
[`ZeroAlloc.ValueObjects`](https://www.nuget.org/packages/ZeroAlloc.ValueObjects)
as route and query parameters in a generated REST client, eliminating raw
`Guid` / `int` / `string` IDs from the client surface.

## Why typed IDs?

Replacing `Guid userId` with `UserId userId` removes a whole class of mix-up
bugs (`GetOrder(userId)` no longer compiles), keeps the URL surface
self-documenting, and works with no extra REST-side configuration: the
generator already calls `id.ToString()` for path and query interpolation,
which is exactly what `[TypedId]` overrides to produce a stable
ULID / UUIDv7 / Snowflake / sequential string.

## 1. Install packages

```sh
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.Generator
dotnet add package ZeroAlloc.Rest.SystemTextJson
dotnet add package ZeroAlloc.ValueObjects
```

The Rest generator does not need to know anything about `[TypedId]` — the
emitted code uses the same `Uri.EscapeDataString(value.ToString())` shape
that any custom struct with a meaningful `ToString()` would benefit from.

## 2. Declare the typed ID

```csharp
using ZeroAlloc.ValueObjects;

[TypedId]
public readonly partial record struct UserId;
```

Defaults to `IdStrategy.Ulid` over a `Guid` backing field. `UserId.New()`
produces an instance whose `ToString()` is the 26-char Crockford-base32
ULID. `UserId.Parse(s)` and `UserId.TryParse(s, out var id)` are generated
for you (`IParsable<UserId>`), so any minimal-API endpoint that takes a
`UserId` route or query parameter binds without further wiring.

## 3. Define the client interface

```csharp
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.ValueObjects;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(UserId id, CancellationToken ct = default);

    [Get("/users")]
    Task<List<UserDto>> SearchAsync(
        [Query] UserId? since = null,
        CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest body, CancellationToken ct = default);
}

public sealed record UserDto(UserId Id, string Name);
public sealed record CreateUserRequest(string Name);
```

The generator emits, for the route segment:

```csharp
var url = $"/users/{Uri.EscapeDataString(id.ToString())}";
```

…and for the nullable `[Query]` parameter:

```csharp
if (since != null)
{
    AppendToUrl(urlBuilder, hasQuery ? '&' : '?');
    AppendToUrl(urlBuilder, "since=".AsSpan());
    AppendToUrl(urlBuilder, Uri.EscapeDataString(since!.ToString()!).AsSpan());
    hasQuery = true;
}
```

Because `UserId.ToString()` is the ULID string, the resulting URL is
`/users/01J9XGE7T0H8YQ7K3M4DFB1HJG?since=01J9XGE5RM7AY2WTC0K7P0G42S`.

## 4. Round-trip with WireMock.Net

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using ZeroAlloc.Rest.SystemTextJson;

[Fact]
public async Task GetUser_TypedIdRoute_RoundTrips()
{
    using var server = WireMockServer.Start();
    var services = new ServiceCollection();
    services.AddIUserApi(o =>
    {
        o.BaseAddress = new Uri(server.Url!);
        o.UseSerializer<SystemTextJsonSerializer>();
    });
    using var sp = services.BuildServiceProvider();
    var api = sp.GetRequiredService<IUserApi>();

    var id = UserId.New();
    server.Given(Request.Create().WithPath($"/users/{id}").UsingGet())
          .RespondWith(Response.Create()
              .WithStatusCode(200)
              .WithHeader("Content-Type", "application/json")
              .WithBody($"{{\"id\":\"{id}\",\"name\":\"Ada\"}}"));

    var user = await api.GetUserAsync(id);

    Assert.Equal(id, user.Id);
}
```

A working example lives at
`tests/ZeroAlloc.Rest.Integration.Tests/TypedIdUserApiTests.cs`.

## 5. Choosing an ID strategy

`[TypedId]` accepts an `IdStrategy` argument; pick the one that matches the
URL story you want:

| Strategy | URL form | When to use |
|---|---|---|
| `IdStrategy.Ulid` (default) | 26-char Crockford-base32 | New surfaces; lex-sortable |
| `IdStrategy.Uuid7` | 36-char hyphenated UUID | Backwards-compat with existing `Guid` URLs |
| `IdStrategy.Snowflake` | Decimal `long` | Numeric IDs; sortable; small |
| `IdStrategy.Sequential` | Decimal `long` | Tests, deterministic IDs |

```csharp
[TypedId(IdStrategy.Uuid7)]
public readonly partial record struct OrderId;
```

The Rest generator does not care which strategy you pick — it only calls
`ToString()` and `Uri.EscapeDataString` on the result.

## 6. Server side

Because `[TypedId]` generates `IParsable<TId>`, ASP.NET Core minimal APIs
bind route and query parameters with no extra wiring:

```csharp
app.MapGet("/users/{id}", (UserId id) => Results.Ok(new UserDto(id, "Ada")));
app.MapGet("/users",      ([FromQuery] UserId? since) => Results.Ok(...));
```

For EF Core persistence, add `ZeroAlloc.ValueObjects.EfCore` and register
the value converter once per `DbContext`:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder b)
    => b.Properties<UserId>().HaveConversion<TypedIdValueConverter<UserId, Guid>>();
```

## See also

- [Parameters](../parameters.md) — full reference for route, query, header, and body annotations.
- [`ZeroAlloc.ValueObjects`](https://www.nuget.org/packages/ZeroAlloc.ValueObjects) — `[TypedId]` source generator and runtime.
- [`ZeroAlloc.ValueObjects.EfCore`](https://www.nuget.org/packages/ZeroAlloc.ValueObjects.EfCore) — `TypedIdValueConverter<TId, TBacking>` for EF Core mapping.
