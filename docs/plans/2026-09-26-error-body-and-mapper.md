# Error Body and Error Mapper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry the error response body, its media type and every response header on `HttpError` (#298), then let a `Result<T, TError>` method return a user-defined error type through an `[ErrorMapper]` (#300).

**Architecture:** Part 1 adds three init properties to `HttpError`, a runtime helper `GeneratedRestClient.ReadErrorBodyAsync` that generated clients call on the `Status` path, a compile-time cap on `[ZeroAllocRestClient]`, a merged content-header copy, and `HttpErrorExtensions.GetRetryAfter`. Part 2 carries `E` through the generator model, resolves `[ErrorMapper]` types at compile time, reports ZRA002 to ZRA004 at real locations, and emits one `IHttpErrorMapper<E>` constructor parameter per used error type, mapping every failure once, after the method's `try`. Registration goes through the existing `IGeneratedRestClient<TSelf>.AddSerializers` and `Create`, so the Rest.Resilience bridge needs no change.

**Tech Stack:** .NET 10, C# latest, Roslyn incremental source generator on netstandard2.0, ZeroAlloc.Results 1.2.3, Microsoft.Extensions.Http, xUnit 2.9.3, Basic.Reference.Assemblies.Net100, ZeroAlloc.Resilience 3.1.0, Native AOT.

**Spec:** `docs/plans/2026-09-26-error-body-and-mapper-design.md`

## Global Constraints

Copied verbatim from the spec:

- **Release:** ZeroAlloc.Rest 2.1.0, a minor.
- The package is AOT-compatible, and CI publishes an AOT smoke sample.
- A method returning `Result<T, HttpError>` generates exactly the code it does today.
- `IGeneratedRestClient` is unchanged, so this is not breaking.
- All three are errors. Each is reported at the offending attribute or method, never at `Location.None`.
- **Two sequential PRs, not stacked.** A stacked PR merged into its base loses its changelog entry.
- **Files.** New public API goes in `PublicAPI.Unshipped.txt`, and ZRA002 to ZRA004 go in `AnalyzerReleases.Unshipped.md`. The api-compat check must pass without new suppressions.
- **Commit bodies.** Lines are at most 100 characters, with no nested parentheses.
- **After each merge,** comment on the issue so downstream consumers know what shipped.

Repository rules that also bind every task:

- `TreatWarningsAsErrors` is on for every project, and RS0016/RS0017 are errors. Fix every warning with real code. Add no `#pragma warning disable`, no `NoWarn`, no `[SuppressMessage]`, and no `.editorconfig` severity change.
- Commit messages are conventional: `feat:`, `fix:`, `docs:`, `test:`. Never write `[ErrorMapper(typeof(X))]` in a commit body, since `(typeof(X))` is a nested parenthesis; write "an [ErrorMapper] naming X" instead.
- Every commit message ends with this line, and has no `Claude-Session:` trailer and no session URL:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- Integration and resilience test projects do not suppress `MA0006`. Compare strings with `string.Equals(a, b, StringComparison.Ordinal)`, never `==`.
- Run every command from the worktree root: `C:\wt\rest-errors` for Part 1, `C:\wt\rest-mapper` for Part 2.

**Full-suite command.** Every task ends with this. It mirrors CI, where the duplicate-generator tests need the local feed:

```bash
dotnet build ZeroAlloc.Rest.slnx -c Release
dotnet pack src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj -c Release -o artifacts/local
dotnet pack src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj -c Release -o artifacts/local
dotnet test ZeroAlloc.Rest.slnx --no-build -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`, then every test project reports `Passed!` with `Failed: 0`.

---

## Design decisions this plan resolves

The spec leaves these points open. Each resolution is binding for the tasks below.

1. **Where the body is read.** `__CreateHttpError` is synchronous and a body read is asynchronous. The `Status` branch therefore awaits a runtime helper, `GeneratedRestClient.ReadErrorBodyAsync`, and passes the result into `__CreateHttpError`, which gains two optional parameters, `body` and `bodyTruncated`. `__CreateHttpError` stays the one place that builds an `HttpError`.
2. **The helper is public, in the runtime library.** Generated code lives in the consumer's assembly, so the helper is a public member of the existing `[EditorBrowsable(Never)]` static class `ZeroAlloc.Rest.GeneratedRestClient`, which is already the home of generated-code support. It is listed in `PublicAPI.Unshipped.txt` and returns `ValueTask<(ReadOnlyMemory<byte> Body, bool Truncated)>`, so no new public type is needed.
3. **Which read failures give an empty body.** The spec asks that "a broken body must not hide" the status. `IOException` (including `HttpIOException`), `HttpRequestException`, `InvalidOperationException` (including `ObjectDisposedException` and "content already consumed"), and an `OperationCanceledException` the caller did not ask for all give an empty body. Any other exception is a bug and propagates, as the generated client already rethrows "anything else". If the caller's token is cancelled, any exception becomes, or stays, an `OperationCanceledException`, so caller cancellation still throws.
4. **A negative `MaxErrorBodyBytes`** behaves like 0: no read, and no body-reading code is emitted. The attribute's XML doc says so. No diagnostic is added.
5. **A known `Content-Length` above the cap** takes the pooled path, as the spec says: it reads `cap + 1` bytes at most, so truncation is detected from the bytes, not trusted from a header. For caps up to 1 MiB, the helper rents one buffer that holds the whole capped body, so the body is copied once. A larger cap starts at 1 MiB and doubles, so a huge cap never rents a huge buffer up front.
6. **Tests that `HttpClient` makes unreachable.** `HttpClient.SendAsync` with the default `ResponseContentRead` buffers the whole body before the generated code sees the response. After buffering, `Content-Length` is always known, and a body that breaks mid-transfer fails inside `SendAsync` as a `Transport` error. The spec's "no Content-Length" and "a body read that throws" cases are therefore tested against `ReadErrorBodyAsync` directly, with an unbuffered `StreamContent`. The client-level tests cover what `HttpClient` can deliver.
7. **Mapping happens once, after the method's `try`.** If `Failure(mapper.Map(...))` ran at each failure site, a mapper that throws `HttpRequestException` or `OperationCanceledException` inside the `try` would be caught by the method's own `Transport` or `Timeout` catch and mapped a second time. That breaks the spec's rule that a mapper's exception is never passed back into a mapper. So a mapped method stores the `HttpError` in a local, `__httpError`, at each of the four sites, and maps it in one guarded block after the catches. That block marks the span failed and rethrows. Methods that return `HttpError` keep their inline returns, character for character.
8. **Constructor parameters exist only for error types that methods use.** If an `[ErrorMapper]` maps an `E` that no method uses, it is registered but never called, as the spec says, and it gets no constructor parameter. A parameter would expose `IHttpErrorMapper<E>` on a public client's constructor. With an internal `E`, that fails with CS0051.
9. **ZRA003's scope.** ZRA003 fires for a type that implements no `IHttpErrorMapper<>`. It also fires for a type that is not a closed, non-abstract, non-static class with a public constructor, because `TryAddSingleton<TMapper>` needs a class and DI needs a public constructor. An invalid mapper still claims its error types, so a method using one of them does not also get ZRA002.
10. **Code emitted next to a ZRA002 or ZRA003.** A method whose `E` has no usable mapper gets a body that throws `NotSupportedException`, so the rest of the client compiles and the ZeroAlloc diagnostic is the only error the user sees.
11. **ZRA004's location.** ZRA004 is reported at the second `[ErrorMapper]` attribute to claim the error type, and its message names the first.
12. **Diagnostic locations.** A location is stored in the model as an equatable `LocationInfo`, a file path plus spans, and rebuilt with `Location.Create`. The models then hold no `SyntaxTree`.
13. **Where the diagnostics are documented.** ZRA001 has only a sentence, in `docs/parameters.md`. The plan adds a "Diagnostics" section to `docs/advanced.md` covering ZRA001 to ZRA004, and links to it from `parameters.md`.
14. **Pre-existing bugs in files this work edits.** These are fixed as their own `fix:` commits in PR 2:
    - ZRA001 is reported at `Location.None`, in `ClientEmitter.cs:189`.
    - `GetSerializerFieldName` emits an invalid field name for a generic serializer type, uses culture-sensitive `char.ToLower`, and can collide with the fixed `_httpClient` and `_serializer` fields, in `ClientEmitter.cs:519-535`.
15. **A mapper's exception and telemetry.** The request's duration is already recorded at that point. The mapper catch only sets the span status, through an emitted `__RecordMapperFailure`, so the duration is not counted twice.
16. **The release.** Both PRs are meant to ship as 2.1.0, a minor. The checkpoint says to hold the release-please PR until PR 2 merges. Merging it between the two PRs would ship Part 2 as 2.2.0. The maintainer makes that call.

---

## File Structure

