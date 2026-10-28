---
id: blazor-webassembly
title: Blazor WebAssembly
slug: /blazor-webassembly
sidebar_position: 6.5
description: Using a generated client in a Blazor WebAssembly app, how response streaming behaves in the browser, and the IHttpClientFactory handler scope trap.
---

# Blazor WebAssembly

A generated client is plain `HttpClient` code with no reflection, so it runs in a Blazor
WebAssembly app like in any other .NET app. In the browser, `HttpClient` sends through the
Fetch API. This page covers the two places where that differs from a server-side app: response
streaming and the lifetime of `DelegatingHandler` dependencies.

`ZeroAlloc.Rest` targets .NET 8 and .NET 10; a Blazor app on .NET 9 uses the .NET 8 build and follows
the .NET 8 and 9 rules below. Registration is the
same as everywhere else; see [Dependency Injection](dependency-injection.md):

```csharp
// Program.cs of the Blazor WebAssembly project
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
    options.UseSerializer<SystemTextJsonSerializer>();
});

await builder.Build().RunAsync();
```

## Response streaming

Browser `fetch` used to buffer a whole response before handing it to .NET. Whether a response
reaches your code as it arrives depends on the target framework:

| | .NET 8 and 9 | .NET 10 |
|---|---|---|
| Response streaming in the browser | Off. Opt in per request. | On by default. |

Two things on the client side matter, and the generated code already does the first:

- **The call must send with `HttpCompletionOption.ResponseHeadersRead`.** Otherwise `HttpClient`
  waits for the whole body before it returns. A method with `[EventStream]`, a method that returns
  `Stream`, and any method with [`StreamResponses = true`](advanced.md#streaming-responses) sends
  this way. A plain method without those does not, so it buffers whatever the browser does.
- **The browser must stream the response.** This is the part that depends on the framework version.

### .NET 10

Nothing to do. An `[EventStream]` method that returns `IAsyncEnumerable<T>` yields each event as
the server writes it, and a `Stream` response is readable while it downloads:

```csharp
[ZeroAllocRestClient]
public interface IFeedApi
{
    [Get("/feed")]
    [EventStream]
    IAsyncEnumerable<ServerSentEvent<FeedItem>> FollowAsync(CancellationToken ct = default);
}

await foreach (var item in feed.FollowAsync(ct))
    Items.Add(item.Data);
```

Do not add opt-ins "to be safe" on .NET 10; they are not needed.

The response content stream is then a `BrowserHttpReadStream`, which does not support synchronous
reads such as `Stream.Read(Span<byte>)`. The generated client and the built-in serializers read
asynchronously. A [custom serializer](serialization.md#custom-serializer) that reads
synchronously from the body stream must either read asynchronously or opt out, as below.

### Opting out

To turn streaming off for the whole app, set the MSBuild property in the Blazor project:

```xml
<PropertyGroup>
  <WasmEnableStreamingResponse>false</WasmEnableStreamingResponse>
</PropertyGroup>
```

Setting the `DOTNET_WASM_ENABLE_STREAMING_RESPONSE` environment variable to `false` or `0` does
the same. To turn it off for one request, call `SetBrowserResponseStreamingEnabled(false)` on the
`HttpRequestMessage`. The generated client builds its own request, so do this from a
`DelegatingHandler` on the client's `HttpClient`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Http;

public sealed class NoStreamingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == "/legacy/report")
            request.SetBrowserResponseStreamingEnabled(false);

        return base.SendAsync(request, cancellationToken);
    }
}

builder.Services.AddTransient<NoStreamingHandler>();
builder.Services.AddIUserApi(options => options.BaseAddress = new Uri("https://api.example.com"))
    .AddHttpMessageHandler<NoStreamingHandler>();
```

### .NET 8 and 9

Streaming is off unless a request opts in. Set `SetBrowserResponseStreamingEnabled(true)` from a
handler, the same way as above, for the requests that must stream:

```csharp
public sealed class StreamingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserResponseStreamingEnabled(true);
        return base.SendAsync(request, cancellationToken);
    }
}
```

Without it, an `[EventStream]` method still works, but the browser delivers the response in one
piece, so every event arrives at once when the response ends.

## Handler scope with IHttpClientFactory

`Add{I}` registers the client through `IHttpClientFactory`, which builds each `DelegatingHandler`
in its own DI scope. That scope is separate from the app's: a **scoped** service injected into a
handler is not the instance your components get. In Blazor WebAssembly there are no per-request
scopes and scoped services behave like singletons in the app's root scope, so the mismatch is easy
to miss: the component sets a token on its instance, and the handler reads an empty one from its
own.

```csharp
// The handler gets its own instance of TokenStore, not the one the components see.
builder.Services.AddScoped<TokenStore>();
builder.Services.AddTransient<BearerHandler>();
```

Register state a handler shares with the rest of the app as a **singleton**:

```csharp
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddTransient<BearerHandler>();

builder.Services.AddIUserApi(options => options.BaseAddress = new Uri("https://api.example.com"))
    .AddHttpMessageHandler<BearerHandler>();

public sealed class BearerHandler(TokenStore tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (tokens.AccessToken is { } token)
            request.Headers.Authorization = new("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
```

If the state has to stay scoped, for example because it is also used server-side by a Blazor Web
App, read it through a singleton accessor that the scoped owner updates.

Handler scopes live as long as the handler, which can be much longer than a component. Do not cache
scope-related data inside a handler.

## Trimming and Native AOT

Nothing here differs from [Native AOT](native-aot.md): the generated client has no reflection and
carries no trim or AOT suppressions, so what the trimmer sees is what your serializer does. Use a
source-generated serializer such as `SystemTextJsonSerializer` with a `JsonSerializerContext`.

## Sources

- [Call a web API from ASP.NET Core Blazor](https://learn.microsoft.com/aspnet/core/blazor/call-web-api): response streaming, opt-out and the .NET 8 and 9 opt-in, and `DelegatingHandler` scope.
- [ASP.NET Core Blazor dependency injection](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/dependency-injection): client-side scoped services behave like singletons.
- [IHttpClientFactory](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#message-handler-scopes-in-ihttpclientfactory): message handler scopes.
