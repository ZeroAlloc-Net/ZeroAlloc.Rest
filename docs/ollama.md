# Ollama client

`ZeroAlloc.Rest.Ollama` is a small, Native AOT-compatible client for the embed endpoint of an [Ollama](https://ollama.com) server, built on ZeroAlloc.Rest. It covers `POST /api/embed` and nothing else.

It does not depend on Microsoft.Extensions.AI. Mapping its types to `IEmbeddingGenerator` is the job of an adapter, such as Thalos.NET.Ollama.

## Install

```sh
dotnet add package ZeroAlloc.Rest.Ollama
```

## Create a client

By hand. The `HttpClient` needs its base address set:

```csharp
using ZeroAlloc.Rest.Ollama;

var http = new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };
var client = OllamaClient.Create(http);
```

With dependency injection:

```csharp
services.AddOllamaClient(o => o.BaseAddress = new Uri("http://ollama:11434/"));
```

`BaseAddress` defaults to `http://localhost:11434/`. `AddOllamaClient` returns the `IHttpClientBuilder`, so you can add handlers to it, such as a resilience handler. `OllamaClient` is registered transient.

## Embed

```csharp
var result = await client.EmbedAsync(
    new EmbedRequest("nomic-embed-text", ["first text", "second text"]), ct);

if (result.IsSuccess)
    Console.WriteLine($"{result.Value.Embeddings.Count} vectors of {result.Value.Embeddings[0].Length}");
else
    Console.WriteLine($"{result.Error.Status}: {result.Error.Message}");
```

`EmbedAsync` returns `Result<EmbedResponse, OllamaApiError>` from [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results). `EmbedResponse.Embeddings` holds one vector per input, in input order. `Model` names the model that answered; the durations and `PromptEvalCount` are set when Ollama reports them.

A 200 response without `embeddings` or `model` is a deserialization failure: you get an `OllamaApiError`, not a response with missing members.

## Retries and errors

This client does not retry. Add a resilience handler to the builder `AddOllamaClient` returns when you want that.

`OllamaApiError` has a `Status` and a `Message`. For an HTTP error, `Message` is the `error` text of Ollama's body, or a note that Ollama answered without one. A timeout has status 0. A transport failure or a body that could not be read carries the response status when there is one. Cancelling throws `OperationCanceledException`.

## Your own serializer

`OllamaJson.Context` is the source-generated `JsonSerializerContext` of the request and response types. Use `new SystemTextJsonSerializer(OllamaJson.Context)` with your own typed client.

## Scope

Covered: `/api/embed`. Not covered: chat, generate, model management and every other Ollama endpoint.