**Part 1 (PR 1, #298)**

| File | Change | Responsibility |
|---|---|---|
| `src/ZeroAlloc.Rest/HttpError.cs` | Modify | `Body`, `ContentType` and `BodyTruncated` init properties; `Headers` doc |
| `src/ZeroAlloc.Rest/GeneratedRestClient.cs` | Modify | Make it `partial`; update the class summary |
| `src/ZeroAlloc.Rest/GeneratedRestClient.ErrorBody.cs` | Create | `ReadErrorBodyAsync`, the capped, pooled, failure-tolerant body read |
| `src/ZeroAlloc.Rest/HttpErrorExtensions.cs` | Create | `GetRetryAfter` |
| `src/ZeroAlloc.Rest/Attributes/ZeroAllocRestClientAttribute.cs` | Modify | `MaxErrorBodyBytes` |
| `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt` | Modify | New API lines |
| `src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs` | Modify | `MaxErrorBodyBytes` |
| `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs` | Modify | Read the cap |
| `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs` | Modify | Content-header merge, body read on the `Status` path, `ContentType` |
| `tests/ZeroAlloc.Rest.Tests/HttpErrorTests.cs` | Modify | Defaults and init of the new properties |
| `tests/ZeroAlloc.Rest.Tests/ReadErrorBodyTests.cs` | Create | Helper behaviour: exact buffer, pooled path, cap, failures, cancellation |
| `tests/ZeroAlloc.Rest.Tests/HttpErrorExtensionsTests.cs` | Create | `GetRetryAfter` |
| `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs` | Modify | Emission checks |
| `tests/ZeroAlloc.Rest.Integration.Tests/StubHandler.cs` | Create | Shared stub `HttpMessageHandler` |
| `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorBodyTests.cs` | Create | Generated-client behaviour through a stub handler |
| `samples/ZeroAlloc.Rest.AotSmoke/UnprocessableHandler.cs` | Create | Answers 422 with a problem+json body |
| `samples/ZeroAlloc.Rest.AotSmoke/Program.cs` | Modify | Checks the body read under ILC |
| `docs/advanced.md` | Modify | Headers, body, cap and `Retry-After` docs |

**Part 2 (PR 2, #300)**

| File | Change | Responsibility |
|---|---|---|
| `src/ZeroAlloc.Rest/IHttpErrorMapper.cs` | Create | `IHttpErrorMapper<TError>` |
| `src/ZeroAlloc.Rest/Attributes/ErrorMapperAttribute.cs` | Create | `[ErrorMapper]` |
| `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt` | Modify | New API lines |
| `src/ZeroAlloc.Rest.Generator/DiagnosticDescriptors.cs` | Create | ZRA001 to ZRA004 descriptors |
| `src/ZeroAlloc.Rest.Generator/Models/LocationInfo.cs` | Create | Equatable location |
| `src/ZeroAlloc.Rest.Generator/Models/DiagnosticInfo.cs` | Create | A deferred diagnostic |
| `src/ZeroAlloc.Rest.Generator/Models/ErrorMapperModel.cs` | Create | A valid mapper and the error types it maps |
| `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs` | Modify | `Location`, `ErrorTypeName`, `ErrorMapperTypeName`, `MapsError` |
| `src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs` | Modify | `ErrorMappers`, `Diagnostics`, `GetUsedErrorMappings` |
| `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs` | Modify | Carry `E`, resolve mappers, collect diagnostics |
| `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs` | Modify | Report diagnostics, field names, mapper parameter, mapped failure sites, registration |
| `src/ZeroAlloc.Rest.Generator/AnalyzerReleases.Unshipped.md` | Modify | ZRA002 to ZRA004 |
| `src/ZeroAlloc.Rest.Generator/DiEmitter.cs` | None | Verified: `Add{I}` already calls `GeneratedRestClient.AddSerializers` and `Create` |
| `src/ZeroAlloc.Rest.Resilience/*` | None | Verified: the bridge calls `TRestClient.AddSerializers` and `TRestClient.Create` |
| `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs` | Create | Diagnostics, emission, compile checks |
| `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs` | Modify | ZRA001 location; field names |
| `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorMapperTests.cs` | Create | Runtime mapping, a throwing mapper, the span, DI |
| `tests/ZeroAlloc.Rest.Resilience.Tests/RestResilienceErrorMapperTests.cs` | Create | The bridge resolves the mapper |
| `samples/ZeroAlloc.Rest.AotSmoke/IQuoteApi.cs` | Create | A mapped client under ILC |
| `samples/ZeroAlloc.Rest.AotSmoke/Program.cs` | Modify | Resolve and exercise the mapped client |
| `docs/advanced.md`, `docs/resilience.md`, `docs/parameters.md` | Modify | Mapper guide, diagnostics, bridge |

---

# Part 1: the error body (PR 1, #298)

Branch: `feat/rest-error-body` in `C:\wt\rest-errors`, based on `origin/main` at 2.0.1. The design doc is already committed there as `4d561a4`.

### Task 1: Content headers in `HttpError.Headers` (the `fix:` commit)

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs:473-478` (the header copy in `EmitCreateHttpError`)
- Modify: `src/ZeroAlloc.Rest/HttpError.cs:13` (the `Headers` param doc)
- Modify: `docs/advanced.md:30,33,43` (the failure table and the `Headers` row)
- Create: `tests/ZeroAlloc.Rest.Integration.Tests/StubHandler.cs`
- Create: `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorBodyTests.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler` in namespace `ZeroAlloc.Rest.Integration.Tests`. Tasks 3, 6 and 7 use it.
  - The test class `ResultErrorBodyTests`, with the helpers `CreateHttpClient`, `Respond`, `HangAsync` and the constant `ProblemJson`. Task 3 adds tests to it.

- [ ] **Step 0: Commit the plan so it lands on main with PR 1**

```bash
git add docs/plans/2026-09-26-error-body-and-mapper.md
git commit -F - <<'EOF'
docs: plan the error body and error mapper

Implementation plan for #298 and #300, in two sequential PRs.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 1: Write the shared stub handler**

Create `tests/ZeroAlloc.Rest.Integration.Tests/StubHandler.cs`:

```csharp
using System.Net.Http;

namespace ZeroAlloc.Rest.Integration.Tests;

// Answers every request with whatever the test's delegate returns or throws.
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => send(request, cancellationToken);
}
```

`ResultTransportErrorTests` keeps its own private nested `StubHandler`. The nested type shadows this one inside that class, so the two do not conflict.

- [ ] **Step 2: Write the failing integration tests**

Create `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorBodyTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Integration.Tests;

// Issue #298: an HttpError carries the error response's headers, content headers included, its
// body and its media type.
public sealed class ResultErrorBodyTests
{
    private const string ProblemJson = """{"type":"validation","code":"field_required","field":"name"}""";

    private static readonly Uri s_baseAddress = new("http://stub.local/");

    [Fact]
    public async Task StatusError_Headers_IncludeContentHeaders()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal("application/problem+json; charset=utf-8", result.Error.Headers["Content-Type"][0]);
        Assert.Equal("nl-NL", result.Error.Headers["content-language"][0]);
        Assert.Equal("req-9", result.Error.Headers["X-Request-Id"][0]);
    }

    [Fact]
    public async Task DeserializationError_Headers_IncludeContentHeaders()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.OK, "{ not json", "application/json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Equal("application/json; charset=utf-8", result.Error.Headers["Content-Type"][0]);
        Assert.Equal("nl-NL", result.Error.Headers["Content-Language"][0]);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHandler(send)) { BaseAddress = s_baseAddress };
        if (timeout is { } t)
            httpClient.Timeout = t;
        return httpClient;
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body, string mediaType)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
        response.Content.Headers.ContentLanguage.Add("nl-NL");
        response.Headers.Add("X-Request-Id", "req-9");
        return Task.FromResult(response);
    }
}
```

Task 3's tests use the `timeout` parameter; Task 3 also adds a `HangAsync` helper.

- [ ] **Step 3: Write the failing generator test**

Add to `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`, after `Generator_Result_ReturnType_MapsTransportTimeoutAndDeserializationFailures`, which ends at line 275:

```csharp
    [Fact]
    public void Generator_Result_CreateHttpError_CopiesContentHeaders()
    {
        var source = """
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IUserApi
            {
                [Get("/users/{id}")]
                System.Threading.Tasks.Task<ZeroAlloc.Results.Result<string, ZeroAlloc.Rest.HttpError>> GetUserResultAsync(int id, System.Threading.CancellationToken ct = default);
            }
            """;
        var output = GetGeneratedSourceWithResults(source, "IUserApi.g.cs");
        Assert.Contains("foreach (var kvp in response.Headers)", output);
        Assert.Contains("foreach (var kvp in response.Content.Headers)", output);
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj --filter "FullyQualifiedName~ResultErrorBodyTests"`
Expected: FAIL, 2 tests, `System.Collections.Generic.KeyNotFoundException : The given key 'Content-Type' was not present in the dictionary.`

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "FullyQualifiedName~Generator_Result_CreateHttpError_CopiesContentHeaders"`
Expected: FAIL, `Assert.Contains() Failure: Sub-string not found`, for `foreach (var kvp in response.Content.Headers)`.

- [ ] **Step 5: Merge the content headers in the emitter**

In `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`, `EmitCreateHttpError`, replace lines 473-478:

```csharp
        // Header names are case-insensitive, and HttpClient reports known headers in its own
        // casing, such as X-Request-ID, so an ordinal lookup would miss them.
        sb.AppendLine("            var copy = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<string>>(global::System.StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("            foreach (var kvp in response.Headers)");
        sb.AppendLine("                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();");
        sb.AppendLine("            headers = copy;");
```

with:

```csharp
        // Header names are case-insensitive, and HttpClient reports known headers in its own
        // casing, such as X-Request-ID, so an ordinal lookup would miss them. Content headers,
        // such as Content-Type, live on the content, not the response, so both are copied.
        sb.AppendLine("            var copy = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<string>>(global::System.StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("            foreach (var kvp in response.Headers)");
        sb.AppendLine("                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();");
        sb.AppendLine("            foreach (var kvp in response.Content.Headers)");
        sb.AppendLine("                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();");
        sb.AppendLine("            headers = copy;");
```

The emitted code becomes:

```csharp
            var copy = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<string>>(global::System.StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in response.Headers)
                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();
            foreach (var kvp in response.Content.Headers)
                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();
            headers = copy;
```

- [ ] **Step 6: Update the `Headers` doc**

In `src/ZeroAlloc.Rest/HttpError.cs`, replace line 13:

```csharp
/// <param name="Headers">The response headers, or an empty dictionary when no response was received.</param>
```

with:

```csharp
/// <param name="Headers">
/// The response and content headers, such as <c>Content-Type</c>, in one dictionary whose lookups
/// ignore case. It is empty when no response was received.
/// </param>
```

In `docs/advanced.md`, change these rows:
- Line 30, the `Status` row: its `Headers` cell becomes `The response and content headers`.
- Line 33, the `Deserialization` row: its `Headers` cell becomes `The response and content headers`.
- Line 43, the `Headers` property row: its description becomes `Response headers and content headers such as Content-Type, or empty when no response arrived. Lookups ignore case.`

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj --filter "FullyQualifiedName~ResultErrorBodyTests|FullyQualifiedName~ResultTransportErrorTests"`
Expected: PASS, all tests.

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 8: Run the full suite**

Run the full-suite command from Global Constraints. Expected: 0 warnings, all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs src/ZeroAlloc.Rest/HttpError.cs docs/advanced.md \
  tests/ZeroAlloc.Rest.Integration.Tests/StubHandler.cs \
  tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorBodyTests.cs \
  tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs
git commit -F - <<'EOF'
fix: include content headers in HttpError.Headers

The generated client copied only response.Headers, so Content-Type, Content-Length,
Content-Language and the other content headers were silently missing from Status and
Deserialization errors. They are now merged into the same case-insensitive dictionary.

Refs #298

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: `HttpError` body properties and the body-read helper

**Files:**
- Modify: `src/ZeroAlloc.Rest/HttpError.cs:28` (add properties after `Exception`)
- Modify: `src/ZeroAlloc.Rest/GeneratedRestClient.cs:7-13` (`partial`, summary)
- Create: `src/ZeroAlloc.Rest/GeneratedRestClient.ErrorBody.cs`
- Modify: `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt`
- Test: `tests/ZeroAlloc.Rest.Tests/HttpErrorTests.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/ReadErrorBodyTests.cs` (create)

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `public ReadOnlyMemory<byte> HttpError.Body { get; init; }`
  - `public string? HttpError.ContentType { get; init; }`
  - `public bool HttpError.BodyTruncated { get; init; }`
  - `public static ValueTask<(ReadOnlyMemory<byte> Body, bool Truncated)> GeneratedRestClient.ReadErrorBodyAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)`. Task 3's emitted code calls it as `global::ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(response.Content, <cap>, <token>)`.

- [ ] **Step 1: Write the failing `HttpError` tests**

Append to `tests/ZeroAlloc.Rest.Tests/HttpErrorTests.cs`, inside the class, after `KindAndException_CanBeInitialised`:

```csharp
    [Fact]
    public void Body_DefaultsToEmpty_ContentTypeToNull_AndNotTruncated()
    {
        var error = new HttpError(HttpStatusCode.BadRequest,
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.True(error.Body.IsEmpty);
        Assert.Null(error.ContentType);
        Assert.False(error.BodyTruncated);
    }

    [Fact]
    public void BodyContentTypeAndTruncated_CanBeInitialised()
    {
        byte[] body = [1, 2, 3];

        var error = new HttpError(HttpStatusCode.UnprocessableEntity,
            new Dictionary<string, IReadOnlyList<string>>())
        {
            Body = body,
            ContentType = "application/problem+json",
            BodyTruncated = true,
        };

        Assert.Equal(body, error.Body.ToArray());
        Assert.Equal("application/problem+json", error.ContentType);
        Assert.True(error.BodyTruncated);
    }
```

- [ ] **Step 2: Write the failing helper tests**

Create `tests/ZeroAlloc.Rest.Tests/ReadErrorBodyTests.cs`:

```csharp
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

// GeneratedRestClient.ReadErrorBodyAsync reads the body of a non-success response for an HttpError.
// These tests use unbuffered content: HttpClient buffers every response before the generated
// client sees it, so only direct calls reach the unknown-length and failing-read paths.
public sealed class ReadErrorBodyTests
{
    private static readonly byte[] s_json = Encoding.UTF8.GetBytes("""{"code":"field_required","field":"name"}""");

    [Fact]
    public async Task KnownLength_UnderTheCap_ReadsIntoAnExactBuffer()
    {
        using var content = new ByteArrayContent(s_json);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
        Assert.True(MemoryMarshal.TryGetArray(body, out var segment));
        Assert.Equal(s_json.Length, segment.Array!.Length);
    }

    [Fact]
    public async Task KnownLength_AtTheCap_IsNotTruncated()
    {
        using var content = new ByteArrayContent(s_json);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, s_json.Length, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
    }

    [Fact]
    public async Task KnownLength_OverTheCap_KeepsTheFirstMaxBytes_AndIsTruncated()
    {
        using var content = new ByteArrayContent(s_json);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 10, CancellationToken.None);

        Assert.Equal(s_json[..10], body.ToArray());
        Assert.True(truncated);
    }

    [Fact]
    public async Task KnownEmptyBody_IsEmptyAndAllocatesNoArray()
    {
        using var content = new ByteArrayContent([]);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        // ReadOnlyMemory<byte>.Empty is default: equal only when no array backs the memory.
        Assert.True(body.Equals(ReadOnlyMemory<byte>.Empty));
        Assert.False(truncated);
    }

    [Fact]
    public async Task UnknownLength_EmptyBody_IsEmptyAndAllocatesNoArray()
    {
        using var content = new StreamContent(new TestStream([]));
        Assert.Null(content.Headers.ContentLength);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.True(body.Equals(ReadOnlyMemory<byte>.Empty));
        Assert.False(truncated);
    }

    [Fact]
    public async Task UnknownLength_UnderTheCap_IsCopiedOnceIntoAnExactArray()
    {
        using var content = new StreamContent(new TestStream(s_json));
        Assert.Null(content.Headers.ContentLength);

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
        Assert.True(MemoryMarshal.TryGetArray(body, out var segment));
        Assert.Equal(0, segment.Offset);
        Assert.Equal(s_json.Length, segment.Array!.Length);
    }

    [Fact]
    public async Task UnknownLength_AtTheCap_IsNotTruncated()
    {
        using var content = new StreamContent(new TestStream(s_json));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, s_json.Length, CancellationToken.None);

        Assert.Equal(s_json, body.ToArray());
        Assert.False(truncated);
    }

    [Fact]
    public async Task UnknownLength_OverTheCap_KeepsTheFirstMaxBytes_AndIsTruncated()
    {
        using var content = new StreamContent(new TestStream(s_json));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 10, CancellationToken.None);

        Assert.Equal(s_json[..10], body.ToArray());
        Assert.True(truncated);
    }

    [Fact]
    public async Task UnknownLength_CapAboveOneMebibyte_GrowsTheBuffer()
    {
        var large = new byte[3 * 1024 * 1024];
        new Random(42).NextBytes(large);
        using var content = new StreamContent(new TestStream(large, chunk: 64 * 1024));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 4 * 1024 * 1024, CancellationToken.None);

        Assert.Equal(large, body.ToArray());
        Assert.False(truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CapZeroOrLess_ReadsNothing(int maxBytes)
    {
        // Reading would throw an exception the helper does not catch.
        using var content = new StreamContent(new TestStream(s_json, failure: () => new FormatException("unexpected read")));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, maxBytes, CancellationToken.None);

        Assert.True(body.IsEmpty);
        Assert.False(truncated);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("http")]
    [InlineData("disposed")]
    [InlineData("consumed")]
    [InlineData("timeout")]
    public async Task ReadFailure_GivesAnEmptyBody(string kind)
    {
        using var content = new StreamContent(new TestStream(s_json, failure: () => Failure(kind), failAt: 7));

        var (body, truncated) = await GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None);

        Assert.True(body.IsEmpty);
        Assert.False(truncated);
    }

    [Fact]
    public async Task CallerCancellationDuringTheRead_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var content = new StreamContent(new TestStream(
            s_json, failure: () => new OperationCanceledException(cts.Token), failAt: 7, cancelFirst: cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GeneratedRestClient.ReadErrorBodyAsync(content, 1024, cts.Token).AsTask());
    }

    [Fact]
    public async Task CallerCancellationSurfacingAsAnIOError_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        using var content = new StreamContent(new TestStream(
            s_json, failure: () => new IOException("aborted"), failAt: 7, cancelFirst: cts));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GeneratedRestClient.ReadErrorBodyAsync(content, 1024, cts.Token).AsTask());

        Assert.IsType<IOException>(ex.InnerException);
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task AnUnrelatedException_Propagates()
    {
        using var content = new StreamContent(new TestStream(s_json, failure: () => new FormatException("unexpected read")));

        await Assert.ThrowsAsync<FormatException>(
            () => GeneratedRestClient.ReadErrorBodyAsync(content, 1024, CancellationToken.None).AsTask());
    }

    private static Exception Failure(string kind) => kind switch
    {
        "io" => new IOException("connection reset"),
        "http" => new HttpRequestException("response ended prematurely"),
        "disposed" => new ObjectDisposedException("content"),
        "consumed" => new InvalidOperationException("The stream was already consumed."),
        "timeout" => new OperationCanceledException("timed out"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // A stream with no length, like a chunked response. It hands out at most `chunk` bytes per
    // read. Once `failAt` bytes have been read, it cancels `cancelFirst`, if given, and throws.
    private sealed class TestStream(
        byte[] data,
        Func<Exception>? failure = null,
        int failAt = 0,
        CancellationTokenSource? cancelFirst = null,
        int chunk = 7) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (failure is not null && _position >= failAt)
            {
                cancelFirst?.Cancel();
                throw failure();
            }

            var count = Math.Min(Math.Min(buffer.Length, chunk), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj --filter "FullyQualifiedName~ReadErrorBodyTests|FullyQualifiedName~HttpErrorTests"`
Expected: build FAIL with `CS0117: 'GeneratedRestClient' does not contain a definition for 'ReadErrorBodyAsync'` and `CS0117: 'HttpError' does not contain a definition for 'Body'`.

- [ ] **Step 4: Add the `HttpError` properties**

In `src/ZeroAlloc.Rest/HttpError.cs`, after line 28, which is `public Exception? Exception { get; init; }`, insert:

```csharp

    /// <summary>
    /// The response body of a <see cref="HttpErrorKind.Status"/> failure, cut to the client's
    /// <c>MaxErrorBodyBytes</c>. It is empty for the other kinds, for a response without a body, when
    /// reading is turned off, and when the body could not be read.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// The media type of the response body, such as <c>application/problem+json</c>, without
    /// parameters. Set for <see cref="HttpErrorKind.Status"/> and
    /// <see cref="HttpErrorKind.Deserialization"/> failures whose response declares one.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// <see langword="true"/> when the body was longer than <c>MaxErrorBodyBytes</c>, so
    /// <see cref="Body"/> holds only its start. Do not parse a truncated body as a whole document.
    /// </summary>
    public bool BodyTruncated { get; init; }
```

- [ ] **Step 5: Make `GeneratedRestClient` partial**

In `src/ZeroAlloc.Rest/GeneratedRestClient.cs`, replace lines 7-13:

```csharp
/// <summary>
/// Calls the <see cref="IGeneratedRestClient{TSelf}"/> static members of a generated client.
/// Generated clients implement those members explicitly, so they never clash with the client
/// interface's own methods; the generated <c>Add{I}</c> reaches them through this helper.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedRestClient
```

with:

```csharp
/// <summary>
/// Support members for generated clients. It calls the <see cref="IGeneratedRestClient{TSelf}"/>
/// static members of a generated client: generated clients implement those members explicitly, so
/// they never clash with the client interface's own methods, and the generated <c>Add{I}</c>
/// reaches them through this helper. It also reads error bodies for generated clients.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class GeneratedRestClient
```

- [ ] **Step 6: Write the helper**

Create `src/ZeroAlloc.Rest/GeneratedRestClient.ErrorBody.cs`:

```csharp
using System.Buffers;

namespace ZeroAlloc.Rest;

public static partial class GeneratedRestClient
{
    // Caps up to this size rent one buffer that holds the whole capped body, so the body is copied
    // once. A larger cap starts at this size and grows, so a huge cap never rents a huge buffer
    // up front.
    private const int MaxInitialErrorBodyRent = 1024 * 1024;

    /// <summary>
    /// Reads the body of a non-success response for an <see cref="HttpError"/>. Generated clients
    /// call this before the response is disposed.
    /// </summary>
    /// <param name="content">The response content.</param>
    /// <param name="maxBytes">The most bytes to keep. A value of 0 or less reads nothing.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// The body, cut to <paramref name="maxBytes"/>, and whether it was cut. The body is empty when
    /// the response has none, and when reading it fails: the status is the real failure, and a
    /// broken body must not hide it.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled during the read.
    /// </exception>
    public static async ValueTask<(ReadOnlyMemory<byte> Body, bool Truncated)> ReadErrorBodyAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var length = content.Headers.ContentLength;
        if (maxBytes <= 0 || length == 0)
            return (ReadOnlyMemory<byte>.Empty, false);

        try
        {
            // The content owns this stream and disposes it with the response.
            var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            if (length is { } known && known <= maxBytes)
                return (await ReadExactAsync(stream, (int)known, cancellationToken).ConfigureAwait(false), false);
            return await ReadCappedAsync(stream, maxBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            // The caller asked to stop. Cancelling can surface mid-read as an I/O error, so it is
            // reported as the cancellation it is.
            if (ex is OperationCanceledException)
                throw;
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A timeout the caller did not ask for.
            return (ReadOnlyMemory<byte>.Empty, false);
        }
        catch (IOException)
        {
            // The connection broke mid-body.
            return (ReadOnlyMemory<byte>.Empty, false);
        }
        catch (HttpRequestException)
        {
            return (ReadOnlyMemory<byte>.Empty, false);
        }
        catch (InvalidOperationException)
        {
            // The content was already consumed or disposed; ObjectDisposedException derives from this.
            return (ReadOnlyMemory<byte>.Empty, false);
        }
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadExactAsync(
        Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var read = await stream.ReadAtLeastAsync(buffer, length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        return read == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(buffer, 0, read);
    }

    private static async ValueTask<(ReadOnlyMemory<byte> Body, bool Truncated)> ReadCappedAsync(
        Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        // One byte past the cap tells a body of exactly maxBytes from a longer one.
        var limit = (int)Math.Min((long)maxBytes + 1, Array.MaxLength);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(limit, MaxInitialErrorBodyRent));
        var total = 0;
        try
        {
            while (total < limit)
            {
                if (total == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent((int)Math.Min((long)buffer.Length * 2, limit));
                    buffer.AsSpan(0, total).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var window = Math.Min(buffer.Length, limit) - total;
                var read = await stream.ReadAsync(buffer.AsMemory(total, window), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
            }

            var truncated = total > maxBytes;
            var kept = truncated ? maxBytes : total;
            return kept == 0
                ? (ReadOnlyMemory<byte>.Empty, false)
                : (new ReadOnlyMemory<byte>(buffer.AsSpan(0, kept).ToArray()), truncated);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
```

- [ ] **Step 7: Record the public API**

Replace the contents of `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt` with:

```text
#nullable enable
static ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(System.Net.Http.HttpContent! content, int maxBytes, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<(System.ReadOnlyMemory<byte> Body, bool Truncated)>
ZeroAlloc.Rest.HttpError.Body.get -> System.ReadOnlyMemory<byte>
ZeroAlloc.Rest.HttpError.Body.init -> void
ZeroAlloc.Rest.HttpError.BodyTruncated.get -> bool
ZeroAlloc.Rest.HttpError.BodyTruncated.init -> void
ZeroAlloc.Rest.HttpError.ContentType.get -> string?
ZeroAlloc.Rest.HttpError.ContentType.init -> void
```

If RS0016 or RS0017 reports a different spelling for any line, replace that line with the exact text from the RS0016 message. The analyzer's format is authoritative.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj --filter "FullyQualifiedName~ReadErrorBodyTests|FullyQualifiedName~HttpErrorTests"`
Expected: PASS, all tests.

- [ ] **Step 9: Run the full suite**

Run the full-suite command. Expected: 0 warnings, all tests pass. If an analyzer flags the helper, fix the code; do not suppress. For example, if ErrorProne reports a catch clause, restructure that clause.

- [ ] **Step 10: Commit**

```bash
git add src/ZeroAlloc.Rest/HttpError.cs src/ZeroAlloc.Rest/GeneratedRestClient.cs \
  src/ZeroAlloc.Rest/GeneratedRestClient.ErrorBody.cs src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt \
  tests/ZeroAlloc.Rest.Tests/HttpErrorTests.cs tests/ZeroAlloc.Rest.Tests/ReadErrorBodyTests.cs
git commit -F - <<'EOF'
feat: add Body, ContentType and BodyTruncated to HttpError

HttpError gains the error response body, its media type and a truncation flag. A new
GeneratedRestClient.ReadErrorBodyAsync reads the body for generated clients:

- a known Content-Length within the cap sizes one exact buffer
- otherwise a pooled buffer is read up to the cap and copied once
- an empty body allocates nothing
- a failed read gives an empty body, so the status stays the failure
- caller cancellation still throws

Refs #298

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Generated clients read the error body, with `MaxErrorBodyBytes`

**Files:**
- Modify: `src/ZeroAlloc.Rest/Attributes/ZeroAllocRestClientAttribute.cs:1-4`
- Modify: `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt`
- Modify: `src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs:5-11`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs:20,50`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`:
  - `using` directives, lines 1-4;
  - the call site at line 102;
  - `EmitMethod`, lines 177 and 220;
  - `EmitSendAndResponse`, lines 331 and 352;
  - `EmitResponseHandling`, lines 394 and 424-428;
  - `EmitCreateHttpError`, lines 466-492.
- Modify: `samples/ZeroAlloc.Rest.AotSmoke/Program.cs:86`
- Create: `samples/ZeroAlloc.Rest.AotSmoke/UnprocessableHandler.cs`
- Modify: `docs/advanced.md`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`, `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorBodyTests.cs`

**Interfaces:**
- Consumes (Task 2): `GeneratedRestClient.ReadErrorBodyAsync(HttpContent, int, CancellationToken)` and the `HttpError` init properties `Body`, `ContentType` and `BodyTruncated`.
- Produces:
  - `public int ZeroAllocRestClientAttribute.MaxErrorBodyBytes { get; set; } = 65536`.
  - `ClientModel.MaxErrorBodyBytes`, an `int`, always 0 or more.
  - The emitted `__CreateHttpError(HttpErrorKind kind, HttpResponseMessage? response, Exception? exception, ReadOnlyMemory<byte> body = default, bool bodyTruncated = false)`.
  - `EmitMethod(..., int maxErrorBodyBytes)` and `EmitSendAndResponse(..., int maxErrorBodyBytes)`.
  - `EmitResponseHandling(StringBuilder sb, MethodModel method, string ctArg, string serializerExpr, int maxErrorBodyBytes, string indent = "        ")`. Part 2 extends these signatures.

- [ ] **Step 1: Write the failing generator tests**

Add to `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`, after `Generator_Result_CreateHttpError_CopiesContentHeaders`:

```csharp
    private const string ResultApiSource = """
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IUserApi
        {
            [Get("/users/{id}")]
            System.Threading.Tasks.Task<ZeroAlloc.Results.Result<string, ZeroAlloc.Rest.HttpError>> GetUserResultAsync(int id, System.Threading.CancellationToken ct = default);
        }
        """;

    [Fact]
    public void Generator_Result_ReadsTheErrorBodyOnlyOnTheStatusPath_WithTheDefaultCap()
    {
        var output = GetGeneratedSourceWithResults(ResultApiSource, "IUserApi.g.cs");

        Assert.Equal(1, CountOccurrences(output, "ReadErrorBodyAsync("));
        Assert.Contains(
            "var __errorBody = await global::ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(response.Content, 65536, ct).ConfigureAwait(false);",
            output);
        Assert.Contains(
            "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null, __errorBody.Body, __errorBody.Truncated));",
            output);
        // Transport, Timeout and Deserialization carry no body.
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Timeout, null, __ex));", output);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Transport, null, __ex));", output);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Deserialization, response, __ex));", output);
    }

    [Fact]
    public void Generator_Result_SetsBodyContentTypeAndTruncatedOnTheError()
    {
        var output = GetGeneratedSourceWithResults(ResultApiSource, "IUserApi.g.cs");

        Assert.Contains("global::System.ReadOnlyMemory<byte> body = default, bool bodyTruncated = false)", output);
        Assert.Contains("contentType = response.Content.Headers.ContentType?.MediaType;", output);
        Assert.Contains("Body = body,", output);
        Assert.Contains("ContentType = contentType,", output);
        Assert.Contains("BodyTruncated = bodyTruncated,", output);
    }

    [Fact]
    public void Generator_Result_CustomCap_IsEmitted()
    {
        var source = ResultApiSource.Replace("[ZeroAllocRestClient]", "[ZeroAllocRestClient(MaxErrorBodyBytes = 1024)]");

        var output = GetGeneratedSourceWithResults(source, "IUserApi.g.cs");

        Assert.Contains("ReadErrorBodyAsync(response.Content, 1024, ct)", output);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Generator_Result_CapZeroOrLess_EmitsNoBodyRead(int cap)
    {
        var source = ResultApiSource.Replace(
            "[ZeroAllocRestClient]", $"[ZeroAllocRestClient(MaxErrorBodyBytes = {cap})]");

        var output = GetGeneratedSourceWithResults(source, "IUserApi.g.cs");

        Assert.DoesNotContain("ReadErrorBodyAsync", output);
        Assert.DoesNotContain("__errorBody", output);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null));", output);
    }

    [Fact]
    public void Generator_Result_WithoutToken_PassesDefaultToTheBodyRead()
    {
        var source = ResultApiSource.Replace(", System.Threading.CancellationToken ct = default", "");

        var output = GetGeneratedSourceWithResults(source, "IUserApi.g.cs");

        Assert.Contains("ReadErrorBodyAsync(response.Content, 65536, default)", output);
    }
```

In `Generator_NonResult_ReturnType_KeepsRethrowingOnly`, around line 294, add after `Assert.DoesNotContain("__CreateHttpError", output);`:

```csharp
        Assert.DoesNotContain("ReadErrorBodyAsync", output);
```

- [ ] **Step 2: Write the failing integration tests**

Add these interfaces to `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorBodyTests.cs`, between the `namespace` line and the `ResultErrorBodyTests` class. Also add `using ZeroAlloc.Rest.Attributes;` and `using ZeroAlloc.Results;` to its usings.

```csharp
[ZeroAllocRestClient(MaxErrorBodyBytes = 16)]
public interface ISmallCapApi
{
    [Get("/users/{id}/result")]
    Task<Result<UserDto, HttpError>> GetUserResultAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient(MaxErrorBodyBytes = 0)]
public interface INoErrorBodyApi
{
    [Get("/users/{id}/result")]
    Task<Result<UserDto, HttpError>> GetUserResultAsync(int id, CancellationToken ct = default);
}
```

Add these tests inside `ResultErrorBodyTests`, before the private helpers:

```csharp
    [Fact]
    public async Task StatusError_CarriesTheBody_AndItsMediaType()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(Encoding.UTF8.GetBytes(ProblemJson), result.Error.Body.ToArray());
        Assert.Equal("application/problem+json", result.Error.ContentType);
        Assert.False(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_WithoutABody_HasAnEmptyBody()
    {
        using var httpClient = CreateHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.Error.Body.IsEmpty);
        Assert.Null(result.Error.ContentType);
        Assert.False(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_BodyOverTheCap_IsTruncatedToTheCap()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        var client = new SmallCapApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(Encoding.UTF8.GetBytes(ProblemJson)[..16], result.Error.Body.ToArray());
        Assert.True(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_BodyAtTheCap_IsNotTruncated()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.BadRequest, "0123456789abcdef", "text/plain"));
        var client = new SmallCapApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(16, result.Error.Body.Length);
        Assert.False(result.Error.BodyTruncated);
    }

    [Fact]
    public async Task StatusError_WithoutContentLength_StillCarriesTheBody()
    {
        var bytes = Encoding.UTF8.GetBytes(ProblemJson);
        using var httpClient = CreateHttpClient((_, _) =>
        {
            var content = new StreamContent(new UnseekableStream(bytes));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/problem+json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = content });
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(bytes, result.Error.Body.ToArray());
        Assert.Equal("application/problem+json", result.Error.ContentType);
    }

    [Fact]
    public async Task CapZero_ReadsNoBody_ButKeepsTheMediaType()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json"));
        var client = new NoErrorBodyApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.True(result.Error.Body.IsEmpty);
        Assert.False(result.Error.BodyTruncated);
        Assert.Equal("application/problem+json", result.Error.ContentType);
    }

    [Fact]
    public async Task CallerCancellationAroundTheBodyRead_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, _) =>
        {
            cts.Cancel();
            return Respond(HttpStatusCode.UnprocessableEntity, ProblemJson, "application/problem+json");
        });
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserResultAsync(1, cts.Token));
    }

    [Fact]
    public async Task TransportAndTimeoutErrors_HaveNoBodyAndNoContentType()
    {
        using var refusing = CreateHttpClient((_, _) => throw new HttpRequestException("refused"));
        using var hanging = CreateHttpClient(HangAsync, TimeSpan.FromMilliseconds(50));

        var transport = await new UserApiClient(refusing, new SystemTextJsonSerializer()).GetUserResultAsync(1);
        var timeout = await new UserApiClient(hanging, new SystemTextJsonSerializer()).GetUserResultAsync(1);

        Assert.True(transport.Error.Body.IsEmpty);
        Assert.Null(transport.Error.ContentType);
        Assert.True(timeout.Error.Body.IsEmpty);
        Assert.Null(timeout.Error.ContentType);
    }

    [Fact]
    public async Task DeserializationError_HasNoBody_ButHasTheMediaType()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.OK, "{ not json", "application/json"));
        IUserApi client = new UserApiClient(httpClient, new SystemTextJsonSerializer());

        var result = await client.GetUserResultAsync(1);

        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.True(result.Error.Body.IsEmpty);
        Assert.Equal("application/json", result.Error.ContentType);
    }
```

Add these members after `Respond`:

```csharp
    private static async Task<HttpResponseMessage> HangAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        throw new InvalidOperationException("unreachable");
    }

    // StreamContent reports no Content-Length for a stream it cannot seek.
    private sealed class UnseekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorEmissionTests"`
Expected: FAIL. The test sources are strings, so the project compiles, and the new `Generator_Result_*` tests fail with `Assert.Contains() Failure: Sub-string not found`. The cap-0 and negative-cap cases already pass, because no body read exists yet. After step 6 they guard the cap.

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj --filter "FullyQualifiedName~ResultErrorBodyTests"`
Expected: build FAIL. The compiler rejects `MaxErrorBodyBytes` in `[ZeroAllocRestClient(MaxErrorBodyBytes = 16)]` as an unknown attribute property.

- [ ] **Step 4: Add the attribute property**

Replace `src/ZeroAlloc.Rest/Attributes/ZeroAllocRestClientAttribute.cs` with:

```csharp
namespace ZeroAlloc.Rest.Attributes;

/// <summary>Generates a REST client for the interface it is applied to.</summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class ZeroAllocRestClientAttribute : Attribute
{
    /// <summary>
    /// The most bytes of an error response body that a <c>Result&lt;T, HttpError&gt;</c> method keeps
    /// in <see cref="HttpError.Body"/>. A longer body is cut to this size and
    /// <see cref="HttpError.BodyTruncated"/> is set. A value of 0 or less turns reading off, and the
    /// generated client then contains no body-reading code. Read at compile time. Defaults to
    /// 65536.
    /// </summary>
    public int MaxErrorBodyBytes { get; set; } = 65536;
}
```

Append to `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt`:

```text
ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute.MaxErrorBodyBytes.get -> int
ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute.MaxErrorBodyBytes.set -> void
```

- [ ] **Step 5: Carry the cap through the model**

In `src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs`, replace lines 5-11:

```csharp
internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName,
    bool IsPublic)
```

with:

```csharp
internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName,
    bool IsPublic,
    int MaxErrorBodyBytes)
```

In `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`, after line 20, which is `private const string ResultOpenType = ...`, add:

```csharp
    private const int DefaultMaxErrorBodyBytes = 65536;
```

Replace line 50:

```csharp
        return new ClientModel(ns, interfaceName, className, methods.AsReadOnly(), clientSerializer, IsEffectivelyPublic(interfaceSymbol));
```

with:

```csharp
        return new ClientModel(ns, interfaceName, className, methods.AsReadOnly(), clientSerializer,
            IsEffectivelyPublic(interfaceSymbol), GetMaxErrorBodyBytes(ctx));
```

Add after `IsEffectivelyPublic`, which ends at line 61:

```csharp
    // [ZeroAllocRestClient(MaxErrorBodyBytes = n)]. A value below 0 means the same as 0: no read.
    private static int GetMaxErrorBodyBytes(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attr in ctx.Attributes)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "MaxErrorBodyBytes" && namedArg.Value.Value is int value)
                    return value < 0 ? 0 : value;
            }
        }
        return DefaultMaxErrorBodyBytes;
    }
