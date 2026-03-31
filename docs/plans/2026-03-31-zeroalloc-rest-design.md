# ZeroAlloc.Rest — Design Document

**Date:** 2026-03-31

## Goal

A Refit-style REST client library for .NET that is fully Native AOT compatible, zero-allocation in hot paths, and driven entirely by source generation. Targets the gap left by Refit's reflection-based approach, which is fundamentally incompatible with Native AOT.

---

## Project Structure

```
ZeroAlloc.Rest/                 ← core: attributes, IRestSerializer, ApiResponse<T>
ZeroAlloc.Rest.Generator/       ← Roslyn incremental source generator
ZeroAlloc.Rest.SystemTextJson/  ← JSON serializer adapter (default)
ZeroAlloc.Rest.MessagePack/     ← MessagePack serializer adapter
ZeroAlloc.Rest.MemoryPack/      ← MemoryPack serializer adapter
ZeroAlloc.Rest.Tools/           ← dotnet CLI tool + MSBuild task (OpenAPI codegen)
```

---

## Pipeline

```
OpenAPI spec (URL or local file)
        ↓  ZeroAlloc.Rest.Tools
  IUserApi interface (.cs)          ← checked into source control
        ↓  ZeroAlloc.Rest.Generator (Roslyn incremental)
  UserApiClient class (generated)   ← AOT-safe, no reflection
```

---

## Interface Definition

Interfaces are decorated with `[ZeroAllocRestClient]` to trigger source generation. Attribute style is intentionally Refit-compatible.

```csharp
[ZeroAllocRestClient]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<User> GetUserAsync(int id, CancellationToken ct = default);

    [Post("/users")]
    Task<User> CreateUserAsync([Body] CreateUserRequest body, CancellationToken ct = default);

    [Get("/users")]
    Task<List<User>> SearchUsersAsync([Query] string name, [Query] int page = 1, CancellationToken ct = default);

    [Delete("/users/{id}")]
    Task DeleteUserAsync(int id, CancellationToken ct = default);

    [Post("/users/{id}/avatar")]
    [Serializer(typeof(MessagePackSerializer))]
    Task UploadAvatarAsync(int id, [Body] AvatarData data, CancellationToken ct = default);
}
```

### Attributes

| Attribute | Target | Purpose |
|---|---|---|
| `[ZeroAllocRestClient]` | Interface | Triggers source generation |
| `[Get(route)]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]` | Method | HTTP method + route template |
| `[Body]` | Parameter | Request body — serialized using active serializer |
| `[Query]` | Parameter | Appended to URL as query string parameter |
| `[Header("Name")]` | Parameter or Method | Dynamic (parameter) or fixed (method) HTTP header |
| `[Serializer(typeof(T))]` | Interface or Method | Override default serializer for this scope |

### Return Types

| Type | Behavior |
|---|---|
| `Task<T>` | Deserialize response body to `T`, throw on non-2xx |
| `Task<ApiResponse<T>>` | Deserialize body, expose status code + headers, no throw |
| `Task` | No response body expected (e.g. DELETE) |

---

## Serializer Abstraction

```csharp
public interface IRestSerializer
{
    string ContentType { get; }
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct);
    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct);
}
```

`ValueTask` keeps allocations low on the synchronous fast path. All three bundled adapters are fully AOT-compatible via their respective source generators:

| Package | Format | AOT mechanism |
|---|---|---|
| `ZeroAlloc.Rest.SystemTextJson` | JSON | `JsonSerializerContext` |
| `ZeroAlloc.Rest.MessagePack` | MessagePack (binary) | `MessagePack.SourceGenerator` |
| `ZeroAlloc.Rest.MemoryPack` | MemoryPack (binary) | MemoryPack source generator |

Serializer selection is **per-client** (default, registered via DI) with optional **per-method override** via `[Serializer(...)]`. The source generator resolves this at compile time — no runtime dispatch.

---

## DI Registration

```csharp
services.AddZeroAllocClient<IUserApi>(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer<SystemTextJsonSerializer>();
})
.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));
```

- Built on `IHttpClientFactory` typed client pattern — correct `HttpClient` lifetime, connection pooling
- `UseSerializer<T>()` sets the default `Content-Type` and `Accept` headers automatically
- The source generator emits a `AddIUserApi(this IServiceCollection)` extension method to wire up the generated implementation

---

## Source Generator

Uses Roslyn **incremental generators** for fast incremental builds. For each `[ZeroAllocRestClient]` interface, emits a `partial class`:

```csharp
// Generated: IUserApi.g.cs
public partial class UserApiClient : IUserApi
{
    private readonly HttpClient _httpClient;
    private readonly IRestSerializer _serializer;

    public UserApiClient(HttpClient httpClient, IRestSerializer serializer)
    {
        _httpClient = httpClient;
        _serializer = serializer;
    }

    public async Task<User> GetUserAsync(int id, CancellationToken ct = default)
    {
        var url = string.Create(null, stackalloc char[64], $"/users/{id}");
        using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await _serializer.DeserializeAsync<User>(
            await response.Content.ReadAsStreamAsync(ct), ct);
    }
}
```

### Zero-Allocation Techniques

- `string.Create` with `stackalloc` for route interpolation — no heap string allocation for URLs
- Stream-based serialization — no intermediate `byte[]` buffers
- `ConfigureAwait(false)` throughout — no `SynchronizationContext` capture
- Serializer resolved at compile time — no runtime type lookup or boxing

---

## OpenAPI Code Generator (ZeroAlloc.Rest.Tools)

Generates the `[ZeroAllocRestClient]` interface from an OpenAPI 3.x spec. Output is a plain `.cs` file that is checked into source control, inspectable, and manually editable.

### MSBuild (developer convenience)

```xml
<ItemGroup>
  <ZeroAllocApiSpec Include="https://api.example.com/swagger.json"
                    Namespace="MyApp.ApiClients"
                    Output="Generated/IUserApi.cs" />
  <ZeroAllocApiSpec Include="specs/local-api.yaml"
                    Namespace="MyApp.LocalClient"
                    Output="Generated/ILocalApi.cs" />
</ItemGroup>
```

Regenerates on build when the spec changes (MSBuild up-to-date check via file hash for local files, configurable polling for URLs).

### CLI (CI/scripting)

```bash
dotnet tool install -g ZeroAlloc.Rest.Tools

dotnet zeroalloc generate \
  --spec https://api.example.com/swagger.json \
  --namespace MyApp.ApiClients \
  --output Generated/IUserApi.cs

dotnet zeroalloc generate \
  --spec specs/local-api.yaml \
  --namespace MyApp.LocalClient \
  --output Generated/ILocalApi.cs
```

Both URL and local file are supported in both invocation modes.

---

## Non-Goals

- Server-side hosting / route registration (out of scope for v1)
- Runtime content negotiation (by design — serializer is compile-time)
- Protobuf support (protobuf-net AOT story not mature enough; community contribution welcome later)
- Shared client/server contracts (potential v2 feature)
