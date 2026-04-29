# ZeroAlloc.Rest

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Rest.svg)](https://www.nuget.org/packages/ZeroAlloc.Rest)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

**ZeroAlloc.Rest** is a source-generated, Native AOT-compatible REST client for .NET 10+. Define your HTTP API as a C# interface — the Roslyn generator emits a fully type-safe, zero-reflection implementation at compile time. No runtime code generation, no IL emit, no allocations beyond the HTTP layer itself.

## Install

```sh
dotnet add package ZeroAlloc.Rest
dotnet add package ZeroAlloc.Rest.Generator
dotnet add package ZeroAlloc.Rest.SystemTextJson
```

Or via `<PackageReference>`:

```xml
<PackageReference Include="ZeroAlloc.Rest" Version="x.y.z" />
<PackageReference Include="ZeroAlloc.Rest.Generator"
                  Version="x.y.z"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
<PackageReference Include="ZeroAlloc.Rest.SystemTextJson" Version="x.y.z" />
```

### Optional: resilience policies

To add retry, timeout, and circuit-breaker behaviour to a client, install the bridge package alongside [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience):

```sh
dotnet add package ZeroAlloc.Rest.Resilience
dotnet add package ZeroAlloc.Resilience
dotnet add package ZeroAlloc.Resilience.Generator  # analyzer reference
```

## Quick Start

**1. Define your API interface:**

```csharp
using ZeroAlloc.Rest.Attributes;

[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Get("/users")]
    Task<List<UserDto>> ListUsersAsync([Query] string? name = null, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest request, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}
```

**2. Register in DI:**

```csharp
builder.Services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer<SystemTextJsonSerializer>();
});
```

**3. Inject and use:**

```csharp
public class UserService(IUserApi api)
{
    public Task<UserDto> GetAsync(int id) => api.GetUserAsync(id);
}
```

The generator produces `UserApiClient` — a sealed class implementing `IUserApi` — at compile time. No reflection, no proxies, no `DynamicMethod`.

## Performance

Measured on .NET 10.0.4, Windows 11, X64. In-memory handler; no real network I/O.
See [docs/benchmarks.md](docs/benchmarks.md) for methodology and full results.

| Method | Mean | vs Refit | Allocated |
|---|---:|---:|---:|
| Raw HttpClient (GET baseline) | 1,648 ns | — | 1.38 KB |
| **ZeroAlloc.Rest GET** | 1,933 ns | **3.2× faster** | 1.74 KB |
| Refit GET | 6,123 ns | 1× | 3.03 KB |
| **ZeroAlloc.Rest QueryParam** | 2,474 ns | **5.5× faster** | 1.85 KB |
| Refit QueryParam | 13,509 ns | 1× | 3.67 KB |

## Features

- **Source-generated** — zero runtime reflection; compile-time type safety
- **Native AOT compatible** — no `DynamicMethod`, no IL emit, no `Type.GetType`
- **Per-method serializer override** — `[Serializer(typeof(MySerializer))]` for mixed protocols
- **Path, query, body, and header parameters** — `{id}`, `[Query]`, `[Body]`, `[Header("X-Api-Key")]`
- **`Result<T, HttpError>`** — typed success/error returns via `ZeroAlloc.Results`; no exception-throwing on 4xx/5xx
- **OpenAPI code generation** — `OpenApiInterfaceGenerator` API + MSBuild `<ZeroAllocApiSpec>` task
- **Pluggable serializers** — System.Text.Json, MemoryPack, MessagePack, or bring your own
- **IHttpClientFactory integration** — `AddI{Interface}` generated extension method
- **Resilience bridge** — `ZeroAlloc.Rest.Resilience` wraps any client with `[Retry]`, `[Timeout]`, `[CircuitBreaker]`, and `[RateLimit]` via `AddRestResilience<,,>()`

## Telemetry

Every generated client emits OpenTelemetry-compatible signals out of the box, with no extra dependency beyond the BCL `System.Diagnostics` primitives.

**Tracing.** An `ActivitySource("ZeroAlloc.Rest")` produces one span per call, named `Interface.Method`. Tags: `http.method`, `http.status_code`, `server.address`, `rest.method`. Exceptions set `ActivityStatusCode.Error` with the message.

**Metrics.** The generator emits `rest.requests_total` (counter) and `rest.request_duration_ms` (histogram) under the `ZeroAlloc.Rest` Meter. Tags: `http.method`, `http.status_code`, `server.address`, `rest.method` (the generated `Interface.Method` identifier).

The counter is incremented only on the success path. On exception, the histogram records the elapsed duration but the counter is left untouched — this lets operators distinguish failed attempts via histogram count vs. counter delta. The exception-path histogram carries only `http.method` and `rest.method`, since `http.status_code` and `server.address` may not be known.

**Subscription:**
```csharp
services.AddOpenTelemetry()
        .WithTracing(t => t.AddSource("ZeroAlloc.Rest"))
        .WithMetrics(m => m.AddMeter("ZeroAlloc.Rest"));
```

## Documentation

| Page | Description |
|---|---|
| [Getting Started](docs/getting-started.md) | Install, register, and make your first call |
| [Routing](docs/routing.md) | Route templates and path parameters |
| [Parameters](docs/parameters.md) | Query, body, header, and path parameters |
| [Serialization](docs/serialization.md) | Built-in serializers and custom `IRestSerializer` |
| [Dependency Injection](docs/dependency-injection.md) | Generated DI extension and `IHttpClientFactory` |
| [Native AOT](docs/native-aot.md) | AOT safety guarantees and publish configuration |
| [OpenAPI Code Generation](docs/openapi-codegen.md) | Generate interfaces from OpenAPI specs |
| [Benchmarks](docs/benchmarks.md) | Performance comparison vs Refit and raw HttpClient |
| [Testing](docs/testing.md) | Testing patterns with WireMock.Net |
| [Advanced](docs/advanced.md) | `Result<T, HttpError>`, multiple serializers, edge cases |
| [Resilience](docs/resilience.md) | Retry, timeout, circuit-breaker, and rate-limit via `ZeroAlloc.Rest.Resilience` |
| [Cookbook](docs/cookbook/) | End-to-end recipes |

## License

MIT
