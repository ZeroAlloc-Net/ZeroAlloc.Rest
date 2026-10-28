# Anthropic client

`ZeroAlloc.Rest.Anthropic` is a typed, Native AOT-compatible client for the [Anthropic Messages API](https://docs.anthropic.com/en/api/messages), built on ZeroAlloc.Rest. It creates a message or streams one, with tools, prompt caching and the usage split.

It does not depend on Microsoft.Extensions.AI. Mapping its types to `IChatClient` is the job of an adapter, such as Thalos.NET.Anthropic.

## Install

```sh
dotnet add package ZeroAlloc.Rest.Anthropic
```

The package brings ZeroAlloc.Rest, the System.Text.Json serializer, `ZeroAlloc.Rest.DependencyInjection` and `ZeroAlloc.Rest.Resilience`. The source generator for the typed client ships inside it.

## Create a client

By hand, over an `HttpClient` you own:

```csharp
using ZeroAlloc.Rest.Anthropic;

var http = new HttpClient();
var client = AnthropicClient.Create(http, new AnthropicClientOptions { ApiKey = key });
```

`Create` sets the base address when the client has none, the `x-api-key` and `anthropic-version` headers and `HttpClient.Timeout`, so call it before the client sends anything. Nothing reads the key from the environment.

With dependency injection:

```csharp
services.AddAnthropicClient(o => o.ApiKey = configuration["Anthropic:ApiKey"]);
```

`AddAnthropicClient` returns the `IHttpClientBuilder`, so you can add handlers to it. A resilience handler added there brings its own timeouts, which apply on top of `Timeout`. `AnthropicClient` is registered transient.

| Option | Default |
|---|---|
| `ApiKey` | none: no key header is sent, so a handler can authenticate instead |
| `BaseAddress` | `https://api.anthropic.com/` |
| `AnthropicVersion` | `2023-06-01` |
| `Timeout` | 10 minutes |
| `MaxRetries` | 2, so three attempts in all |

## Create a message

```csharp
var request = new MessageRequest
{
    Model = "claude-sonnet-5-5",
    MaxTokens = 1024,
    System = "You answer in one sentence.",
    Messages = [new MessageParam(MessageRole.User, [new TextBlockParam("Why is the sky blue?")])],
};

var result = await client.CreateMessageAsync(request, ct);
if (result.IsSuccess)
    Console.WriteLine(((TextBlock)result.Value.Content[0]).Text);
else
    Console.WriteLine($"{result.Error.Type}: {result.Error.Message}");
```

`CreateMessageAsync` returns `Result<MessageResponse, AnthropicApiError>` from [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results). `MessageResponse` carries the content blocks, `StopReason` (the constants are in `StopReasons`) and `Usage`.

## Stream a message

```csharp
await foreach (var evt in client.StreamMessageAsync(request, ct))
{
    if (evt is ContentBlockDeltaEvent { Delta: TextDelta text })
        Console.Write(text.Text);
}
```

`StreamMessageAsync` sets `stream: true` and yields the events in order: `MessageStartEvent`, `ContentBlockStartEvent`, `ContentBlockDeltaEvent` with a `TextDelta` or an `InputJsonDelta`, `ContentBlockStopEvent`, `MessageDeltaEvent`, `MessageStopEvent`, plus `PingEvent`. It uses the [server-sent events](advanced.md#server-sent-events) support of ZeroAlloc.Rest.

## Tools

```csharp
using System.Text.Json;

using var doc = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""");
var tool = new ToolDefinition("get_weather", "Current weather for a city.", doc.RootElement.Clone(), null);

var request = new MessageRequest { Model = model, MaxTokens = 1024, Messages = history, Tools = [tool] };
```

When the model stops with `StopReasons.ToolUse`, its `ToolUseBlock` holds the `Id`, `Name` and `Input`. Send the turn back as a `ToolUseBlockParam` in an assistant message, then answer in a user message with a `ToolResultBlockParam(toolUseId, content, isError)`. While streaming, `InputJsonDelta` carries the input as partial JSON.

## Prompt caching

`CacheControl` marks a cache breakpoint. `CacheControl.FiveMinutes` and `CacheControl.OneHour` are ready made, and `new CacheControl(null)` takes the API default. It goes on a `TextBlockParam` in the system prompt, on any content block param of a message, and on a `ToolDefinition`:

```csharp
System = new TextBlockParam[] { new("Long, stable instructions.", CacheControl.OneHour) },
```

`Usage` reports the split: `InputTokens`, `OutputTokens`, `CacheCreationInputTokens`, `CacheReadInputTokens` and `CacheCreation`, the writes by time to live.

## Retries

Retrying is built in and uses [ZeroAlloc.Rest.Resilience](resilience.md).

- A 429, a 529 and any 5xx are retried, up to `MaxRetries` times.
- The `retry-after` header is honoured in seconds. The wait is capped at 60 seconds. Without the header the backoff starts at 500 ms, with jitter.
- A stream is retried only before its first event. Once an event has been yielded, nothing is retried.
- Transport failures, timeouts and 408 and 409 responses are not retried. This differs on purpose from the official Anthropic SDKs, which retry them.

Set `MaxRetries` to 0 to turn retries off.

## Errors

`CreateMessageAsync` does not throw for a failed call. It returns an `AnthropicApiError` with `Type`, `Message`, `Status`, `RequestId` and `RetryAfter`. `Type` is the API's own error type, such as `rate_limit_error` or `overloaded_error`. For failures the API did not describe it is `http_<status>` for an error body that is not an Anthropic error, `timeout`, `connection_error`, `invalid_response` for a success body that could not be read, or `stream_incomplete`.

`StreamMessageAsync` throws `AnthropicApiException`, whose `Error` is the same record. It throws:

- on a failure before the first event, once the retries have run out;
- on an `error` event in the stream;
- with type `stream_incomplete` when the stream ends without `message_stop`.

Cancelling throws `OperationCanceledException` in both methods. The exception text holds nothing from the request, so the key cannot leak into a log.

## Your own serializer

`AnthropicJson.Context` is the source-generated `JsonSerializerContext` of every request and response type. To build your own typed client over the same types, use it with the System.Text.Json serializer of ZeroAlloc.Rest:

```csharp
using ZeroAlloc.Rest.SystemTextJson;

var serializer = new SystemTextJsonSerializer(AnthropicJson.Context);
```

## Scope

Covered: messages create and stream, text, tools, tool use and results, `cache_control` with a time to live on the system prompt, messages and tools, usage with the cache split, stop reasons, error mapping and retries.

Not covered: extended thinking, `tool_choice`, images, documents, token counting, the models endpoint and batches.