```

- [ ] **Step 6: Thread the cap through the emitter**

In `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`:

Add `using System.Globalization;` after line 1, `using System.Collections.Generic;`.

Line 102 becomes:

```csharp
            EmitMethod(ctx, sb, model.InterfaceName, method, serializerFieldMap, model.MaxErrorBodyBytes);
```

Line 177 becomes:

```csharp
    private static void EmitMethod(SourceProductionContext ctx, StringBuilder sb, string interfaceName, MethodModel method, IReadOnlyDictionary<string, string> serializerFieldMap, int maxErrorBodyBytes)
```

Line 220 becomes:

```csharp
        EmitSendAndResponse(sb, method, ctParam?.Name, serializerExpr, maxErrorBodyBytes);
```

Line 331 becomes:

```csharp
    private static void EmitSendAndResponse(StringBuilder sb, MethodModel method, string? callerToken, string serializerExpr, int maxErrorBodyBytes)
```

Line 352 becomes:

```csharp
        EmitResponseHandling(sb, method, ctArg, serializerExpr, maxErrorBodyBytes, indent: "            ");
```

Line 394 becomes:

```csharp
    private static void EmitResponseHandling(StringBuilder sb, MethodModel method, string ctArg, string serializerExpr, int maxErrorBodyBytes, string indent = "        ")
```

Replace lines 424-428, the `else` branch of the Result path:

```csharp
            sb.AppendLine($"{indent}else");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{i1}return {resultType}.Failure(");
            sb.AppendLine($"{i1}    __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null));");
            sb.AppendLine($"{indent}}}");
```

with:

```csharp
            sb.AppendLine($"{indent}else");
            sb.AppendLine($"{indent}{{");
            if (maxErrorBodyBytes > 0)
            {
                // Read before the response is disposed. The helper caps the body, turns a failed read
                // into an empty body, and still throws on caller cancellation.
                var cap = maxErrorBodyBytes.ToString(CultureInfo.InvariantCulture);
                sb.AppendLine($"{i1}var __errorBody = await global::ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(response.Content, {cap}, {ctArg}).ConfigureAwait(false);");
                sb.AppendLine($"{i1}return {resultType}.Failure(");
                sb.AppendLine($"{i1}    __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null, __errorBody.Body, __errorBody.Truncated));");
            }
            else
            {
                sb.AppendLine($"{i1}return {resultType}.Failure(");
                sb.AppendLine($"{i1}    __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null));");
            }
            sb.AppendLine($"{indent}}}");
```

- [ ] **Step 7: Extend `__CreateHttpError`**

In `EmitCreateHttpError`, replace lines 466-492, from the `__CreateHttpError` signature line through the `};` of the object initializer, with:

```csharp
        sb.AppendLine("    private static global::ZeroAlloc.Rest.HttpError __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind kind, global::System.Net.Http.HttpResponseMessage? response, global::System.Exception? exception, global::System.ReadOnlyMemory<byte> body = default, bool bodyTruncated = false)");
        sb.AppendLine("    {");
        sb.AppendLine("        global::System.Net.HttpStatusCode statusCode;");
        sb.AppendLine($"        {Headers} headers;");
        sb.AppendLine("        string? contentType = null;");
        sb.AppendLine("        if (response is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            statusCode = response.StatusCode;");
        // Header names are case-insensitive, and HttpClient reports known headers in its own
        // casing, such as X-Request-ID, so an ordinal lookup would miss them. Content headers,
        // such as Content-Type, live on the content, not the response, so both are copied.
        sb.AppendLine("            var copy = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<string>>(global::System.StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("            foreach (var kvp in response.Headers)");
        sb.AppendLine("                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();");
        sb.AppendLine("            foreach (var kvp in response.Content.Headers)");
        sb.AppendLine("                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();");
        sb.AppendLine("            headers = copy;");
        sb.AppendLine("            contentType = response.Content.Headers.ContentType?.MediaType;");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            // No response arrived. An HttpRequestException may still carry a status code.");
        sb.AppendLine("            statusCode = exception is global::System.Net.Http.HttpRequestException { StatusCode: { } requestStatus }");
        sb.AppendLine("                ? requestStatus");
        sb.AppendLine("                : (global::System.Net.HttpStatusCode)0;");
        sb.AppendLine("            headers = __noHeaders;");
        sb.AppendLine("        }");
        sb.AppendLine("        return new global::ZeroAlloc.Rest.HttpError(statusCode, headers, exception?.Message)");
        sb.AppendLine("        {");
        sb.AppendLine("            Kind = kind,");
        sb.AppendLine("            Exception = exception,");
        sb.AppendLine("            Body = body,");
        sb.AppendLine("            ContentType = contentType,");
        sb.AppendLine("            BodyTruncated = bodyTruncated,");
        sb.AppendLine("        };");
```

The emitted `Status` branch, for `IUserApi.GetUserResultAsync(int id, CancellationToken ct)` with the default cap, is now:

```csharp
            else
            {
                var __errorBody = await global::ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(response.Content, 65536, ct).ConfigureAwait(false);
                return ZeroAlloc.Results.Result<string, ZeroAlloc.Rest.HttpError>.Failure(
                    __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null, __errorBody.Body, __errorBody.Truncated));
            }
