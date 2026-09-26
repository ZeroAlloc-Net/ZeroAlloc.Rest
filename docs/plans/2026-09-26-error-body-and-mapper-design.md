# Error body and error mapper: design

**Issues:** #298 (error response body on `HttpError`) and #300 (`Result<T, TError>` with a user-defined error type). **Release:** ZeroAlloc.Rest 2.1.0, a minor. **Status:** approved 2026-09-26.

This is sub-project A of the Result-integration cluster. Sub-project B, in ZeroAlloc.Resilience (#142, #143), adds Result-aware retry with a `RetryWhen` predicate and a `DelayHint`. Sub-project C, in ZeroAlloc.Telemetry (#142), adds metrics read from the result. B's `DelayHint` builds on the `GetRetryAfter` helper this spec adds.

## Problem

- **The body is dropped.** On the non-success branch of a `Result<T, HttpError>` method, the generated client builds `HttpError` from the status code and `response.Headers` only. The body is never read, so an API's JSON error detail can't be recovered. For example, a 422 response that says which field failed validation is lost. Content headers such as `Content-Type` are dropped too.
- **The error type is fixed.** `ModelExtractor` recognises `Result<T, E>` but discards `E`, and `ClientEmitter.ResultTypeName` hard-codes `HttpError`. Declaring any other error type produces generated code that doesn't compile (CS0029). A client SDK that wants to return its own domain error has to wrap every method by hand.

## Current state, 2.0.1

- `HttpError` is a `sealed record (HttpStatusCode StatusCode, IReadOnlyDictionary<string, IReadOnlyList<string>> Headers, string? Message = null)` with init-only `Kind` (an `HttpErrorKind`: Status, Transport, Timeout or Deserialization) and `Exception`.
- Every `HttpError` is built by one generated helper, `__CreateHttpError(kind, response?, exception?)`, which is emitted only when some method returns a Result.
  - **Status:** a non-success status gives `(Status, response, null)`.
  - **Timeout:** an `OperationCanceledException` not caused by the caller's token gives `(Timeout, null, ex)`.
  - **Transport:** an `HttpRequestException` gives `(Transport, null, ex)`.
  - **Deserialization:** a serializer failure on a 2xx body gives `(Deserialization, response, ex)`.
  - **Anything else:** any other exception is recorded and rethrown.
- `response` is a `using var` inside the method's `try`. Anything read from it must be read before the method returns.
- The generated constructor takes `HttpClient`, the interface serializer, and one `IRestSerializer` per method-level `[Serializer]`. `IGeneratedRestClient<TSelf>.AddSerializers` registers dependencies and `Create` resolves them. The Rest.Resilience bridge calls both.
- The only diagnostic is `ZRA001`. `PublicAPI` tracking and the api-compat check are active. The package is AOT-compatible, and CI publishes an AOT smoke sample.

## Decisions

- **What the mapper receives.** It gets the built `HttpError` with the body already read, through one synchronous method. It does not get the `HttpResponseMessage` or the exception separately. Every failure kind goes through one hook, and the mapper never touches the response, whose lifetime ends inside the generated method.
- **How it is registered.** It uses an interface-level `[ErrorMapper]` attribute that mirrors `[Serializer]`, not a runtime option. The mapper is known at compile time, so a missing mapper is a compile error. Registration is trim- and AOT-safe, and a library's mapper is isolated from the host.
- **Where the body cap lives.** The cap is a compile-time property on `[ZeroAllocRestClient]`. The generated client never sees `ZeroAllocClientOptions` at runtime, and a runtime cap would need new constructor plumbing for no gain.

## Part 1: the error body (#298)

### Public API

These are all additive. `HttpError` gains:

```csharp
public ReadOnlyMemory<byte> Body { get; init; }   // empty when not read or not present
public string? ContentType { get; init; }          // media type only, e.g. "application/problem+json"
public bool BodyTruncated { get; init; }           // true when the body exceeded the cap
```

`ZeroAllocRestClientAttribute` gains:

```csharp
public int MaxErrorBodyBytes { get; set; } = 65536;   // 0 = never read the error body
```

A new helper supports sub-project B:

```csharp
public static class HttpErrorExtensions
{
    public static TimeSpan? GetRetryAfter(this HttpError error, TimeProvider? timeProvider = null);
}
```

`GetRetryAfter` reads the `Retry-After` header in both delta-seconds and HTTP-date form. It returns null when the header is absent or invalid. A date in the past gives `TimeSpan.Zero`, and a negative delta-seconds value is invalid, so it returns null. `timeProvider` defaults to `TimeProvider.System` and is used only for the HTTP-date form.

### Behaviour

- **Which failures carry a body.** Only a `Status` failure reads the body, inside `__CreateHttpError`, before `response` is disposed. `Transport` and `Timeout` have no response. The `Deserialization` stream has already been consumed. All three keep an empty `Body`.
- **Cap and truncation.** The read stops at `MaxErrorBodyBytes`. If the body is longer, `Body` holds the first `MaxErrorBodyBytes` bytes and `BodyTruncated` is true, so a mapper knows not to parse a partial document.
- **Allocation.** A known `Content-Length` at or below the cap sizes one exact buffer. Otherwise the body is read into a pooled buffer up to the cap and copied once into an exact-size array. An empty body stays `ReadOnlyMemory<byte>.Empty` and allocates nothing.
- **A read that fails.** If reading the body throws, for example because the connection drops mid-body, the result is still a `Status` error. `Body` is empty and `BodyTruncated` is false. The status is the real failure, and a broken body must not hide it. The caller's cancellation is still honoured: if the caller's token is cancelled during the read, the method throws as it does today.
- **With the cap set to 0.** The generator emits no body-reading code at all.
- **Content type.** `ContentType` is `response.Content.Headers.ContentType?.MediaType`, set for `Status` and `Deserialization`.
- **Headers fix.** `Headers` now also contains the content headers, merged into the same case-insensitive dictionary. Today only `response.Headers` is copied, which silently drops `Content-Type`, `Content-Length` and the rest. This ships as a `fix:`.

### Tests

**Stub-handler runtime tests:**
- A 422 with a JSON body.
- An empty body.
- A body over the cap: truncated, with the flag set.
- No `Content-Length` header.
- A body read that throws.
- Caller cancellation during the body read.
- Content headers present in `Headers`.
- `GetRetryAfter`: seconds, an HTTP-date, a date in the past, invalid values, and an absent header.

**Generator tests:**
- The body read is emitted only on the `Status` path.
- `MaxErrorBodyBytes = 0` emits no body read.

## Part 2: the error mapper (#300)

### Public API

```csharp
public interface IHttpErrorMapper<TError>
{
    TError Map(HttpError error);
}

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
public sealed class ErrorMapperAttribute(Type mapperType) : Attribute
{
    public Type MapperType { get; } = mapperType;
}
```

Usage:

```csharp
[ZeroAllocRestClient]
[ErrorMapper(typeof(JevErrorMapper))]
internal interface IJevApi
{
    [Post("v1/systemone")]
    ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
}
```

### Generator

- **Carry `E` through.** `ModelExtractor` records `E` on the method model as `ErrorTypeName`. `ResultTypeName` emits `Result<T, E>`. A method returning `Result<T, HttpError>` generates exactly the code it does today.
- **Pick a mapper.** For each distinct `E ≠ HttpError`, the generator selects the `[ErrorMapper]` type that implements `IHttpErrorMapper<E>`.
- **Constructor.** The generated constructor gains one parameter per mapped `E`, of type `IHttpErrorMapper<E>`.
- **Failure sites.** Every failure site for that method becomes `Failure(mapper.Map(__CreateHttpError(kind, …)))`. That covers Status, Timeout, Transport and Deserialization.
- **Registration.** The generated `AddSerializers` calls `TryAddSingleton<TMapper>()`. `Create` resolves the concrete `TMapper`, not `IHttpErrorMapper<E>`, so a host registration of `IHttpErrorMapper<E>` can never replace a library's mapper.
  - `IGeneratedRestClient` is unchanged, so this is not breaking.
  - The Rest.Resilience bridge gets the mapper through the existing `AddSerializers` and `Create`.
- **A mapper that throws.** Its exception propagates to the caller unchanged and is recorded as a failure on the span. The generated code never passes a mapper's own exception back into a mapper, so a throwing mapper can't recurse. The docs say a mapper should be total.

### Diagnostics

All three are errors. Each is reported at the offending attribute or method, never at `Location.None`.

- **ZRA002:** a method returns `Result<T, E>` with `E ≠ HttpError`, and no `[ErrorMapper]` on the interface implements `IHttpErrorMapper<E>`.
- **ZRA003:** an `[ErrorMapper]` type implements no `IHttpErrorMapper<>` interface, or has no accessible public constructor.
- **ZRA004:** two `[ErrorMapper]` attributes on one interface map the same `E`.

A mapper whose `E` no method uses is allowed. It is registered and simply never called.

### Tests

**Generator:**
- The mapper constructor parameter, and the `Map` call on all four failure kinds.
- `HttpError` methods unchanged.
- Two error types on one interface.
- ZRA002, ZRA003 and ZRA004, each with its location.
- The generated code compiles for an internal interface with an internal mapper and error type.

**Runtime, with a stub handler:**
- A 422 body mapped to a domain error.
- A timeout, a transport error and a deserialization failure, each mapped.
- A mapper that throws propagates, and is not re-mapped.

**DI:**
- A host registration of `IHttpErrorMapper<E>` does not replace the library mapper.
- The Rest.Resilience bridge resolves the mapper.

**AOT:** the smoke sample gains a client with a mapped error type.

## Docs

- `docs/advanced.md`, error handling:
  - `Body`, `ContentType`, `BodyTruncated` and `MaxErrorBodyBytes`.
  - The content-headers fix.
  - `GetRetryAfter`.
  - A worked `[ErrorMapper]` example with a domain error type.
- A section for each of ZRA002, ZRA003 and ZRA004, next to ZRA001.
- `docs/resilience.md`: mapped errors flow through the Rest.Resilience bridge.

## Delivery

- **Two sequential PRs, not stacked.** A stacked PR merged into its base loses its changelog entry.
  1. **#298:** body, content type, cap, headers fix, `GetRetryAfter`, and docs. Commits: `feat:` for the body and helper, and `fix:` for the headers.
  2. **#300:** mapper, attribute and diagnostics. It is opened after PR 1 merges.
- **Files.** New public API goes in `PublicAPI.Unshipped.txt`, and ZRA002 to ZRA004 go in `AnalyzerReleases.Unshipped.md`. The api-compat check must pass without new suppressions.
- **Commit bodies.** Lines are at most 100 characters, with no nested parentheses.
- **After each merge,** comment on the issue so downstream consumers know what shipped.