```

The emitted `__CreateHttpError` is:

```csharp
    private static global::ZeroAlloc.Rest.HttpError __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind kind, global::System.Net.Http.HttpResponseMessage? response, global::System.Exception? exception, global::System.ReadOnlyMemory<byte> body = default, bool bodyTruncated = false)
    {
        global::System.Net.HttpStatusCode statusCode;
        global::System.Collections.Generic.IReadOnlyDictionary<string, global::System.Collections.Generic.IReadOnlyList<string>> headers;
        string? contentType = null;
        if (response is not null)
        {
            statusCode = response.StatusCode;
            var copy = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<string>>(global::System.StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in response.Headers)
                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();
            foreach (var kvp in response.Content.Headers)
                copy[kvp.Key] = new global::System.Collections.Generic.List<string>(kvp.Value).AsReadOnly();
            headers = copy;
            contentType = response.Content.Headers.ContentType?.MediaType;
        }
        else
        {
            // No response arrived. An HttpRequestException may still carry a status code.
            statusCode = exception is global::System.Net.Http.HttpRequestException { StatusCode: { } requestStatus }
                ? requestStatus
                : (global::System.Net.HttpStatusCode)0;
            headers = __noHeaders;
        }
        return new global::ZeroAlloc.Rest.HttpError(statusCode, headers, exception?.Message)
        {
            Kind = kind,
            Exception = exception,
            Body = body,
            ContentType = contentType,
            BodyTruncated = bodyTruncated,
        };
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj`
Expected: PASS, all tests. This includes `Generator_Result_ReturnType_CompilesForValueNullableAndTokenlessShapes`, which compiles the new emitted code, and the existing single-`new HttpError(` count.

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj --filter "FullyQualifiedName~ResultErrorBodyTests|FullyQualifiedName~ResultTransportErrorTests"`
Expected: PASS, all tests.

- [ ] **Step 9: Add the AOT smoke check for the body read**

Create `samples/ZeroAlloc.Rest.AotSmoke/UnprocessableHandler.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.AotSmoke;

// Answers every request with a 422 and a problem+json body, so the smoke can check under ILC that
// a Result method reads the error body.
internal sealed class UnprocessableHandler : HttpMessageHandler
{
    internal const string Body = """{"code":"field_required","field":"name"}""";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/problem+json"),
        });
}
```

In `samples/ZeroAlloc.Rest.AotSmoke/Program.cs`, insert after line 86, which is the closing `}` of the `failingHttp` block, before `Console.WriteLine("AOT smoke: PASS");`:

```csharp

// A non-success status carries its body: the generated client reads it through
// GeneratedRestClient.ReadErrorBodyAsync before disposing the response.
using (var rejectingHttp = new System.Net.Http.HttpClient(new UnprocessableHandler()) { BaseAddress = new Uri("http://localhost/") })
{
    IUserApi rejecting = new UserApiClient(rejectingHttp, new SmokeSerializer());
    var result = await rejecting.TryGetUserAsync(1).ConfigureAwait(false);
    if (!result.IsFailure
        || result.Error.Kind != HttpErrorKind.Status
        || result.Error.Body.Length != UnprocessableHandler.Body.Length
        || !string.Equals(result.Error.ContentType, "application/problem+json", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a 422 should carry its body and media type");
        return 1;
    }
}
```

- [ ] **Step 10: Publish and run the AOT smoke**

Run:

```bash
dotnet publish samples/ZeroAlloc.Rest.AotSmoke/ZeroAlloc.Rest.AotSmoke.csproj -r win-x64 -c Release -o C:/wt/aot-out-rest
C:/wt/aot-out-rest/ZeroAlloc.Rest.AotSmoke.exe
```

Expected: the publish reports no IL2026, IL3050 or other trim/AOT warnings, and the binary prints `AOT smoke: PASS`. A native AOT publish on Windows needs the MSVC toolchain. If it is not installed, say so in the PR and rely on CI's `aot-smoke` job, which runs the same publish on `linux-x64`. Do not skip the check silently.

- [ ] **Step 11: Document the body and the cap**

In `docs/advanced.md`, add three rows to the `HttpError` property table, after the `Exception` row at line 45:

```markdown
| `Body` | `ReadOnlyMemory<byte>` | The response body of a `Status` failure, up to `MaxErrorBodyBytes`. Empty for the other kinds, for a response without a body, and when the body could not be read. |
| `ContentType` | `string?` | The media type of the response body, such as `application/problem+json`, for `Status` and `Deserialization` failures |
| `BodyTruncated` | `bool` | `true` when the body was longer than `MaxErrorBodyBytes`, so `Body` holds only its start |
```

Insert this subsection before `### What still throws` at line 48:

````markdown
### The error body

For a `Status` failure the client reads the response body before it disposes the response, so an API's error detail survives:

```csharp
var result = await api.CreateUserAsync(request);
if (result.IsFailure
    && result.Error.Kind == HttpErrorKind.Status
    && result.Error.ContentType == "application/problem+json"
    && !result.Error.BodyTruncated)
{
    using var problem = JsonDocument.Parse(result.Error.Body);
    var detail = problem.RootElement.GetProperty("detail").GetString();
}
```

The body is capped at 64 KiB. Change the cap per client:

```csharp
[ZeroAllocRestClient(MaxErrorBodyBytes = 4096)]
public interface IUserApi { ... }
```

- A longer body is cut to the cap and `BodyTruncated` is `true`. Don't parse a truncated body as a whole document.
- `MaxErrorBodyBytes = 0` turns reading off: the generated client contains no body-reading code. A negative value does the same.
- The cap limits what `HttpError` keeps, not what is downloaded. `HttpClient` has already buffered the response when the client sees it; limit that with `HttpClient.MaxResponseContentBufferSize`.
- If reading the body fails, the failure is still a `Status` error, with an empty `Body`: the status is the real failure. Cancellation you asked for still throws.
- Only `Status` failures carry a body. `Transport` and `Timeout` failures have no response, and a `Deserialization` failure's body has already been consumed by the serializer.
````

Also change the `docs/advanced.md` front-matter `description` on line 6 to:

```text
description: Result<T, HttpError> with the error body, multiple serializers, CancellationToken, and edge cases.
```

- [ ] **Step 12: Run the full suite**

Run the full-suite command. Expected: 0 warnings, all tests pass.

- [ ] **Step 13: Commit**

```bash
git add src/ZeroAlloc.Rest/Attributes/ZeroAllocRestClientAttribute.cs src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt \
  src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs src/ZeroAlloc.Rest.Generator/ModelExtractor.cs \
  src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
  samples/ZeroAlloc.Rest.AotSmoke/UnprocessableHandler.cs samples/ZeroAlloc.Rest.AotSmoke/Program.cs \
  docs/advanced.md tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs \
  tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorBodyTests.cs
git commit -F - <<'EOF'
feat: read the error response body into HttpError

A Result method now reads the body of a non-success response before the response is
disposed, and sets Body, ContentType and BodyTruncated. The new
ZeroAllocRestClient.MaxErrorBodyBytes sets the cap at compile time, 64 KiB by default;
0 emits no body-reading code at all. Only Status failures carry a body.

Refs #298

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: `GetRetryAfter`

**Files:**
- Create: `src/ZeroAlloc.Rest/HttpErrorExtensions.cs`
- Modify: `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt`
- Modify: `docs/advanced.md`, adding a subsection after `### The error body`
- Test: `tests/ZeroAlloc.Rest.Tests/HttpErrorExtensionsTests.cs` (create)

**Interfaces:**
- Consumes: `HttpError.Headers`. Task 1 made it include content headers; `Retry-After` is a response header either way.
- Produces: `public static TimeSpan? HttpErrorExtensions.GetRetryAfter(this HttpError error, TimeProvider? timeProvider = null)`. ZeroAlloc.Resilience sub-project B builds `DelayHint` on it.

- [ ] **Step 1: Write the failing tests**

Create `tests/ZeroAlloc.Rest.Tests/HttpErrorExtensionsTests.cs`:

```csharp
using System.Globalization;
using System.Net;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

public sealed class HttpErrorExtensionsTests
{
    private static readonly DateTimeOffset s_now = new(2015, 10, 21, 7, 27, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider s_clock = new(s_now);

    [Fact]
    public void DeltaSeconds_IsThatManySeconds()
        => Assert.Equal(TimeSpan.FromSeconds(120), WithRetryAfter("120").GetRetryAfter(s_clock));

    [Fact]
    public void DeltaSeconds_WithSurroundingWhitespace_IsAccepted()
        => Assert.Equal(TimeSpan.FromSeconds(5), WithRetryAfter("  5 ").GetRetryAfter(s_clock));

    [Theory]
    [InlineData("Wed, 21 Oct 2015 07:28:00 GMT")]      // IMF-fixdate
    [InlineData("Wednesday, 21-Oct-15 07:28:00 GMT")]  // obsolete RFC 850
    [InlineData("Wed Oct 21 07:28:00 2015")]           // obsolete asctime
    public void HttpDate_IsTheTimeUntilThatDate(string value)
        => Assert.Equal(TimeSpan.FromMinutes(1), WithRetryAfter(value).GetRetryAfter(s_clock));

    [Fact]
    public void HttpDate_InThePast_IsZero()
        => Assert.Equal(TimeSpan.Zero, WithRetryAfter("Wed, 21 Oct 2015 07:00:00 GMT").GetRetryAfter(s_clock));

    [Theory]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("soon")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Wed, 32 Oct 2015 07:28:00 GMT")]
    public void InvalidValue_IsNull(string value)
        => Assert.Null(WithRetryAfter(value).GetRetryAfter(s_clock));

    [Fact]
    public void AbsentHeader_IsNull()
    {
        var error = new HttpError(HttpStatusCode.TooManyRequests, new Dictionary<string, IReadOnlyList<string>>());

        Assert.Null(error.GetRetryAfter(s_clock));
    }

    [Fact]
    public void HandBuiltOrdinalDictionary_IsSearchedIgnoringCase()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["retry-after"] = new[] { "30" },
        };

        Assert.Equal(TimeSpan.FromSeconds(30), new HttpError(HttpStatusCode.ServiceUnavailable, headers).GetRetryAfter());
    }

    [Fact]
    public void WithoutATimeProvider_UsesTheSystemClock()
    {
        var inAnHour = DateTimeOffset.UtcNow.AddHours(1).ToString("r", CultureInfo.InvariantCulture);

        var delay = WithRetryAfter(inAnHour).GetRetryAfter();

        Assert.NotNull(delay);
        Assert.InRange(delay.Value, TimeSpan.FromMinutes(58), TimeSpan.FromMinutes(61));
    }

    private static HttpError WithRetryAfter(string value)
        => new(HttpStatusCode.TooManyRequests, new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Retry-After"] = new[] { value },
        });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj --filter "FullyQualifiedName~HttpErrorExtensionsTests"`
Expected: build FAIL, `CS1061: 'HttpError' does not contain a definition for 'GetRetryAfter'`.

- [ ] **Step 3: Write the extension**

Create `src/ZeroAlloc.Rest/HttpErrorExtensions.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ZeroAlloc.Rest;

/// <summary>Helpers for reading an <see cref="HttpError"/>.</summary>
public static class HttpErrorExtensions
{
    private const string RetryAfterHeader = "Retry-After";

    // RFC 9110 section 5.6.7: IMF-fixdate, then the two obsolete forms a recipient must accept.
    private static readonly string[] s_httpDateFormats =
    [
        "r",
        "dddd, dd'-'MMM'-'yy HH':'mm':'ss 'GMT'",
        "ddd MMM d HH':'mm':'ss yyyy",
    ];

    /// <summary>
    /// Reads the <c>Retry-After</c> header, in delta-seconds or HTTP-date form, as the time to wait
    /// before retrying.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="timeProvider">
    /// The clock an HTTP-date is measured against. Defaults to <see cref="TimeProvider.System"/>.
    /// It is not used for delta-seconds.
    /// </param>
    /// <returns>
    /// The time to wait; <see cref="TimeSpan.Zero"/> for a date in the past; or
    /// <see langword="null"/> when the header is absent or invalid. A negative delta-seconds value
    /// is invalid.
    /// </returns>
    public static TimeSpan? GetRetryAfter(this HttpError error, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (!TryGetFirstValue(error.Headers, out var raw))
            return null;

        var value = raw.AsSpan().Trim();
        if (value.IsEmpty)
            return null;

        // delta-seconds is digits only, so a sign or a fraction makes it invalid.
        if (char.IsAsciiDigit(value[0]))
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : null;
        }

        if (!DateTimeOffset.TryParseExact(value, s_httpDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowInnerWhite | DateTimeStyles.AssumeUniversal, out var date))
        {
            return null;
        }

        var remaining = date - (timeProvider ?? TimeProvider.System).GetUtcNow();
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private static bool TryGetFirstValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers, [NotNullWhen(true)] out string? value)
    {
        // The generated client's dictionary ignores case; an HttpError built by hand may not.
        if (!headers.TryGetValue(RetryAfterHeader, out var values))
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, RetryAfterHeader, StringComparison.OrdinalIgnoreCase))
                {
                    values = header.Value;
                    break;
                }
            }
        }

        value = values is { Count: > 0 } ? values[0] : null;
        return value is not null;
    }
}
```

Append to `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt`:

```text
static ZeroAlloc.Rest.HttpErrorExtensions.GetRetryAfter(this ZeroAlloc.Rest.HttpError! error, System.TimeProvider? timeProvider = null) -> System.TimeSpan?
ZeroAlloc.Rest.HttpErrorExtensions
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj --filter "FullyQualifiedName~HttpErrorExtensionsTests"`
Expected: PASS, 16 test cases.

- [ ] **Step 5: Document `GetRetryAfter`**

In `docs/advanced.md`, insert after the `### The error body` subsection, before `### What still throws`:

````markdown
### Retry-After

`GetRetryAfter` reads the `Retry-After` header of a 429 or 503 as the time to wait:

```csharp
if (result.IsFailure && result.Error.GetRetryAfter() is { } wait)
    await Task.Delay(wait, ct);
```

It accepts delta-seconds (`120`) and HTTP-dates (`Wed, 21 Oct 2015 07:28:00 GMT`, and the two obsolete forms). A date in the past gives `TimeSpan.Zero`. An absent or invalid header, including a negative number, gives `null`. Pass a `TimeProvider` to measure an HTTP-date against your own clock; it defaults to `TimeProvider.System`.
````

- [ ] **Step 6: Run the full suite**

Run the full-suite command. Expected: 0 warnings, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ZeroAlloc.Rest/HttpErrorExtensions.cs src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt \
  docs/advanced.md tests/ZeroAlloc.Rest.Tests/HttpErrorExtensionsTests.cs
git commit -F - <<'EOF'
feat: add HttpError.GetRetryAfter

Reads the Retry-After header in delta-seconds or HTTP-date form. A past date gives
TimeSpan.Zero; an absent or invalid header, including a negative delta, gives null. An
optional TimeProvider measures the HTTP-date form. ZeroAlloc.Resilience builds its
DelayHint on this.

Refs #298

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Checkpoint: ship PR 1 before starting Part 2

- [ ] Push `feat/rest-error-body` and open PR 1 against `main`. Title: `feat: carry the error response body on HttpError`. Body: a summary of Tasks 1 to 4, `Closes #298`, and a note that it is Part 1 of the design in `docs/plans/2026-09-26-error-body-and-mapper-design.md`. End the PR body with the `🤖 Generated with [Claude Code](https://claude.com/claude-code)` line and no session URL.
- [ ] Confirm CI is green: `build`, `api-compat` and `aot-smoke`. `api-compat` must pass with no change to `apicompat-suppressions.xml`.
- [ ] After the squash merge, confirm the release-please PR lists both `feat:` entries and the `fix:` entry. A green workflow does not mean the commits were counted.
- [ ] Do not merge the release-please PR yet if 2.1.0 should contain both parts; see design decision 16. That is the maintainer's call.
- [ ] Comment on #298 with what shipped: `HttpError.Body`, `ContentType`, `BodyTruncated`, `MaxErrorBodyBytes`, the content-headers fix, and `GetRetryAfter`.
- [ ] Start Part 2 in a fresh worktree off the updated `main`. It is not stacked on PR 1:

```bash
git -C C:/wt/rest-errors fetch origin
git -C C:/wt/rest-errors worktree add C:/wt/rest-mapper -b feat/rest-error-mapper origin/main
```

- [ ] In `C:\wt\rest-mapper`, confirm that Part 1 is present before going on:

```bash
grep -n "ReadErrorBodyAsync" src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt src/ZeroAlloc.Rest.Generator/ClientEmitter.cs
```

Expected: one hit in each file.

---

# Part 2: the error mapper (PR 2, #300)

Branch: `feat/rest-error-mapper` in `C:\wt\rest-mapper`, off `main` with PR 1 merged. Line numbers below refer to the files after Part 1. Where they may have moved, the anchor is the function name.

### Task 5: `[ErrorMapper]`, the model, and diagnostics ZRA001 to ZRA004 at real locations

**Files:**
- Create: `src/ZeroAlloc.Rest/IHttpErrorMapper.cs`
- Create: `src/ZeroAlloc.Rest/Attributes/ErrorMapperAttribute.cs`
- Modify: `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt`
- Create: `src/ZeroAlloc.Rest.Generator/DiagnosticDescriptors.cs`
- Create: `src/ZeroAlloc.Rest.Generator/Models/LocationInfo.cs`
- Create: `src/ZeroAlloc.Rest.Generator/Models/DiagnosticInfo.cs`
- Create: `src/ZeroAlloc.Rest.Generator/Models/ErrorMapperModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs` (`Extract`, `ExtractMethod`, the comment at lines 95-97, new resolution code)
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`:
  - remove the `s_conflictingBodyDescriptor` field, lines 10-16;
  - the start of `Emit`;
  - the ZRA001 report in `EmitMethod`.
- Modify: `src/ZeroAlloc.Rest.Generator/AnalyzerReleases.Unshipped.md`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs` (`ConflictingBodyAndFormBody_ReportsDiagnostic`)
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs` (create)

**Interfaces:**
- Consumes (Part 1): `ClientModel(..., bool IsPublic, int MaxErrorBodyBytes)`.
- Produces:
  - `public interface IHttpErrorMapper<TError> { TError Map(HttpError error); }` in `ZeroAlloc.Rest`.
  - `public sealed class ErrorMapperAttribute(Type mapperType) : Attribute`, with `Type MapperType`, in `ZeroAlloc.Rest.Attributes`.
  - `internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)`, with `Location ToLocation()` and `static LocationInfo From(Location)`.
  - `internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo Location, object[] MessageArgs)`, with `Diagnostic ToDiagnostic()`.
  - `internal sealed record ErrorMapperModel(string MapperTypeName, IReadOnlyList<string> ErrorTypeNames)`. Names are `global::`-qualified.
  - `MethodModel`, gaining `LocationInfo Location`, `string? ErrorTypeName`, `string? ErrorMapperTypeName`, `const string HttpErrorTypeName = "global::ZeroAlloc.Rest.HttpError"`, and `bool MapsError`.
  - `ClientModel`, gaining `IReadOnlyList<ErrorMapperModel> ErrorMappers` and `IReadOnlyList<DiagnosticInfo> Diagnostics`.
  - `internal static class DiagnosticDescriptors`, with `ConflictingBody` (ZRA001), `MissingErrorMapper` (ZRA002), `InvalidErrorMapper` (ZRA003) and `DuplicateErrorMapper` (ZRA004).

#### 5a. Fix: report ZRA001 at the method

- [ ] **Step 1: Write the failing test**

In `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`, `ConflictingBodyAndFormBody_ReportsDiagnostic`, replace the last two lines:

```csharp
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => d.Id == "ZRA001");
```

with:

```csharp
        var diagnostic = Assert.Single(result.Results[0].Diagnostics, d => d.Id == "ZRA001");
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Equal("BadAsync", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.Equal(7, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
```

`BadAsync` is on the eighth line of the raw string, which is line 7 zero-based: `using`, `using`, `namespace`, `[ZeroAllocRestClient]`, `interface`, `{`, `[Post]`, then `System.Threading.Tasks.Task<string> BadAsync(`.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "FullyQualifiedName~ConflictingBodyAndFormBody_ReportsDiagnostic"`
Expected: FAIL, `Assert.NotEqual() Failure: Values are equal`, because the location is `None`.

- [ ] **Step 3: Add `LocationInfo` and the descriptor class**

Create `src/ZeroAlloc.Rest.Generator/Models/LocationInfo.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Rest.Generator.Models;

// A diagnostic location the model can hold without keeping a SyntaxTree alive.
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    internal Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    internal static LocationInfo From(Location location)
        => new(location.SourceTree?.FilePath ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span);
}
```

Create `src/ZeroAlloc.Rest.Generator/DiagnosticDescriptors.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "ZeroAlloc.Rest.Generator";

    internal static readonly DiagnosticDescriptor ConflictingBody = new(
        id: "ZRA001",
        title: "Conflicting body attributes",
        messageFormat: "Method '{0}' has both [Body] and [FormBody] parameters; only one is allowed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
```

- [ ] **Step 4: Give the method model its location**

Replace `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs` with:

```csharp
using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record MethodModel(
    string Name,
    string HttpMethod,
    string Route,
    string ReturnTypeName,
    string? InnerTypeName,
    bool ReturnsResult,
    bool ReturnsVoid,
    IReadOnlyList<ParameterModel> Parameters,
    string? SerializerTypeName,
    IReadOnlyList<(string Name, string Value)> StaticHeaders,
    LocationInfo Location);
```

In `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`, `ExtractMethod`, replace the final `return new MethodModel(...)` statement, lines 128-131:

```csharp
        return new MethodModel(
            method.Name, httpMethod, route, returnTypeName,
            innerTypeName, returnsResult, returnsVoid,
            parameters, methodSerializer, staticHeaders.AsReadOnly());
```

with:

```csharp
        return new MethodModel(
            method.Name, httpMethod, route, returnTypeName,
            innerTypeName, returnsResult, returnsVoid,
            parameters, methodSerializer, staticHeaders.AsReadOnly(),
            LocationInfo.From(method.Locations[0]));
```

Also replace the comment at lines 95-97. It names ZRA002, which Task 5b now uses for something else:

```csharp
            // A method-level [Header] without a Value is intentionally ignored — there is nothing
            // to emit at compile time. Users who omit Value get no output and no diagnostic.
            // Consider adding ZRA002 here in future to warn about this silent no-op.
```

with:

```csharp
            // A method-level [Header] without a Value is intentionally ignored — there is nothing
            // to emit at compile time. Users who omit Value get no output and no diagnostic.
```

- [ ] **Step 5: Report ZRA001 at the method**

In `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`, delete the `s_conflictingBodyDescriptor` field, lines 10-16. In `EmitMethod`, replace:

```csharp
            ctx.ReportDiagnostic(Diagnostic.Create(s_conflictingBodyDescriptor, Location.None, method.Name));
```

with:

```csharp
            ctx.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ConflictingBody, method.Location.ToLocation(), method.Name));
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 7: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/Models/LocationInfo.cs src/ZeroAlloc.Rest.Generator/DiagnosticDescriptors.cs \
  src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs src/ZeroAlloc.Rest.Generator/ModelExtractor.cs \
  src/ZeroAlloc.Rest.Generator/ClientEmitter.cs tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs
git commit -F - <<'EOF'
fix: report ZRA001 at the offending method

ZRA001 was reported at Location.None, so the error had no file or line and the IDE could
not point at the method. It is now reported at the method name. The method model carries
its location for this, which the error-mapper diagnostics need as well.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

#### 5b. Feature: `[ErrorMapper]` and ZRA002 to ZRA004

- [ ] **Step 8: Write the failing diagnostic tests**

Create `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #300: Result<T, TError> with a user-defined error type, mapped by an [ErrorMapper].
public class GeneratorErrorMapperTests
{
    private static readonly MetadataReference[] References =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    private const string Types = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        public sealed record JevError(string Code);
        public sealed record OtherError(int Status);
        public sealed record SystemOneRequest(string Question);
        public sealed record SystemOneResponse(string Answer);
        public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new(error.StatusCode.ToString());
        }
        public sealed class AnotherJevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new("another");
        }
        public sealed class OtherErrorMapper : IHttpErrorMapper<OtherError>
        {
            public OtherError Map(HttpError error) => new((int)error.StatusCode);
        }
        public sealed class NotAMapper { }
        public sealed class PrivateCtorMapper : IHttpErrorMapper<JevError>
        {
            private PrivateCtorMapper() { }
            public JevError Map(HttpError error) => new("x");
        }
        public abstract class AbstractMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new("x");
        }

        """;

    [Fact]
    public void MissingMapper_ReportsZra002_AtTheMethod()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("AskAsync", At(source, diagnostic));
        Assert.Contains("MyApp.JevError", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void MapperForAnotherType_StillReportsZra002()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(OtherErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var diagnostic = Assert.Single(Run(source).GeneratorDiagnostics);
        Assert.Equal("ZRA002", diagnostic.Id);
    }

    [Fact]
    public void TypeImplementingNoMapperInterface_ReportsZra003_AtTheAttribute()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(NotAMapper))]
            public interface IJevApi
            {
                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA003", diagnostic.Id);
        Assert.Equal("ErrorMapper(typeof(NotAMapper))", At(source, diagnostic));
        Assert.Contains("implements no IHttpErrorMapper<TError>", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PrivateCtorMapper")]
    [InlineData("AbstractMapper")]
    public void MapperThatCannotBeConstructed_ReportsZra003_AndNotZra002(string mapper)
    {
        var source = Types + $$"""
            [ZeroAllocRestClient]
            [ErrorMapper(typeof({{mapper}}))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA003", diagnostic.Id);
        Assert.Equal($"ErrorMapper(typeof({mapper}))", At(source, diagnostic));
        Assert.Contains("public constructor", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoMappersForOneErrorType_ReportZra004_AtTheSecondAttribute()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(JevErrorMapper))]
            [ErrorMapper(typeof(AnotherJevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA004", diagnostic.Id);
        Assert.Equal("ErrorMapper(typeof(AnotherJevErrorMapper))", At(source, diagnostic));
        Assert.Contains("MyApp.JevErrorMapper", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("MyApp.AnotherJevErrorMapper", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void MapperWhoseErrorTypeNoMethodUses_IsAllowed()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(OtherErrorMapper))]
            public interface IJevApi
            {
                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            }
            """;

        Assert.Empty(Run(source).GeneratorDiagnostics);
    }

    [Fact]
    public void HttpErrorMethods_NeedNoMapper()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            public interface IJevApi
            {
                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
    }

    private static string At(string source, Diagnostic diagnostic)
        => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static GeneratorRun Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "Api.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var sources = driver.GetRunResult().Results[0].GeneratedSources
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString().Replace("\r\n", "\n"), StringComparer.Ordinal);
        var compileErrors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return new GeneratorRun(sources, generatorDiagnostics, compileErrors);
    }

    private sealed record GeneratorRun(
        Dictionary<string, string> Sources,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompileErrors);
}
```

- [ ] **Step 9: Run them to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorErrorMapperTests"`
Expected: FAIL. The test sources are strings, so the test project compiles, and the tests fail at run time:
- `Assert.Single() Failure: The collection was empty` for the ZRA tests;
- a `CS0246` for `IHttpErrorMapper<>` in the compile errors of `HttpErrorMethods_NeedNoMapper`, because the `Types` source references a type that does not exist yet.

- [ ] **Step 10: Add the runtime types**

Create `src/ZeroAlloc.Rest/IHttpErrorMapper.cs`:

```csharp
namespace ZeroAlloc.Rest;

/// <summary>
/// Turns an <see cref="HttpError"/> into the error type of methods that return
/// <c>Result&lt;T, TError&gt;</c>. Name it on the client interface with
/// <see cref="Attributes.ErrorMapperAttribute"/>.
/// </summary>
/// <typeparam name="TError">The error type the client's methods return.</typeparam>
/// <remarks>
/// A mapper should be total: return a <typeparamref name="TError"/> for every
/// <see cref="HttpError"/>, whatever its <see cref="HttpError.Kind"/>. An exception it throws
/// propagates to the caller unchanged, marks the request's span as failed, and is never passed to a
/// mapper again. The error body has already been read, and the response is already disposed.
/// </remarks>
public interface IHttpErrorMapper<TError>
{
    /// <summary>Maps one failure.</summary>
    /// <param name="error">The failure, with its body already read.</param>
    /// <returns>The error the method returns.</returns>
    TError Map(HttpError error);
}
```

Create `src/ZeroAlloc.Rest/Attributes/ErrorMapperAttribute.cs`:

```csharp
namespace ZeroAlloc.Rest.Attributes;

/// <summary>
/// Names the <see cref="IHttpErrorMapper{TError}"/> that turns an <see cref="HttpError"/> into the
/// error type of this interface's <c>Result&lt;T, TError&gt;</c> methods. Declare one per error type.
/// The generated <c>Add{I}</c> registers the mapper, and the client is built with the concrete
/// mapper type, so a host registration of <see cref="IHttpErrorMapper{TError}"/> never replaces it.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
public sealed class ErrorMapperAttribute(Type mapperType) : Attribute
{
    /// <summary>
    /// The mapper: a non-abstract class that implements <see cref="IHttpErrorMapper{TError}"/> and has
    /// a public constructor.
    /// </summary>
    public Type MapperType { get; } = mapperType;
}
```

Append to `src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt`:

```text
ZeroAlloc.Rest.Attributes.ErrorMapperAttribute
ZeroAlloc.Rest.Attributes.ErrorMapperAttribute.ErrorMapperAttribute(System.Type! mapperType) -> void
ZeroAlloc.Rest.Attributes.ErrorMapperAttribute.MapperType.get -> System.Type!
ZeroAlloc.Rest.IHttpErrorMapper<TError>
ZeroAlloc.Rest.IHttpErrorMapper<TError>.Map(ZeroAlloc.Rest.HttpError! error) -> TError
```

- [ ] **Step 11: Add the descriptors and the release rows**

Add these to `DiagnosticDescriptors`, after `ConflictingBody`:

```csharp
    internal static readonly DiagnosticDescriptor MissingErrorMapper = new(
        id: "ZRA002",
        title: "No error mapper for a Result error type",
        messageFormat: "Method '{0}' returns a Result with error type '{1}', but no [ErrorMapper] on the interface implements IHttpErrorMapper<{1}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidErrorMapper = new(
        id: "ZRA003",
        title: "Invalid error mapper type",
        messageFormat: "'{0}' cannot be an error mapper: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateErrorMapper = new(
        id: "ZRA004",
        title: "Duplicate error mapper",
        messageFormat: "'{1}' maps '{2}', which '{0}' already maps; declare one [ErrorMapper] per error type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
```

Replace `src/ZeroAlloc.Rest.Generator/AnalyzerReleases.Unshipped.md` with:

```markdown
; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category                 | Severity | Notes
--------|--------------------------|----------|-----------------------------------------
ZRA002  | ZeroAlloc.Rest.Generator | Error    | No error mapper for a Result error type
ZRA003  | ZeroAlloc.Rest.Generator | Error    | Invalid error mapper type
ZRA004  | ZeroAlloc.Rest.Generator | Error    | Duplicate error mapper
```

- [ ] **Step 12: Extend the models**

Create `src/ZeroAlloc.Rest.Generator/Models/DiagnosticInfo.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator.Models;

// A diagnostic found while extracting the model, reported when the client is emitted.
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo Location, object[] MessageArgs)
{
    internal Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location.ToLocation(), MessageArgs);
}
```

Create `src/ZeroAlloc.Rest.Generator/Models/ErrorMapperModel.cs`:

```csharp
using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

// A usable [ErrorMapper]: its global::-qualified type, and the error types it maps for this interface.
internal sealed record ErrorMapperModel(string MapperTypeName, IReadOnlyList<string> ErrorTypeNames);
```

Replace `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs` with:

```csharp
using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record MethodModel(
    string Name,
    string HttpMethod,
    string Route,
    string ReturnTypeName,
    string? InnerTypeName,
    bool ReturnsResult,
    bool ReturnsVoid,
    IReadOnlyList<ParameterModel> Parameters,
    string? SerializerTypeName,
    IReadOnlyList<(string Name, string Value)> StaticHeaders,
    LocationInfo Location,
    string? ErrorTypeName,
    string? ErrorMapperTypeName)
{
    internal const string HttpErrorTypeName = "global::ZeroAlloc.Rest.HttpError";

    // Result<T, E> with E other than HttpError: every failure goes through an IHttpErrorMapper<E>.
    // ErrorMapperTypeName is null when no usable [ErrorMapper] maps E.
    internal bool MapsError => ReturnsResult && ErrorTypeName is not null && ErrorTypeName != HttpErrorTypeName;
}
```

In `src/ZeroAlloc.Rest.Generator/Models/ClientModel.cs`, change the record header to:

```csharp
internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName,
    bool IsPublic,
    int MaxErrorBodyBytes,
    IReadOnlyList<ErrorMapperModel> ErrorMappers,
    IReadOnlyList<DiagnosticInfo> Diagnostics)
```

Add this method after `GetOverrideSerializerTypes`:

```csharp
    // The error types methods use that have a usable mapper, each with that mapper, in order of first
    // use. Each gets one constructor parameter. A mapper for an error type no method uses is only
    // registered.
    internal IReadOnlyList<(string ErrorTypeName, string MapperTypeName)> GetUsedErrorMappings()
    {
        var result = new List<(string ErrorTypeName, string MapperTypeName)>();
        foreach (var m in Methods)
        {
            if (!m.MapsError || m.ErrorMapperTypeName is null)
                continue;
            var seen = false;
            foreach (var (errorType, _) in result)
            {
                if (errorType == m.ErrorTypeName) { seen = true; break; }
            }
            if (!seen)
                result.Add((m.ErrorTypeName!, m.ErrorMapperTypeName));
        }
        return result.AsReadOnly();
    }
```

- [ ] **Step 13: Resolve mappers and collect diagnostics in the extractor**

In `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`, add after `SerializerAttr`, line 19:

```csharp
    private const string ErrorMapperAttr = "ZeroAlloc.Rest.Attributes.ErrorMapperAttribute";
    private const string ErrorMapperOpenType = "ZeroAlloc.Rest.IHttpErrorMapper<TError>";
    private const string NotConstructibleReason = "it must be a closed, non-abstract class with a public constructor";
    private const string NoMapperInterfaceReason = "it implements no IHttpErrorMapper<TError> interface";
```

In `Extract`, replace the lines from `var clientSerializer = GetSerializerType(interfaceSymbol);` to the end of the method with:

```csharp
        var clientSerializer = GetSerializerType(interfaceSymbol);
        var diagnostics = new List<DiagnosticInfo>();
        var mappers = ResolveErrorMappers(interfaceSymbol, diagnostics, ct);

        var methods = new List<MethodModel>();
        foreach (var member in interfaceSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method) continue;
            var methodModel = ExtractMethod(method, mappers, diagnostics);
            if (methodModel is not null) methods.Add(methodModel);
        }

        return new ClientModel(ns, interfaceName, className, methods.AsReadOnly(), clientSerializer,
            IsEffectivelyPublic(interfaceSymbol), GetMaxErrorBodyBytes(ctx),
            mappers.Valid.AsReadOnly(), diagnostics.AsReadOnly());
    }

    private sealed class ErrorMapperResolution
    {
        internal List<ErrorMapperModel> Valid { get; } = new();

        // Error type -> the usable mapper type that maps it. Both names are global::-qualified.
        internal Dictionary<string, string> MapperByError { get; } = new(System.StringComparer.Ordinal);

        // Error types claimed by a mapper that ZRA003 rejected. A method using one is not also ZRA002.
        internal HashSet<string> ClaimedByInvalidMapper { get; } = new(System.StringComparer.Ordinal);
    }

    private static ErrorMapperResolution ResolveErrorMappers(
        INamedTypeSymbol interfaceSymbol, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var resolution = new ErrorMapperResolution();
        // Error type -> display name of the mapper that claimed it first, valid or not, for ZRA004.
        var ownerByError = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var attr in interfaceSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != ErrorMapperAttr) continue;
            if (attr.ConstructorArguments.Length == 0
                || attr.ConstructorArguments[0].Value is not INamedTypeSymbol mapperType)
                continue;

            var location = LocationInfo.From(
                attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? interfaceSymbol.Locations[0]);
            var mapperDisplay = mapperType.ToDisplayString();

            if (mapperType.IsUnboundGenericType)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidErrorMapper, location,
                    new object[] { mapperDisplay, NotConstructibleReason }));
                continue;
            }

            var errorTypes = new List<ITypeSymbol>();
            foreach (var iface in mapperType.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == ErrorMapperOpenType)
                    errorTypes.Add(iface.TypeArguments[0]);
            }

            if (errorTypes.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidErrorMapper, location,
                    new object[] { mapperDisplay, NoMapperInterfaceReason }));
                continue;
            }

            var constructible = IsConstructible(mapperType);
            if (!constructible)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidErrorMapper, location,
                    new object[] { mapperDisplay, NotConstructibleReason }));
            }

            var mapperName = mapperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var mapped = new List<string>();
            foreach (var errorType in errorTypes)
            {
                var errorName = errorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (ownerByError.TryGetValue(errorName, out var owner))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateErrorMapper, location,
                        new object[] { owner, mapperDisplay, errorType.ToDisplayString() }));
                    continue;
                }

                ownerByError[errorName] = mapperDisplay;
                if (constructible)
                {
                    resolution.MapperByError[errorName] = mapperName;
                    mapped.Add(errorName);
                }
                else
                {
                    resolution.ClaimedByInvalidMapper.Add(errorName);
                }
            }

            if (mapped.Count > 0)
                resolution.Valid.Add(new ErrorMapperModel(mapperName, mapped.AsReadOnly()));
        }

        return resolution;
    }

    // TryAddSingleton<TMapper> needs a class, and the container needs a public constructor.
    private static bool IsConstructible(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
            return false;
        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public)
                return true;
        }
        return false;
    }
```

Change the `ExtractMethod` signature, line 63:

```csharp
    private static MethodModel? ExtractMethod(IMethodSymbol method, ErrorMapperResolution mappers, List<DiagnosticInfo> diagnostics)
```

In `ExtractMethod`, replace the block from `bool returnsVoid = false;` through the closing brace of the `else { returnsVoid = true; }` block, lines 107-123 on 2.0.1, with:

```csharp
        bool returnsVoid = false;
        bool returnsResult = false;
        string? innerTypeName = null;
        string? errorTypeName = null;
        string? errorMapperTypeName = null;
        string returnTypeName = returnType.ToDisplayString();
        var location = LocationInfo.From(method.Locations[0]);

        if (returnType.TypeArguments.Length == 1)
        {
            var inner = returnType.TypeArguments[0] as INamedTypeSymbol;
            innerTypeName = inner?.ToDisplayString();
            returnsResult = inner?.OriginalDefinition.ToDisplayString() == ResultOpenType;
            if (returnsResult && inner?.TypeArguments.Length == 2)
            {
                innerTypeName = inner.TypeArguments[0].ToDisplayString();
                var errorType = inner.TypeArguments[1];
                errorTypeName = errorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // An unresolved error type already has its own compiler error.
                if (errorTypeName != MethodModel.HttpErrorTypeName && errorType.TypeKind != TypeKind.Error)
                {
                    if (mappers.MapperByError.TryGetValue(errorTypeName, out var mapper))
                        errorMapperTypeName = mapper;
                    else if (!mappers.ClaimedByInvalidMapper.Contains(errorTypeName))
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MissingErrorMapper, location,
                            new object[] { method.Name, errorType.ToDisplayString() }));
                }
            }
        }
        else
        {
            returnsVoid = true;
        }
```

Replace the final `return new MethodModel(...)` with:

```csharp
        return new MethodModel(
            method.Name, httpMethod, route, returnTypeName,
            innerTypeName, returnsResult, returnsVoid,
            parameters, methodSerializer, staticHeaders.AsReadOnly(),
            location, errorTypeName, errorMapperTypeName);
```

- [ ] **Step 14: Report the model's diagnostics**

In `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`, make these the first statements of `Emit`:

```csharp
        foreach (var diagnostic in model.Diagnostics)
            ctx.ReportDiagnostic(diagnostic.ToDiagnostic());

```

- [ ] **Step 15: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj`
Expected: PASS, all tests.

A mapped method with a usable mapper still emits `Result<T, HttpError>` and does not compile yet. No test in this task compiles one; Task 6 makes it compile.

- [ ] **Step 16: Run the full suite**

Run the full-suite command. Expected: 0 warnings, all tests pass. `RS2008` and its siblings check the release file against the descriptors, so a mismatch fails the build.

- [ ] **Step 17: Commit**

```bash
git add src/ZeroAlloc.Rest/IHttpErrorMapper.cs src/ZeroAlloc.Rest/Attributes/ErrorMapperAttribute.cs \
  src/ZeroAlloc.Rest/PublicAPI.Unshipped.txt src/ZeroAlloc.Rest.Generator/DiagnosticDescriptors.cs \
  src/ZeroAlloc.Rest.Generator/AnalyzerReleases.Unshipped.md src/ZeroAlloc.Rest.Generator/Models \
  src/ZeroAlloc.Rest.Generator/ModelExtractor.cs src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
  tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs
git commit -F - <<'EOF'
feat: add [ErrorMapper] with diagnostics ZRA002 to ZRA004

IHttpErrorMapper<TError> and the interface-level [ErrorMapper] attribute name the mapper
for a Result<T, TError> method. The generator now carries the error type through its model
and resolves each mapper at compile time. All three new diagnostics are errors, reported at
the method or attribute:

- ZRA002: no mapper for a method's error type
- ZRA003: the mapper type is not a usable mapper
- ZRA004: two mappers for one error type

Refs #300

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Emit the mapped client: constructor, failure sites, and a throwing stub

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`:
  - `Emit`, the field map, constructor and method loop;
  - `EmitGeneratedClientMembers`, the `Create` arguments;
  - `EmitMethod`;
  - `EmitSendAndResponse`;
  - `EmitResultCatches`;
  - `EmitResponseHandling`;
  - `ResultTypeName`;
  - the field-name helper.

  It also gains `EmitFailure`, `EmitMapping`, `EmitUnmappedStub` and `EmitRecordMapperFailure`.
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs` (field names)
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs`
- Test: `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorMapperTests.cs` (create)

**Interfaces:**
- Consumes (Task 5): `MethodModel.MapsError`, `ErrorTypeName`, `ErrorMapperTypeName`, `ClientModel.GetUsedErrorMappings()` and `ClientModel.ErrorMappers`.
- Produces: generated clients whose public constructor is `(HttpClient httpClient, IRestSerializer serializer, <override serializers>..., IHttpErrorMapper<E1> e1Mapper, ...)`, with `Create` resolving each concrete mapper type through `GetRequiredService<TMapper>`. Task 7 registers those types in `AddSerializers`.

#### 6a. Fix: valid, distinct generated field names

- [ ] **Step 1: Write the failing tests**

Add to `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`, after `MethodOverrideSerializer_EmitsAdditionalConstructorParameter`, which ends around line 228:

```csharp
    private const string SerializerStub = """
        public sealed class {0} : ZeroAlloc.Rest.IRestSerializer
        {{
            public string ContentType => "application/x-test";
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public System.Threading.Tasks.ValueTask<T?> DeserializeAsync<T>(System.IO.Stream stream, System.Threading.CancellationToken ct = default)
                => System.Threading.Tasks.ValueTask.FromResult<T?>(default);
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public System.Threading.Tasks.ValueTask SerializeAsync<T>(System.IO.Stream stream, T value, System.Threading.CancellationToken ct = default)
                => System.Threading.Tasks.ValueTask.CompletedTask;
        }}
        """;

    [Fact]
    public void GenericMethodSerializer_GetsAValidFieldName_AndCompiles()
    {
        var source = "#nullable enable\nusing ZeroAlloc.Rest.Attributes;\nnamespace MyApp;\n"
            + string.Format(System.Globalization.CultureInfo.InvariantCulture, SerializerStub, "Wrapper<TPayload>")
            + """

            public sealed class Payload { }
            [ZeroAllocRestClient]
            public interface IUploadApi
            {
                [Post("/upload")]
                [Serializer(typeof(Wrapper<Payload>))]
                System.Threading.Tasks.Task UploadAsync([Body] string data, System.Threading.CancellationToken ct = default);
            }
            """;

        var (output, errors) = CompileGenerated(source, "IUploadApi.g.cs");

        Assert.Empty(errors);
        Assert.Contains("private readonly ZeroAlloc.Rest.IRestSerializer _wrapper;", output);
    }

    [Fact]
    public void MethodSerializerNamedLikeAFixedField_GetsADistinctName_AndCompiles()
    {
        var source = "#nullable enable\nusing ZeroAlloc.Rest.Attributes;\nnamespace MyApp;\n"
            + string.Format(System.Globalization.CultureInfo.InvariantCulture, SerializerStub, "Serializer")
            + """

            [ZeroAllocRestClient]
            public interface IUploadApi
            {
                [Post("/upload")]
                [Serializer(typeof(Serializer))]
                System.Threading.Tasks.Task UploadAsync([Body] string data, System.Threading.CancellationToken ct = default);
            }
            """;

        var (output, errors) = CompileGenerated(source, "IUploadApi.g.cs");

        Assert.Empty(errors);
        Assert.Contains("private readonly ZeroAlloc.Rest.IRestSerializer _serializer2;", output);
    }

    private static (string Output, List<Diagnostic> Errors) CompileGenerated(string source, string hintName)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            Basic.Reference.Assemblies.Net100.References.All
                .Append(MetadataReference.CreateFromFile(AttributesAssembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var file = driver.GetRunResult().Results[0].GeneratedSources.First(f => f.HintName == hintName);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return (file.SourceText.ToString(), errors);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "FullyQualifiedName~GenericMethodSerializer_GetsAValidFieldName_AndCompiles|FullyQualifiedName~MethodSerializerNamedLikeAFixedField_GetsADistinctName_AndCompiles"`
Expected: FAIL.
- The generic case gives `Assert.Empty() Failure`, with a CS1519 or CS1002 syntax error for the field `_payload>`.
- The fixed-field case gives `CS0102: The type 'UploadApiClient' already contains a definition for '_serializer'`.

- [ ] **Step 3: Fix the field-name helper**

In `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`, replace the doc comment, `StripGlobal` and `GetSerializerFieldName`, lines 514-535 on 2.0.1, with:

```csharp
    private static string StripGlobal(string typeName)
        => typeName.StartsWith("global::", System.StringComparison.Ordinal) ? typeName.Substring("global::".Length) : typeName;

    /// <summary>
    /// Derives a collision-free private field name from a type's name plus a suffix, deduplicating
    /// against already-assigned field names. Generic arguments are dropped.
    /// "global::MyApp.OverrideSerializer" gives "_overrideSerializer";
    /// "global::MyApp.JevError" with suffix "Mapper" gives "_jevErrorMapper";
    /// "global::MyApp.Wrapper&lt;global::MyApp.Foo&gt;" gives "_wrapper".
    /// Collisions get a numeric suffix: _overrideSerializer2, and so on.
    /// </summary>
    private static string GetFieldName(string fullTypeName, string suffix, List<string> existingFieldNames)
    {
        var name = StripGlobal(fullTypeName);
        var genericStart = name.IndexOf('<');
        if (genericStart >= 0)
            name = name.Substring(0, genericStart);
        var simpleName = name.Substring(name.LastIndexOf('.') + 1) + suffix;
        if (simpleName.Length == 0) simpleName = "Serializer"; // defensive fallback
        var candidate = "_" + char.ToLowerInvariant(simpleName[0]) + simpleName.Substring(1);
        if (!existingFieldNames.Contains(candidate)) return candidate;
        // Collision — append index
        var i = 2;
        while (existingFieldNames.Contains(candidate + i)) i++;
        return candidate + i;
    }
```

In `Emit`, replace the serializer field map, lines 23-31 on 2.0.1, just after the diagnostics loop from Task 5:

```csharp
        // Build a collision-free FQN → field name map
        var serializerFieldMap = new Dictionary<string, string>();
        var usedFieldNames = new List<string>();
        foreach (var st in overrideSerializers)
        {
            var fieldName = GetSerializerFieldName(st, usedFieldNames);
            usedFieldNames.Add(fieldName);
            serializerFieldMap[st] = fieldName;
        }
```

with:

```csharp
        // Field names are unique across the fixed fields and every injected dependency.
        var usedFieldNames = new List<string> { "_httpClient", "_serializer" };
        var serializerFieldMap = new Dictionary<string, string>();
        foreach (var st in overrideSerializers)
        {
            var fieldName = GetFieldName(st, string.Empty, usedFieldNames);
            usedFieldNames.Add(fieldName);
            serializerFieldMap[st] = fieldName;
        }
```

- [ ] **Step 4: Run the generator tests**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj`
Expected: PASS, all tests. The existing `_overrideSerializer` and `uploadSerializer` assertions are unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs
git commit -F - <<'EOF'
fix: generate valid, distinct field names for injected serializers

A generic method-level serializer type produced a field name containing angle brackets, and
a serializer type named Serializer or HttpClient collided with the client's fixed fields.
Both failed to compile. Field names now drop generic arguments, avoid the fixed fields, and
lower-case with the invariant culture.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

#### 6b. Feature: the mapped client

- [ ] **Step 6: Write the failing generator tests**

Add to `GeneratorErrorMapperTests`, after `HttpErrorMethods_NeedNoMapper`:

```csharp
    private const string JevApi = """
        [ZeroAllocRestClient]
        [ErrorMapper(typeof(JevErrorMapper))]
        public interface IJevApi
        {
            [Post("v1/systemone")]
            ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
        }
        """;

    [Fact]
    public void MappedMethod_ConstructorTakesTheMapperInterface_CreateResolvesTheConcreteMapper()
    {
        var run = Run(Types + JevApi);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        var client = run.Sources["IJevApi.g.cs"];
        Assert.Contains("private readonly global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> _jevErrorMapper;", client);
        Assert.Contains(
            "public JevApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> jevErrorMapper)",
            client);
        Assert.Contains("_jevErrorMapper = jevErrorMapper;", client);
        Assert.Contains(
            "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::MyApp.JevErrorMapper>(services));",
            client);
        Assert.DoesNotContain("GetRequiredService<global::ZeroAlloc.Rest.IHttpErrorMapper", client);
    }

    [Fact]
    public void MappedMethod_MapsAllFourFailureKinds_Once_AfterTheTry()
    {
        var client = Run(Types + JevApi).Sources["IJevApi.g.cs"];

        Assert.Contains("global::ZeroAlloc.Rest.HttpError __httpError;", client);
        Assert.Contains(
            "__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null, __errorBody.Body, __errorBody.Truncated);",
            client);
        Assert.Contains("__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Deserialization, response, __ex);", client);
        Assert.Contains("__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Timeout, null, __ex);", client);
        Assert.Contains("__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Transport, null, __ex);", client);
        Assert.Contains(
            "return ZeroAlloc.Results.Result<MyApp.SystemOneResponse, global::MyApp.JevError>.Failure(_jevErrorMapper.Map(__httpError));",
            client);
        Assert.Contains(
            "return ZeroAlloc.Results.Result<MyApp.SystemOneResponse, global::MyApp.JevError>.Success(content);",
            client);
        Assert.Equal(1, CountOccurrences(client, ".Map("));
        Assert.Contains("__RecordMapperFailure(__activity, __ex);", client);
        Assert.Contains("private static void __RecordMapperFailure(", client);
    }

    [Fact]
    public void HttpErrorMethod_IsEmittedExactlyAsWithoutAMapper()
    {
        const string HttpErrorMethod = """
                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            """;
        var plain = Types + """
            [ZeroAllocRestClient]
            public interface IJevApi
            {
            """ + "\n" + HttpErrorMethod + "\n}";
        var mapped = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(JevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            """ + "\n" + HttpErrorMethod + "\n}";

        var plainRun = Run(plain);
        var mappedRun = Run(mapped);

        Assert.Empty(plainRun.CompileErrors);
        Assert.Empty(mappedRun.CompileErrors);
        Assert.Equal(
            MethodText(plainRun.Sources["IJevApi.g.cs"], "PingAsync"),
            MethodText(mappedRun.Sources["IJevApi.g.cs"], "PingAsync"));
        Assert.DoesNotContain("__httpError", MethodText(mappedRun.Sources["IJevApi.g.cs"], "PingAsync"));
    }

    [Fact]
    public void TwoErrorTypes_GetTwoParameters_InOrderOfFirstUse()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(OtherErrorMapper))]
            [ErrorMapper(typeof(JevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);

                [Get("v1/status")]
                Task<Result<string, OtherError>> StatusAsync(CancellationToken ct = default);

                [Get("v1/again")]
                Task<Result<string, JevError>> AgainAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        var client = run.Sources["IJevApi.g.cs"];
        Assert.Contains(
            "ZeroAlloc.Rest.IRestSerializer serializer, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> jevErrorMapper, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.OtherError> otherErrorMapper)",
            client);
        Assert.Equal(2, CountOccurrences(client, "_jevErrorMapper.Map(__httpError)"));
        Assert.Equal(1, CountOccurrences(client, "_otherErrorMapper.Map(__httpError)"));
    }

    [Fact]
    public void MapperForAnUnusedErrorType_AddsNoConstructorParameter()
    {
        var source = Types + """
            internal sealed record HiddenError(int Status);
            internal sealed class HiddenErrorMapper : IHttpErrorMapper<HiddenError>
            {
                public HiddenError Map(HttpError error) => new((int)error.StatusCode);
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(HiddenErrorMapper))]
            public interface IJevApi
            {
                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Contains(
            "public JevApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer)",
            run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void InternalInterface_WithInternalMapperAndErrorType_Compiles()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;
            using ZeroAlloc.Rest.Attributes;
            using ZeroAlloc.Results;
            namespace MyApp;
            internal sealed record JevError(string Code);
            internal sealed record SystemOneRequest(string Question);
            internal sealed record SystemOneResponse(string Answer);
            internal sealed class JevErrorMapper : IHttpErrorMapper<JevError>
            {
                public JevError Map(HttpError error) => new(error.StatusCode.ToString());
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(JevErrorMapper))]
            internal interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Contains("internal sealed partial class JevApiClient", run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void UnmappedMethod_ReportsOnlyZra002_AndTheClientStillCompiles()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);

                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Equal("ZRA002", Assert.Single(run.GeneratorDiagnostics).Id);
        Assert.Empty(run.CompileErrors);
        Assert.Contains("=> throw new global::System.NotSupportedException(", run.Sources["IJevApi.g.cs"]);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string MethodText(string generated, string methodName)
    {
        var signature = generated.IndexOf($" {methodName}(", StringComparison.Ordinal);
        var start = generated.LastIndexOf("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Trimming\"", signature, StringComparison.Ordinal);
        var end = generated.IndexOf("\n    }\n", signature, StringComparison.Ordinal);
        return generated.Substring(start, end - start);
    }
```

- [ ] **Step 7: Run them to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorErrorMapperTests"`
Expected: FAIL.
- The mapped cases fail on `Assert.Empty(run.CompileErrors)` with `CS0029: Cannot implicitly convert type 'ZeroAlloc.Results.Result<MyApp.SystemOneResponse, ZeroAlloc.Rest.HttpError>'`.
- `UnmappedMethod_ReportsOnlyZra002_AndTheClientStillCompiles` fails the same way.
- The Task 5 diagnostic tests still pass.

- [ ] **Step 8: Emit the mapper fields, constructor parameters and `Create` arguments**

In `ClientEmitter.Emit`, after the serializer field map from step 3, add:

```csharp
        var errorMappings = model.GetUsedErrorMappings();
        var errorMapperFieldMap = new Dictionary<string, string>();
        foreach (var (errorType, _) in errorMappings)
        {
            var fieldName = GetFieldName(errorType, "Mapper", usedFieldNames);
            usedFieldNames.Add(fieldName);
            errorMapperFieldMap[errorType] = fieldName;
        }
```

After the loop that declares the override-serializer fields, the one ending `sb.AppendLine($"    private readonly ZeroAlloc.Rest.IRestSerializer {serializerFieldMap[st]};");`, add:

```csharp
        // A mapper is taken as IHttpErrorMapper<E>, so an internal mapper type never appears in the
        // constructor. E is as accessible as the method that returns it. Create resolves the concrete
        // mapper type.
        foreach (var (errorType, _) in errorMappings)
            sb.AppendLine($"    private readonly global::ZeroAlloc.Rest.IHttpErrorMapper<{errorType}> {errorMapperFieldMap[errorType]};");
```

After the loop that adds the override-serializer constructor parameters, add:

```csharp
        foreach (var (errorType, _) in errorMappings)
        {
            var fieldName = errorMapperFieldMap[errorType];
            ctorParams.Add($"global::ZeroAlloc.Rest.IHttpErrorMapper<{errorType}> {fieldName.Substring(1)}");
        }
```

After the loop that assigns the override-serializer fields in the constructor body, before `sb.AppendLine("    }");`, add:

```csharp
        foreach (var (errorType, _) in errorMappings)
        {
            var fieldName = errorMapperFieldMap[errorType];
            sb.AppendLine($"        {fieldName} = {fieldName.Substring(1)};");
        }
```

Change the call `EmitGeneratedClientMembers(sb, model, overrideSerializers);` to:

```csharp
        EmitGeneratedClientMembers(sb, model, overrideSerializers, errorMappings);
```

Change the method loop to:

```csharp
        foreach (var method in model.Methods)
            EmitMethod(ctx, sb, model.InterfaceName, method, serializerFieldMap, errorMapperFieldMap, model.MaxErrorBodyBytes);
```

After `if (anyReturnsResult) EmitCreateHttpError(sb);`, add:

```csharp
        if (errorMappings.Count > 0)
            EmitRecordMapperFailure(sb);
```

In `EmitGeneratedClientMembers`, change the signature and extend the `Create` arguments:

```csharp
    private static void EmitGeneratedClientMembers(StringBuilder sb, ClientModel model, IReadOnlyList<string> overrideSerializers, IReadOnlyList<(string ErrorTypeName, string MapperTypeName)> errorMappings)
```

After `foreach (var st in overrideSerializers) ctorArgs.Add(...);`, add:

```csharp
        // The concrete mapper type, never IHttpErrorMapper<E>: a host registration of the interface
        // can never replace a library's mapper.
        foreach (var (_, mapperType) in errorMappings)
            ctorArgs.Add($"{GetRequired}<{mapperType}>(services)");
```

For `IJevApi`, the emitted members are:

```csharp
    private readonly global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> _jevErrorMapper;

    public JevApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> jevErrorMapper)
    {
        _httpClient = httpClient;
        _serializer = serializer;
        _jevErrorMapper = jevErrorMapper;
    }

    static JevApiClient global::ZeroAlloc.Rest.IGeneratedRestClient<JevApiClient>.Create(global::System.Net.Http.HttpClient httpClient, global::System.IServiceProvider services)
        => new JevApiClient(
            httpClient,
            global::ZeroAlloc.Rest.RestSerializerServiceProviderExtensions.GetRequiredRestSerializer<IJevApi>(services),
            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::MyApp.JevErrorMapper>(services));
```

- [ ] **Step 9: Emit the mapped failure sites**

Change `ResultTypeName` to:

```csharp
    private static string ResultTypeName(MethodModel method)
        => method.MapsError
            ? $"ZeroAlloc.Results.Result<{method.InnerTypeName}, {method.ErrorTypeName}>"
            : $"ZeroAlloc.Results.Result<{method.InnerTypeName}, ZeroAlloc.Rest.HttpError>";
```

Add these helpers after `ResultTypeName`:

```csharp
    // How a failure site ends. An HttpError method returns the error from the site, exactly as
    // before. A mapped method stores it in __httpError and maps it once, after the method's try.
    private static void EmitFailure(StringBuilder sb, string indent, MethodModel method, string createCall)
    {
        if (method.MapsError)
        {
            sb.AppendLine($"{indent}__httpError = {createCall};");
        }
        else
        {
            sb.AppendLine($"{indent}return {ResultTypeName(method)}.Failure(");
            sb.AppendLine($"{indent}    {createCall});");
        }
    }

    // The one mapping site. It sits outside the method's try, so a mapper's exception never reaches
    // the Result catches and is never mapped again. The exception reaches the caller unchanged,
    // and the span is marked failed.
    private static void EmitMapping(StringBuilder sb, MethodModel method, string errorMapperField)
    {
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine($"            return {ResultTypeName(method)}.Failure({errorMapperField}.Map(__httpError));");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.Exception __ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            __RecordMapperFailure(__activity, __ex);");
        sb.AppendLine("            throw;");
        sb.AppendLine("        }");
    }

    // The request's duration is already recorded when a mapper runs, so a mapper failure only marks
    // the span.
    private static void EmitRecordMapperFailure(StringBuilder sb)
    {
        sb.AppendLine("    private static void __RecordMapperFailure(global::System.Diagnostics.Activity? activity, global::System.Exception exception)");
        sb.AppendLine("        => activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, exception.Message);");
        sb.AppendLine();
    }

    // ZRA002 or ZRA003 is already reported for this method. A throwing body keeps the rest of the
    // client compiling, so that diagnostic is the only error the user sees.
    private static void EmitUnmappedStub(StringBuilder sb, MethodModel method)
    {
        var errorType = StripGlobal(method.ErrorTypeName!);
        sb.AppendLine($"    public {method.ReturnTypeName} {method.Name}({BuildParamList(method.Parameters)})");
        sb.AppendLine($"        => throw new global::System.NotSupportedException(\"No usable [ErrorMapper] maps {errorType}; see the ZeroAlloc.Rest diagnostic reported for this method.\");");
        sb.AppendLine();
    }
```

Change `EmitMethod`'s signature to:

```csharp
    private static void EmitMethod(SourceProductionContext ctx, StringBuilder sb, string interfaceName, MethodModel method, IReadOnlyDictionary<string, string> serializerFieldMap, IReadOnlyDictionary<string, string> errorMapperFieldMap, int maxErrorBodyBytes)
```

In `EmitMethod`, after the `serializerExpr` assignment and before the `UnconditionalSuppressMessage` lines, insert:

```csharp
        string? errorMapperField = null;
        if (method.MapsError && !errorMapperFieldMap.TryGetValue(method.ErrorTypeName!, out errorMapperField))
        {
            EmitUnmappedStub(sb, method);
            return;
        }
```

Change the `EmitSendAndResponse` call in `EmitMethod` to:

```csharp
        EmitSendAndResponse(sb, method, ctParam?.Name, serializerExpr, maxErrorBodyBytes, errorMapperField);
```

Change `EmitSendAndResponse`: its signature, the declaration before `try`, and the mapping after the catches:

```csharp
    private static void EmitSendAndResponse(StringBuilder sb, MethodModel method, string? callerToken, string serializerExpr, int maxErrorBodyBytes, string? errorMapperField)
    {
        var ctArg = callerToken ?? "default";
        if (errorMapperField != null)
            sb.AppendLine("        global::ZeroAlloc.Rest.HttpError __httpError;");
        sb.AppendLine("        try");
```

The body in between stays unchanged. After its final line, `sb.AppendLine("#pragma warning restore EPC12");`, append:

```csharp
        if (errorMapperField != null)
            EmitMapping(sb, method, errorMapperField);
    }
```

Replace `EmitResultCatches` with:

```csharp
    private static void EmitResultCatches(StringBuilder sb, MethodModel method, string? callerToken)
    {
        if (callerToken != null)
        {
            sb.AppendLine($"        catch (global::System.OperationCanceledException __ex) when ({callerToken}.IsCancellationRequested)");
            sb.AppendLine("        {");
            sb.AppendLine("            __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);");
            sb.AppendLine("            throw;");
            sb.AppendLine("        }");
        }
        // Not requested by the caller, so it is HttpClient.Timeout or another timeout in the pipeline.
        sb.AppendLine("        catch (global::System.OperationCanceledException __ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);");
        EmitFailure(sb, "            ", method, "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Timeout, null, __ex)");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.Net.Http.HttpRequestException __ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);");
        EmitFailure(sb, "            ", method, "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Transport, null, __ex)");
        sb.AppendLine("        }");
    }
```

Keep the existing comment block above `EmitResultCatches`.

In `EmitResponseHandling`, replace the whole `else if (method.ReturnsResult) { ... }` branch with:

```csharp
        else if (method.ReturnsResult)
        {
            var resultType = ResultTypeName(method);
            sb.AppendLine($"{indent}if (response.IsSuccessStatusCode)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{i1}var responseStream = await response.Content.ReadAsStreamAsync({ctArg}).ConfigureAwait(false);");
            // Only the deserialize call is guarded: whatever the serializer throws, a JsonException or
            // a MemoryPack or MessagePack exception, means the body could not be read. Cancellation
            // is left to the method's cancellation catches.
            if (method.MapsError)
            {
                // The success return stays inside the try, so the catch can store the error and fall
                // through to the single mapping site after the method's try.
                sb.AppendLine($"{i1}try");
                sb.AppendLine($"{i1}{{");
                sb.AppendLine($"{i2}{method.InnerTypeName} content = (await {serializerExpr}.DeserializeAsync<{method.InnerTypeName}>(responseStream, {ctArg}).ConfigureAwait(false))!;");
                sb.AppendLine($"{i2}return {resultType}.Success(content);");
                sb.AppendLine($"{i1}}}");
            }
            else
            {
                sb.AppendLine($"{i1}{method.InnerTypeName} content;");
                sb.AppendLine($"{i1}try");
                sb.AppendLine($"{i1}{{");
                sb.AppendLine($"{i2}content = (await {serializerExpr}.DeserializeAsync<{method.InnerTypeName}>(responseStream, {ctArg}).ConfigureAwait(false))!;");
                sb.AppendLine($"{i1}}}");
            }
            sb.AppendLine($"{i1}catch (global::System.Exception __ex) when (__ex is not global::System.OperationCanceledException)");
            sb.AppendLine($"{i1}{{");
            sb.AppendLine($"{i2}__RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);");
            EmitFailure(sb, i2, method, "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Deserialization, response, __ex)");
            sb.AppendLine($"{i1}}}");
            if (!method.MapsError)
                sb.AppendLine($"{i1}return {resultType}.Success(content);");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine($"{indent}else");
            sb.AppendLine($"{indent}{{");
            if (maxErrorBodyBytes > 0)
            {
                // Read before the response is disposed. The helper caps the body, turns a failed read
                // into an empty body, and still throws on caller cancellation.
                var cap = maxErrorBodyBytes.ToString(CultureInfo.InvariantCulture);
                sb.AppendLine($"{i1}var __errorBody = await global::ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(response.Content, {cap}, {ctArg}).ConfigureAwait(false);");
                EmitFailure(sb, i1, method, "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null, __errorBody.Body, __errorBody.Truncated)");
            }
            else
            {
                EmitFailure(sb, i1, method, "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null)");
            }
            sb.AppendLine($"{indent}}}");
        }
```

For an `HttpError` method, `EmitFailure` writes the same two lines Part 1 wrote, so that output is unchanged. `HttpErrorMethod_IsEmittedExactlyAsWithoutAMapper` checks this.

The emitted `AskAsync` in `IJevApi`, from the send onward, is:

```csharp
        global::ZeroAlloc.Rest.HttpError __httpError;
        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            // ... status tag, server address, metrics: unchanged ...
            if (response.IsSuccessStatusCode)
            {
                var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                try
                {
                    MyApp.SystemOneResponse content = (await _serializer.DeserializeAsync<MyApp.SystemOneResponse>(responseStream, ct).ConfigureAwait(false))!;
                    return ZeroAlloc.Results.Result<MyApp.SystemOneResponse, global::MyApp.JevError>.Success(content);
                }
                catch (global::System.Exception __ex) when (__ex is not global::System.OperationCanceledException)
                {
                    __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);
                    __httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Deserialization, response, __ex);
                }
            }
            else
            {
                var __errorBody = await global::ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(response.Content, 65536, ct).ConfigureAwait(false);
                __httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null, __errorBody.Body, __errorBody.Truncated);
            }
        }
#pragma warning disable EPC12
        catch (global::System.OperationCanceledException __ex) when (ct.IsCancellationRequested)
        {
            __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);
            throw;
        }
        catch (global::System.OperationCanceledException __ex)
        {
            __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);
            __httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Timeout, null, __ex);
        }
        catch (global::System.Net.Http.HttpRequestException __ex)
        {
            __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);
            __httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Transport, null, __ex);
        }
        catch (global::System.Exception __ex)
        {
            __RecordFailure(__activity, __ex, __sw, __httpMethod, __RestMethodTag);
            throw;
        }
#pragma warning restore EPC12
        try
        {
            return ZeroAlloc.Results.Result<MyApp.SystemOneResponse, global::MyApp.JevError>.Failure(_jevErrorMapper.Map(__httpError));
        }
        catch (global::System.Exception __ex)
        {
            __RecordMapperFailure(__activity, __ex);
            throw;
        }
```

`__httpError` is definitely assigned after the outer `try`:
- every path out of the `try` block either returns or assigns it;
- the two `OperationCanceledException` catches and the `HttpRequestException` catch assign it or throw;
- the generic catch throws.

The `#pragma warning disable EPC12` around the catches already exists on 2.0.1. This plan adds no suppression.

- [ ] **Step 10: Run the generator tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 11: Write the failing runtime tests**

Create `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorMapperTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

// Issue #300: a method returning Result<T, TError> maps every failure through the interface's
// [ErrorMapper]. A mapper's own exception propagates unchanged and is never mapped again.

public sealed record DomainError(HttpErrorKind Kind, HttpStatusCode Status, string Code, string Source);

public sealed class DomainErrorMapper : IHttpErrorMapper<DomainError>
{
    public DomainError Map(HttpError error) => new(error.Kind, error.StatusCode, ReadCode(error), "library");

    private static string ReadCode(HttpError error)
    {
        if (error.Body.IsEmpty || error.BodyTruncated)
            return "none";
        try
        {
            using var document = JsonDocument.Parse(error.Body);
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "none" : "none";
        }
        catch (JsonException)
        {
            return "unparsable";
        }
    }
}

public sealed class ThrowingMapper : IHttpErrorMapper<DomainError>
{
    public int Calls { get; private set; }

    public DomainError Map(HttpError error)
    {
        Calls++;
        // An HttpRequestException on purpose: if the mapper ran inside the method's try, the
        // Transport catch would take it and map it a second time.
        throw new HttpRequestException("mapper broke");
    }
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(DomainErrorMapper))]
public interface IMappedApi
{
    [Get("/things/{id}")]
    Task<Result<UserDto, DomainError>> GetThingAsync(int id, CancellationToken ct = default);

    [Get("/things/{id}/plain")]
    Task<Result<UserDto, HttpError>> GetPlainThingAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(ThrowingMapper))]
public interface IThrowingMapperApi
{
    [Get("/things/{id}")]
    Task<Result<UserDto, DomainError>> GetThingAsync(int id, CancellationToken ct = default);
}

public sealed class ResultErrorMapperTests
{
    internal const string ProblemJson = """{"code":"field_required","field":"name"}""";

    internal static readonly Uri BaseAddress = new("http://stub.local/");

    [Fact]
    public async Task StatusError_BodyIsMappedToTheDomainError()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.UnprocessableEntity, ProblemJson));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new DomainError(HttpErrorKind.Status, HttpStatusCode.UnprocessableEntity, "field_required", "library"),
            result.Error);
    }

    [Fact]
    public async Task Timeout_IsMapped()
    {
        using var httpClient = CreateHttpClient(HangAsync, TimeSpan.FromMilliseconds(50));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.Equal(new DomainError(HttpErrorKind.Timeout, 0, "none", "library"), result.Error);
    }

    [Fact]
    public async Task TransportError_IsMapped()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("refused"));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.Equal(new DomainError(HttpErrorKind.Transport, 0, "none", "library"), result.Error);
    }

    [Fact]
    public async Task DeserializationError_IsMapped()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.OK, "{ not json"));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.Equal(new DomainError(HttpErrorKind.Deserialization, HttpStatusCode.OK, "none", "library"), result.Error);
    }

    [Fact]
    public async Task Success_PassesThrough()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.OK, """{"id":1,"name":"Ada"}"""));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada", result.Value.Name);
    }

    [Fact]
    public async Task HttpErrorMethod_OnTheSameInterface_IsNotMapped()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.UnprocessableEntity, ProblemJson));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetPlainThingAsync(1);

        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(Encoding.UTF8.GetBytes(ProblemJson), result.Error.Body.ToArray());
    }

    [Fact]
    public async Task CallerCancellation_StillThrows_AndIsNotMapped()
    {
        using var httpClient = CreateHttpClient(HangAsync);
        var mapper = new ThrowingMapper();
        IThrowingMapperApi client = new ThrowingMapperApiClient(httpClient, new SystemTextJsonSerializer(), mapper);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetThingAsync(1, cts.Token));
        Assert.Equal(0, mapper.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingMapper_ExceptionPropagates_AndIsNotMappedAgain(bool transportFailure)
    {
        using var httpClient = CreateHttpClient((_, _) => transportFailure
            ? throw new HttpRequestException("refused")
            : Respond(HttpStatusCode.UnprocessableEntity, ProblemJson));
        var mapper = new ThrowingMapper();
        IThrowingMapperApi client = new ThrowingMapperApiClient(httpClient, new SystemTextJsonSerializer(), mapper);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetThingAsync(1));

        Assert.Equal("mapper broke", ex.Message);
        Assert.Equal(1, mapper.Calls);
    }

    internal static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHandler(send)) { BaseAddress = BaseAddress };
        if (timeout is { } t)
            httpClient.Timeout = t;
        return httpClient;
    }

    internal static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        });

    private static async Task<HttpResponseMessage> HangAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        throw new InvalidOperationException("unreachable");
    }
}

// Process-wide ActivityListener: kept out of parallel runs, like TelemetryTests.
[Collection("rest-telemetry-non-parallel")]
public sealed class ResultErrorMapperTelemetryTests
{
    [Fact]
    public async Task ThrowingMapper_MarksTheSpanFailed()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "ZeroAlloc.Rest", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                    stopped.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var httpClient = ResultErrorMapperTests.CreateHttpClient((_, _) =>
            ResultErrorMapperTests.Respond(HttpStatusCode.UnprocessableEntity, ResultErrorMapperTests.ProblemJson));
        IThrowingMapperApi client = new ThrowingMapperApiClient(httpClient, new SystemTextJsonSerializer(), new ThrowingMapper());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetThingAsync(1));

        var span = Assert.Single(stopped, a => string.Equals(a.DisplayName, "IThrowingMapperApi.GetThingAsync", StringComparison.Ordinal));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("mapper broke", span.StatusDescription);
    }
}
```

`StubHandler` is internal, from Part 1 Task 1. `HttpStatusCode` 0 in the expected records is `(HttpStatusCode)0`. The literal `0` converts implicitly to an enum. If the analyzers flag that, write `(HttpStatusCode)0`.

- [ ] **Step 12: Run them to verify they pass**

These tests are written after the generator work, so they should pass on first run. To prove they bite, temporarily move the `Map` call back inside the `try`, as `Failure(mapper.Map(__CreateHttpError(...)))` at the `Status` site, and run the theory: `ThrowingMapper_ExceptionPropagates_AndIsNotMappedAgain(False)` must fail with `Assert.Equal() Failure: Expected: 1 Actual: 2`. Then revert.

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj --filter "FullyQualifiedName~ResultErrorMapper"`
Expected: PASS, 10 test cases.

- [ ] **Step 13: Run the full suite**

Run the full-suite command. Expected: 0 warnings, all tests pass.

- [ ] **Step 14: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
  tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs \
  tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorMapperTests.cs
git commit -F - <<'EOF'
feat: map Result failures to a user-defined error type

A method returning Result<T, TError> now compiles when the interface names a mapper for
TError. The client takes one IHttpErrorMapper<TError> per error type its methods use, and
Create resolves the concrete mapper type.

Status, Timeout, Transport and Deserialization failures are all mapped, once, after the
method's try. A mapper's own exception reaches the caller unchanged, marks the span failed,
and is never mapped again. Methods returning HttpError are emitted exactly as before.

Refs #300

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Registration: `Add{I}`, the Resilience bridge, and the AOT smoke

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs` (`EmitGeneratedClientMembers`, the `AddSerializers` body)
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs`
- Test: `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorMapperTests.cs` (append the DI tests)
- Test: `tests/ZeroAlloc.Rest.Resilience.Tests/RestResilienceErrorMapperTests.cs` (create)
- Create: `samples/ZeroAlloc.Rest.AotSmoke/IQuoteApi.cs`
- Modify: `samples/ZeroAlloc.Rest.AotSmoke/Program.cs`

**Interfaces:**
- Consumes (Task 6): `Create`, which resolves `GetRequiredService<TMapper>`, and `ClientModel.ErrorMappers`.
- Produces: `IGeneratedRestClient<TSelf>.AddSerializers`, which calls `TryAddSingleton<TMapper>(services)` once per distinct usable mapper type, used or not. `DiEmitter` and `RestResilienceServiceCollectionExtensions` stay unchanged and reach it through the existing calls.

- [ ] **Step 1: Write the failing generator test**

Add to `GeneratorErrorMapperTests`:

```csharp
    [Fact]
    public void AddSerializers_RegistersEveryDeclaredMapper_ByItsConcreteType()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(JevErrorMapper))]
            [ErrorMapper(typeof(OtherErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.CompileErrors);
        var client = run.Sources["IJevApi.g.cs"];
        const string TryAdd = "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton";
        Assert.Contains($"{TryAdd}<global::MyApp.JevErrorMapper>(services);", client);
        // No method uses OtherError: its mapper is registered, never called, and not a parameter.
        Assert.Contains($"{TryAdd}<global::MyApp.OtherErrorMapper>(services);", client);
        Assert.DoesNotContain("otherErrorMapper", client);
        Assert.DoesNotContain("IHttpErrorMapper<global::MyApp.JevError>>(services)", client);
    }
```

- [ ] **Step 2: Write the failing DI tests**

Append to `tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorMapperTests.cs`, and add `using Microsoft.Extensions.DependencyInjection;` to its usings:

```csharp
public sealed class HostDomainErrorMapper : IHttpErrorMapper<DomainError>
{
    public DomainError Map(HttpError error) => new(error.Kind, error.StatusCode, "host", "host");
}

public sealed class ResultErrorMapperDiTests
{
    [Fact]
    public void Add_RegistersTheMapper()
    {
        var services = new ServiceCollection();
        services.AddIMappedApi(o => o.UseSerializer(new SystemTextJsonSerializer()));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<DomainErrorMapper>());
        Assert.IsType<MappedApiClient>(provider.GetRequiredService<IMappedApi>());
    }

    [Fact]
    public async Task HostRegistrationOfTheMapperInterface_DoesNotReplaceTheLibraryMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpErrorMapper<DomainError>, HostDomainErrorMapper>();
        services.AddIMappedApi(o =>
            {
                o.BaseAddress = ResultErrorMapperTests.BaseAddress;
                o.UseSerializer(new SystemTextJsonSerializer());
            })
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                ResultErrorMapperTests.Respond(HttpStatusCode.UnprocessableEntity, ResultErrorMapperTests.ProblemJson)));

        using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IMappedApi>().GetThingAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal("library", result.Error.Source);
        Assert.Equal("field_required", result.Error.Code);
    }
}
```

- [ ] **Step 3: Write the failing Resilience bridge tests**

Create `tests/ZeroAlloc.Rest.Resilience.Tests/RestResilienceErrorMapperTests.cs`:

```csharp
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Resilience.Tests;

// Issue #300 through the Resilience bridge: AddRestResilience registers the mapper through the
// client's AddSerializers and builds the client with the concrete mapper type.

public sealed record BridgeError(HttpStatusCode Status, int BodyLength, string Source);

public sealed class BridgeErrorMapper : IHttpErrorMapper<BridgeError>
{
    public BridgeError Map(HttpError error) => new(error.StatusCode, error.Body.Length, "library");
}

public sealed class HostBridgeErrorMapper : IHttpErrorMapper<BridgeError>
{
    public BridgeError Map(HttpError error) => new(error.StatusCode, error.Body.Length, "host");
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(BridgeErrorMapper))]
[Retry(MaxAttempts = 2, BackoffMs = 1)]
public interface IMappedBridgeApi
{
    [Get("/items/{id}")]
    Task<Result<string, BridgeError>> GetItemAsync(int id, CancellationToken ct = default);
}

public sealed class RestResilienceErrorMapperTests
{
    private const string Problem = "{\"code\":\"x\"}";

    [Fact]
    public async Task Bridge_ResolvesTheMapper_AndReturnsTheMappedError()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.UnprocessableEntity, Problem);
        using var provider = Build(handler, _ => { });

        var result = await provider.GetRequiredService<IMappedBridgeApi>().GetItemAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(new BridgeError(HttpStatusCode.UnprocessableEntity, Problem.Length, "library"), result.Error);
        // [Retry] passes a returned Result through; it retries only a thrown exception.
        Assert.Single(handler.Requests);
        Assert.NotNull(provider.GetService<BridgeErrorMapper>());
    }

    [Fact]
    public async Task Bridge_HostRegistrationOfTheMapperInterface_DoesNotReplaceTheLibraryMapper()
    {
        var handler = new FakeMessageHandler();
        handler.SetResponse(HttpStatusCode.UnprocessableEntity, Problem);
        using var provider = Build(handler, s => s.AddSingleton<IHttpErrorMapper<BridgeError>, HostBridgeErrorMapper>());

        var result = await provider.GetRequiredService<IMappedBridgeApi>().GetItemAsync(1);

        Assert.Equal("library", result.Error.Source);
    }

    private static ServiceProvider Build(FakeMessageHandler handler, Action<IServiceCollection> before)
    {
        var services = new ServiceCollection();
        before(services);
        services.AddMappedBridgeApiResiliencePolicies((_, p) =>
            p.Retry = new RetryPolicy(maxAttempts: 1, backoffMs: 1, jitter: false, perAttemptTimeoutMs: 0));
        services.AddRestResilience<IMappedBridgeApi, MappedBridgeApiClient, IMappedBridgeApiResilienceProxy>(
                resilienceFactory: (inner, sp) => new IMappedBridgeApiResilienceProxy(
                    inner,
                    sp.GetRequiredService<MappedBridgeApiResiliencePolicies>()),
                configure: o =>
                {
                    o.BaseAddress = new Uri("http://fake.local/");
                    o.UseSerializer<SystemTextJsonSerializer>();
                })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }
}
```

ZeroAlloc.Resilience 3.1.0 accepts plain `[Retry]` on `Task<Result<T, E>>` for any `E`: "retry already passes the inner Result through". Only `NonThrowing`, `[CircuitBreaker]` without `Fallback`, and `[RateLimit]` report ZR0003. If the Resilience generator reports any diagnostic for this interface, stop and report it. Do not add it to `NoWarn`.

- [ ] **Step 4: Run them to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "FullyQualifiedName~AddSerializers_RegistersEveryDeclaredMapper_ByItsConcreteType"`
Expected: FAIL, `Assert.Contains() Failure: Sub-string not found`, for the `TryAddSingleton<global::MyApp.JevErrorMapper>` line.

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj --filter "FullyQualifiedName~ResultErrorMapperDiTests"`
Expected: FAIL.
- `Add_RegistersTheMapper` fails with `Assert.NotNull() Failure: Value is null`.
- The host-registration test fails with `System.InvalidOperationException : No service for type 'ZeroAlloc.Rest.Integration.Tests.DomainErrorMapper' has been registered.`

Run: `dotnet test tests/ZeroAlloc.Rest.Resilience.Tests/ZeroAlloc.Rest.Resilience.Tests.csproj --filter "FullyQualifiedName~RestResilienceErrorMapperTests"`
Expected: FAIL, `No service for type 'ZeroAlloc.Rest.Resilience.Tests.BridgeErrorMapper' has been registered.`

- [ ] **Step 5: Register the mappers in `AddSerializers`**

In `EmitGeneratedClientMembers`, after `foreach (var st in overrideSerializers) sb.AppendLine($"        {TryAddSingleton}<{st}>(services);");` and before the closing `sb.AppendLine("    }");` of `AddSerializers`, add:

```csharp
        // Every usable [ErrorMapper], used or not, by its concrete type. Create resolves that type, so
        // a host registration of IHttpErrorMapper<E> is never consulted.
        var registeredMappers = new List<string>();
        foreach (var mapper in model.ErrorMappers)
        {
            if (registeredMappers.Contains(mapper.MapperTypeName))
                continue;
            registeredMappers.Add(mapper.MapperTypeName);
            sb.AppendLine($"        {TryAddSingleton}<{mapper.MapperTypeName}>(services);");
        }
```

For `IJevApi` with `JevErrorMapper` and `OtherErrorMapper`, the emitted `AddSerializers` is:

```csharp
    static void global::ZeroAlloc.Rest.IGeneratedRestClient<JevApiClient>.AddSerializers(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::ZeroAlloc.Rest.ZeroAllocClientOptions options)
    {
        global::ZeroAlloc.Rest.GeneratedRestClient.AddPerClientSerializer<IJevApi>(services, options);
        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::MyApp.JevErrorMapper>(services);
        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::MyApp.OtherErrorMapper>(services);
    }
```

`TryAddSingleton<TService>` carries `[DynamicallyAccessedMembers(PublicConstructors)]`, the same way the interface-level `[Serializer]` type is already registered, so it is trim- and AOT-safe.

- [ ] **Step 6: Run the tests to verify they pass**

Run the three commands from step 4 again.
Expected: PASS for each.

- [ ] **Step 7: Add the mapped client to the AOT smoke**

Create `samples/ZeroAlloc.Rest.AotSmoke/IQuoteApi.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.AotSmoke;

public sealed record QuoteError(HttpErrorKind Kind, int Status, int BodyLength);

public sealed class QuoteErrorMapper : IHttpErrorMapper<QuoteError>
{
    public QuoteError Map(HttpError error) => new(error.Kind, (int)error.StatusCode, error.Body.Length);
}

// A Result method with a user-defined error type: the generated client takes the mapper, and the
// generated Add registers and resolves it, all under ILC.
[ZeroAllocRestClient]
[ErrorMapper(typeof(QuoteErrorMapper))]
public interface IQuoteApi
{
    [Get("/quotes/{id}")]
    Task<Result<string, QuoteError>> TryGetQuoteAsync(int id, CancellationToken ct = default);
}
```

In `samples/ZeroAlloc.Rest.AotSmoke/Program.cs`:

After the `services.AddIUserApi(...)` call, add:

```csharp
services.AddIQuoteApi(o =>
{
    o.BaseAddress = new Uri("http://localhost/");
    o.UseSerializer(new SmokeSerializer());
});
```

Inside the `using (var provider = ...)` block, after the `IStatusApi` check, add:

```csharp
    if (provider.GetRequiredService<IQuoteApi>() is not QuoteApiClient)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — IQuoteApi should resolve to QuoteApiClient");
        return 1;
    }
```

Before `Console.WriteLine("AOT smoke: PASS");`, add:

```csharp

// A mapped error type: the 422 body and a refused connection both reach the mapper.
using (var rejectingHttp = new System.Net.Http.HttpClient(new UnprocessableHandler()) { BaseAddress = new Uri("http://localhost/") })
using (var refusingHttp = new System.Net.Http.HttpClient(new RefusingHandler()) { BaseAddress = new Uri("http://localhost/") })
{
    IQuoteApi rejecting = new QuoteApiClient(rejectingHttp, new SmokeSerializer(), new QuoteErrorMapper());
    var status = await rejecting.TryGetQuoteAsync(1).ConfigureAwait(false);
    if (!status.IsFailure
        || status.Error.Kind != HttpErrorKind.Status
        || status.Error.Status != 422
        || status.Error.BodyLength != UnprocessableHandler.Body.Length)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a 422 should map to QuoteError with its body");
        return 1;
    }

    IQuoteApi refusing = new QuoteApiClient(refusingHttp, new SmokeSerializer(), new QuoteErrorMapper());
    var transport = await refusing.TryGetQuoteAsync(1).ConfigureAwait(false);
    if (!transport.IsFailure || transport.Error.Kind != HttpErrorKind.Transport)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a transport failure should map to QuoteError");
        return 1;
    }
}
```

- [ ] **Step 8: Publish and run the AOT smoke**

Run:

```bash
dotnet publish samples/ZeroAlloc.Rest.AotSmoke/ZeroAlloc.Rest.AotSmoke.csproj -r win-x64 -c Release -o C:/wt/aot-out-rest
C:/wt/aot-out-rest/ZeroAlloc.Rest.AotSmoke.exe
```

Expected: no IL2026, IL2067, IL2075, IL2091, IL3050 or IL3051 warnings, and the output `AOT smoke: PASS`. Without the MSVC toolchain, say so in the PR and rely on CI's `aot-smoke` job.

- [ ] **Step 9: Run the full suite**

Run the full-suite command. Expected: 0 warnings, all tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
  tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs \
  tests/ZeroAlloc.Rest.Integration.Tests/ResultErrorMapperTests.cs \
  tests/ZeroAlloc.Rest.Resilience.Tests/RestResilienceErrorMapperTests.cs \
  samples/ZeroAlloc.Rest.AotSmoke/IQuoteApi.cs samples/ZeroAlloc.Rest.AotSmoke/Program.cs
git commit -F - <<'EOF'
feat: register error mappers through Add and the Resilience bridge

The generated AddSerializers registers every usable mapper by its concrete type, and Create
resolves that type. A host registration of IHttpErrorMapper<TError> can therefore never
replace a library's mapper. The Resilience bridge gets the mapper through the same calls, and
the AOT smoke now publishes and runs a mapped client.

Refs #300

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Docs for the mapper, the diagnostics and the bridge

**Files:**
- Modify: `docs/advanced.md` (add two sections; update the front-matter description)
- Modify: `docs/resilience.md` (the `## Result Returns and Error Handling` section)
- Modify: `docs/parameters.md:127`

**Interfaces:**
- Consumes: everything from Tasks 5 to 7, as user-facing behaviour.
- Produces: nothing in code.

- [ ] **Step 1: Add the mapper guide**

In `docs/advanced.md`, insert this before `## CancellationToken`:

````markdown
## Your own error type: `[ErrorMapper]`

A client SDK often wants to return its own error type rather than `HttpError`. Declare the method with `Result<T, TError>`, and name a mapper for `TError` on the interface:

```csharp
using System.Text.Json;
using ZeroAlloc.Rest;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

public sealed record JevError(string Code, string Message, bool Retryable);

public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
{
    public JevError Map(HttpError error) => error.Kind switch
    {
        HttpErrorKind.Status when error.ContentType == "application/problem+json" && !error.BodyTruncated
            => FromProblem(error),
        HttpErrorKind.Status => new($"http_{(int)error.StatusCode}", $"Status {(int)error.StatusCode}", (int)error.StatusCode >= 500),
        HttpErrorKind.Timeout => new("timeout", error.Message ?? "Timed out", true),
        HttpErrorKind.Transport => new("transport", error.Message ?? "Transport failure", true),
        _ => new("unreadable_response", error.Message ?? "Unreadable response", false),
    };

    private static JevError FromProblem(HttpError error)
    {
        try
        {
            using var problem = JsonDocument.Parse(error.Body);
            var root = problem.RootElement;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            return new(code ?? $"http_{(int)error.StatusCode}", detail ?? "", (int)error.StatusCode >= 500);
        }
        catch (JsonException)
        {
            return new($"http_{(int)error.StatusCode}", "Unparsable problem details", false);
        }
    }
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(JevErrorMapper))]
internal interface IJevApi
{
    [Post("v1/systemone")]
    ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
}
```

- **Every failure kind** goes through `Map` once: `Status`, `Timeout`, `Transport` and `Deserialization`. The mapper receives the built `HttpError`, with the body already read. It never sees the response, which is already disposed.
- **Make the mapper total.** Return a `TError` for every input. An exception it throws reaches the caller unchanged and marks the request's span as failed. It is never passed to a mapper again.
- **Registration is automatic.** `Add{I}`, and `AddRestResilience`, register the mapper as a singleton by its concrete type, and the client is built with that type. A host registration of `IHttpErrorMapper<JevError>` never replaces a library's mapper. The mapper's constructor dependencies come from the container.
- **One mapper per error type.** Declare several `[ErrorMapper]` attributes for several error types. A mapper whose error type no method uses is allowed; it is registered and never called.
- `Result<T, HttpError>` methods on the same interface are unaffected.
- Caller cancellation still throws, as for `HttpError` methods.

## Diagnostics

The generator reports these errors. Each points at the method or attribute at fault.

### ZRA001: Conflicting body attributes

A method has both a `[Body]` and a `[FormBody]` parameter. Keep one.

### ZRA002: No error mapper for a Result error type

A method returns `Result<T, TError>` with a `TError` other than `HttpError`, and no `[ErrorMapper]` on the interface implements `IHttpErrorMapper<TError>`. Add `[ErrorMapper(typeof(YourMapper))]` for that type, or return `Result<T, HttpError>`.

### ZRA003: Invalid error mapper type

An `[ErrorMapper]` names a type that implements no `IHttpErrorMapper<TError>`, or that is not a closed, non-abstract class with a public constructor. The container must be able to construct it.

### ZRA004: Duplicate error mapper

Two `[ErrorMapper]` attributes on one interface map the same error type. Keep one. The error points at the second attribute and names the first.
````

Change the front-matter `description` on line 6 to:

```text
description: Result<T, HttpError>, the error body, your own error type with [ErrorMapper], diagnostics, multiple serializers, and edge cases.
```

- [ ] **Step 2: Link ZRA001 to the new section**

In `docs/parameters.md`, replace line 127:

```markdown
`[FormBody]` and `[Body]` are mutually exclusive on the same method. Using both produces a `ZRA001` compile-time error.
```

with:

```markdown
`[FormBody]` and `[Body]` are mutually exclusive on the same method. Using both produces a [`ZRA001`](advanced.md#zra001-conflicting-body-attributes) compile-time error.
```

- [ ] **Step 3: Document mapped errors through the bridge**

In `docs/resilience.md`, insert this before the final paragraph of `## Result Returns and Error Handling`, the one starting `See the [ZeroAlloc.Resilience result-return-types guide]`:

```markdown
### Mapped error types

A method that returns `Result<T, TError>` through an [`[ErrorMapper]`](advanced.md#your-own-error-type-errormapper) works through the bridge exactly like the generated `Add{I}`. `AddRestResilience` registers the mapper through the client's `AddSerializers` and builds the client with the concrete mapper type, so a host registration of `IHttpErrorMapper<TError>` does not replace it.

The policy rules in the table above apply with `TError` in place of `HttpError`. `[Retry]` passes the mapped failure through. `[CircuitBreaker]` without `Fallback`, and `[RateLimit]`, are compile error ZR0003, because the resilience generator cannot build a `TError`.
```

- [ ] **Step 4: Check the docs links and build**

Run:

```bash
grep -n "zra001-conflicting-body-attributes\|your-own-error-type-errormapper" docs/*.md
grep -n "^### ZRA00\|^## Your own error type" docs/advanced.md
```

Expected: the anchors used in `parameters.md` and `resilience.md` match the headings in `advanced.md`: `### ZRA001: Conflicting body attributes` and ``## Your own error type: `[ErrorMapper]` ``. The published site uses GitHub-style slugs, which drop the colon and the backticks and brackets.

Run the full-suite command. Expected: 0 warnings, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add docs/advanced.md docs/resilience.md docs/parameters.md
git commit -F - <<'EOF'
docs: document ErrorMapper, diagnostics ZRA001 to ZRA004, and the bridge

Adds a worked mapper example with a domain error type and a diagnostics section covering
every generator error, and explains how mapped errors flow through AddRestResilience.

Refs #300

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Checkpoint: ship PR 2

- [ ] Push `feat/rest-error-mapper` and open PR 2 against `main`. Title: `feat: Result<T, TError> with a user-defined error type`. Body: a summary of Tasks 5 to 8, the two `fix:` commits, `Closes #300`, and a note that the spec's design decisions are resolved as listed in this plan. End the body with the `🤖 Generated with [Claude Code](https://claude.com/claude-code)` line and no session URL.
- [ ] Confirm CI is green: `build`, `api-compat` with no new suppressions, and `aot-smoke`.
- [ ] After the squash merge, confirm the release-please PR lists every `feat:` and `fix:` from both PRs. Its version must be 2.1.0 if the release was held after PR 1.
- [ ] Comment on #300 with what shipped:
  - `IHttpErrorMapper<TError>` and `[ErrorMapper]`;
  - ZRA002 to ZRA004;
  - registration through `Add{I}` and the bridge;
  - the ZRA001 location fix.
- [ ] Remove the worktrees once both PRs are merged:

```bash
git -C C:/wt/rest-errors worktree remove C:/wt/rest-mapper
```

---

## Spec coverage

| Spec requirement | Task |
|---|---|
| `HttpError.Body`, `ContentType`, `BodyTruncated` | 2 |
| `MaxErrorBodyBytes = 65536` on `[ZeroAllocRestClient]`; 0 emits no body read | 3 |
| Only `Status` reads the body; the others keep an empty `Body` | 3 |
| Cap and truncation flag | 2, 3 |
| Exact buffer for a known length; pooled and copied once otherwise; empty allocates nothing | 2 |
| A failed read still gives `Status` with an empty body; caller cancellation throws | 2, 3 |
| `ContentType` for `Status` and `Deserialization` | 3 |
| Content headers merged into `Headers`, as a `fix:` | 1 |
| `GetRetryAfter`: seconds, HTTP-date, past date, invalid, negative, absent, `TimeProvider` | 4 |
| Tests: 422 JSON, empty, over cap, no Content-Length, read throws, cancellation, content headers | 1, 2, 3 |
| Generator tests: body read only on `Status`; cap 0 emits none | 3 |
| `IHttpErrorMapper<TError>`, `ErrorMapperAttribute` | 5 |
| `ModelExtractor` records `E` as `ErrorTypeName`; `ResultTypeName` emits `Result<T, E>` | 5, 6 |
| Mapper chosen per `E` that implements `IHttpErrorMapper<E>` | 5 |
| Constructor parameter per mapped `E` | 6 |
| All four failure kinds mapped | 6 |
| `AddSerializers` `TryAddSingleton<TMapper>`; `Create` resolves the concrete `TMapper` | 6, 7 |
| `IGeneratedRestClient` unchanged; the bridge works through the existing calls | 7 |
| A mapper that throws propagates, is recorded on the span, and is never re-mapped | 6 |
| ZRA002, ZRA003, ZRA004 as errors at real locations | 5 |
| An unused mapper is allowed and registered | 5, 6, 7 |
| Generator tests: parameter, four kinds, `HttpError` unchanged, two types, diagnostics, internal compile | 5, 6, 7 |
| Runtime: 422 mapped; timeout, transport, deserialization mapped; throwing mapper | 6 |
| DI: host registration does not replace; bridge resolves | 7 |
| AOT smoke with a mapped client | 7, plus the Part 1 body check in 3 |
| Docs: advanced.md body, content type, cap, headers fix, `GetRetryAfter`, mapper example | 1, 3, 4, 8 |
| Docs: ZRA002 to ZRA004 sections next to ZRA001 | 8 |
| Docs: resilience.md mapped errors through the bridge | 8 |
| `PublicAPI.Unshipped.txt`, `AnalyzerReleases.Unshipped.md`, api-compat without suppressions | 2, 3, 4, 5; checkpoints |
| Two sequential PRs; comment on each issue after merge | checkpoints |
