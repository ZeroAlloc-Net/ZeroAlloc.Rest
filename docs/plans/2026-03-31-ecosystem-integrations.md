# ZeroAlloc.Rest Ecosystem Integrations Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Integrate ZeroAlloc.Collections, ZeroAlloc.Results, and ZeroAlloc.Analyzers into ZeroAlloc.Rest — replacing `ApiResponse<T>` with `Result<T, HttpError>`, using pooled `HeapPooledList<char>` for URL building, and fixing all analyzer-flagged issues.

**Architecture:** Three orthogonal changes committed in sequence: (1) add new packages to Directory.Packages.props and project files; (2) add `HttpError`, remove `ApiResponse<T>`, update generator to emit `Result<T, HttpError>` wrapping; (3) fix `ZeroAlloc.Analyzers` diagnostics throughout the codebase.

**Tech Stack:** ZeroAlloc.Results 0.1.4, ZeroAlloc.Collections 0.1.3, ZeroAlloc.Analyzers 1.3.12, .NET 10, Roslyn source generator (netstandard2.0).

---

## Context

Working directory: `c:/Projects/Prive/ZeroAlloc.Rest`

Key files:
- `Directory.Packages.props` — central package versions
- `src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj` — runtime library
- `src/ZeroAlloc.Rest/ApiResponse.cs` — **will be deleted**
- `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs` — `ReturnsApiResponse` field renamed to `ReturnsResult`
- `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs` — detection of `Result<T, HttpError>` return type
- `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs` — emits URL building and response handling
- `tests/ZeroAlloc.Rest.Tests/ApiResponseTests.cs` — **will be replaced**
- 70 tests currently passing across 4 test projects

Package APIs:
- `ZeroAlloc.Results`: `Result<T,E>.Success(value)`, `Result<T,E>.Failure(error)`, `UnitResult<E>.Success()`, `UnitResult<E>.Failure(error)` — all `readonly struct`
- `ZeroAlloc.Collections`: `HeapPooledList<char>(capacity)`, `.Add(char)`, `.AsReadOnlySpan()`, `IDisposable` — thread-safe via per-instance ArrayPool rental

---

### Task 1: Add package versions to Directory.Packages.props and project references

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj`

**Step 1: Add package versions to `Directory.Packages.props`**

Inside the existing `<ItemGroup>`, add after the existing entries:

```xml
    <!-- ZeroAlloc ecosystem -->
    <PackageVersion Include="ZeroAlloc.Results" Version="0.1.4" />
    <PackageVersion Include="ZeroAlloc.Collections" Version="0.1.3" />
    <PackageVersion Include="ZeroAlloc.Analyzers" Version="1.3.12" />
```

**Step 2: Add runtime references to `src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj`**

Add inside the existing `<ItemGroup>` (alongside `Microsoft.Extensions.Http`):

```xml
    <PackageReference Include="ZeroAlloc.Results" />
    <PackageReference Include="ZeroAlloc.Collections" />
```

**Step 3: Fix ZeroAlloc.Analyzers in `Directory.Build.props`**

The global analyzer reference in `Directory.Build.props` currently has no version (it relies on central management). It already is listed as `ZeroAlloc.Analyzers` with `PrivateAssets="all"` — after adding the version to `Directory.Packages.props` this will now resolve. No change needed to `Directory.Build.props` itself.

**Step 4: Verify restore and build succeed**

```bash
cd c:/Projects/Prive/ZeroAlloc.Rest
dotnet restore
dotnet build ZeroAlloc.Rest.sln -c Release 2>&1 | tail -10
```

Expected: build succeeds (0 errors). New analyzer warnings may appear — do NOT fix them yet (that's Task 5).

**Step 5: Commit**

```bash
git add Directory.Packages.props src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj
git commit -m "build(deps): add ZeroAlloc.Results, Collections, Analyzers package versions"
```

---

### Task 2: Add `HttpError` and `HeapPooledListExtensions`

**Files:**
- Create: `src/ZeroAlloc.Rest/HttpError.cs`
- Create: `src/ZeroAlloc.Rest/Internal/HeapPooledListExtensions.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/HttpErrorTests.cs`

**Step 1: Write the failing test**

Create `tests/ZeroAlloc.Rest.Tests/HttpErrorTests.cs`:

```csharp
using System.Net;
using Xunit;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.Tests;

public class HttpErrorTests
{
    [Fact]
    public void HttpError_ExposesStatusCode()
    {
        var error = new HttpError(HttpStatusCode.NotFound,
            new Dictionary<string, IReadOnlyList<string>>());
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public void HttpError_ExposesHeaders()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>
        {
            ["X-Request-Id"] = new[] { "abc123" }
        };
        var error = new HttpError(HttpStatusCode.InternalServerError, headers, "boom");
        Assert.Equal("abc123", error.Headers["X-Request-Id"][0]);
        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public void HttpError_MessageDefaultsToNull()
    {
        var error = new HttpError(HttpStatusCode.BadRequest,
            new Dictionary<string, IReadOnlyList<string>>());
        Assert.Null(error.Message);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj --filter "HttpErrorTests" 2>&1 | tail -10
```

Expected: FAIL — `HttpError` not found.

**Step 3: Create `src/ZeroAlloc.Rest/HttpError.cs`**

```csharp
using System.Net;

namespace ZeroAlloc.Rest;

public sealed record HttpError(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Message = null);
```

**Step 4: Create `src/ZeroAlloc.Rest/Internal/HeapPooledListExtensions.cs`**

This is an internal helper used by the *generated* client code (the generator emits calls to it). It enables ergonomic `urlBuilder.Append("key=")` calls on `HeapPooledList<char>`.

```csharp
using ZeroAlloc.Collections;

namespace ZeroAlloc.Rest.Internal;

internal static class HeapPooledListExtensions
{
    internal static void Append(this HeapPooledList<char> list, ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
            list.Add(value[i]);
    }

    internal static void Append(this HeapPooledList<char> list, char value)
        => list.Add(value);
}
```

**Step 5: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj --filter "HttpErrorTests" 2>&1 | tail -5
```

Expected: 3/3 PASS.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Rest/HttpError.cs src/ZeroAlloc.Rest/Internal/HeapPooledListExtensions.cs tests/ZeroAlloc.Rest.Tests/HttpErrorTests.cs
git commit -m "feat(core): add HttpError record and HeapPooledList Append extension"
```

---

### Task 3: Replace `ApiResponse<T>` with `Result<T, HttpError>` — generator models

**Files:**
- Delete: `src/ZeroAlloc.Rest/ApiResponse.cs`
- Delete: `tests/ZeroAlloc.Rest.Tests/ApiResponseTests.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`

**Step 1: Write the failing generator test**

Add to `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs` (or create a new file `GeneratorResultTests.cs` in the same project):

```csharp
[Fact]
public void Generator_ResultReturn_EmitsResultWrapping()
{
    var source = """
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IUserApi
        {
            [Get("/users/{id}")]
            System.Threading.Tasks.Task<Result<string, HttpError>> GetUserAsync(
                int id, System.Threading.CancellationToken ct = default);
        }
        """;

    var output = RunAndGetSources(source);
    var clientFile = output.Single(f => f.HintName == "IUserApi.g.cs");
    var content = clientFile.SourceText.ToString();
    Assert.Contains("Result<", content);
    Assert.Contains("HttpError", content);
    Assert.Contains("Success", content);
    Assert.Contains("Failure", content);
}
```

`RunAndGetSources` already exists in `GeneratorDiTests.cs` — copy/reference the same helper. The test project references `Basic.Reference.Assemblies.Net100` for framework types but NOT ZeroAlloc.Results, so the generator must detect the return type by string matching on the fully-qualified name (same pattern as `ApiResponse<T>` detection).

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "Generator_ResultReturn_EmitsResultWrapping" 2>&1 | tail -10
```

Expected: FAIL — generator emits `EnsureSuccessStatusCode` instead of `Result`.

**Step 3: Update `MethodModel.cs` — rename `ReturnsApiResponse` to `ReturnsResult`**

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
    string? SerializerTypeName);
```

**Step 4: Update `ModelExtractor.cs`**

Change the constant and detection logic:

```csharp
// Replace:
private const string ApiResponseOpenType = "ZeroAlloc.Rest.ApiResponse<T>";

// With:
private const string ResultOpenType = "ZeroAlloc.Results.Result<T, E>";
```

Replace the detection block (currently lines ~80-85):

```csharp
// Old:
returnsApiResponse = inner?.OriginalDefinition.ToDisplayString() == ApiResponseOpenType;
if (returnsApiResponse && inner?.TypeArguments.Length == 1)
    innerTypeName = inner.TypeArguments[0].ToDisplayString();

// New:
bool returnsResult = false;
if (returnType.TypeArguments.Length == 1)
{
    var inner = returnType.TypeArguments[0] as INamedTypeSymbol;
    innerTypeName = inner?.ToDisplayString();

    // Detect Result<T, HttpError> — match by open generic display name
    var innerOpenType = inner?.OriginalDefinition.ToDisplayString();
    returnsResult = innerOpenType == ResultOpenType;
    if (returnsResult && inner?.TypeArguments.Length == 2)
        innerTypeName = inner.TypeArguments[0].ToDisplayString(); // T in Result<T, HttpError>
}
else
{
    returnsVoid = true;
}
```

Also update the `MethodModel` constructor call at the end of `ExtractMethod`:

```csharp
return new MethodModel(
    method.Name, httpMethod, route, returnTypeName,
    innerTypeName, returnsResult, returnsVoid,       // was: returnsApiResponse
    parameters, methodSerializer);
```

**Step 5: Delete `src/ZeroAlloc.Rest/ApiResponse.cs`**

```bash
del c:\Projects\Prive\ZeroAlloc.Rest\src\ZeroAlloc.Rest\ApiResponse.cs
```

Or via git:
```bash
git rm src/ZeroAlloc.Rest/ApiResponse.cs
```

**Step 6: Delete `tests/ZeroAlloc.Rest.Tests/ApiResponseTests.cs`**

```bash
git rm tests/ZeroAlloc.Rest.Tests/ApiResponseTests.cs
```

**Step 7: Build to verify compilation**

```bash
dotnet build ZeroAlloc.Rest.sln -c Release 2>&1 | tail -10
```

Expected: 0 errors. `ClientEmitter.cs` still references `method.ReturnsApiResponse` — that will be a compile error. Fix it now: in `ClientEmitter.cs` line ~184 change `method.ReturnsApiResponse` to `method.ReturnsResult`.

**Step 8: Run all tests**

```bash
dotnet test ZeroAlloc.Rest.sln --no-build -c Release 2>&1 | tail -10
```

Expected: all tests pass (was 70, now -6 ApiResponse tests + new HttpError tests + new generator test).

**Step 9: Commit**

```bash
git add -A
git commit -m "feat(core): replace ApiResponse<T> with Result<T, HttpError> in generator models"
```

---

### Task 4: Update `ClientEmitter` — emit `Result<T, HttpError>` and `HeapPooledList<char>` URL builder

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`

**Step 1: Write generator tests for the new emission**

Add to the generator tests file:

```csharp
[Fact]
public void Generator_ResultReturn_EmitsSuccessAndFailurePaths()
{
    var source = """
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IUserApi
        {
            [Get("/users/{id}")]
            System.Threading.Tasks.Task<ZeroAlloc.Results.Result<string, ZeroAlloc.Rest.HttpError>> GetAsync(
                int id, System.Threading.CancellationToken ct = default);
        }
        """;

    var output = RunAndGetSources(source);
    var content = output.Single(f => f.HintName == "IUserApi.g.cs").SourceText.ToString();
    Assert.Contains("Result<string, ZeroAlloc.Rest.HttpError>.Success", content);
    Assert.Contains("Result<string, ZeroAlloc.Rest.HttpError>.Failure", content);
    Assert.Contains("new ZeroAlloc.Rest.HttpError(", content);
}

[Fact]
public void Generator_QueryParams_UsesHeapPooledList()
{
    var source = """
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IUserApi
        {
            [Get("/users")]
            System.Threading.Tasks.Task<string> ListAsync(
                [Query] string? name, System.Threading.CancellationToken ct = default);
        }
        """;

    var output = RunAndGetSources(source);
    var content = output.Single(f => f.HintName == "IUserApi.g.cs").SourceText.ToString();
    Assert.Contains("HeapPooledList", content);
    Assert.DoesNotContain("System.Text.StringBuilder", content);
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj --filter "EmitsSuccessAndFailure|UsesHeapPooledList" 2>&1 | tail -10
```

Expected: FAIL.

**Step 3: Update `ClientEmitter.EmitResponseHandling` — emit Result wrapping**

Replace the `else if (method.ReturnsResult)` branch in `EmitResponseHandling`:

```csharp
else if (method.ReturnsResult)
{
    var resultType = $"ZeroAlloc.Results.Result<{method.InnerTypeName}, ZeroAlloc.Rest.HttpError>";
    sb.AppendLine($"        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);");
    sb.AppendLine("        if (response.IsSuccessStatusCode)");
    sb.AppendLine("        {");
    sb.AppendLine($"            var content = await {serializerExpr}.DeserializeAsync<{method.InnerTypeName}>(responseStream, {ctArg}).ConfigureAwait(false);");
    sb.AppendLine($"            return {resultType}.Success(content!);");
    sb.AppendLine("        }");
    sb.AppendLine("        var errorHeaders = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>();");
    sb.AppendLine("        foreach (var header in response.Headers)");
    sb.AppendLine("            errorHeaders[header.Key] = new System.Collections.Generic.List<string>(header.Value).AsReadOnly();");
    sb.AppendLine($"        return {resultType}.Failure(new ZeroAlloc.Rest.HttpError(response.StatusCode, errorHeaders));");
}
```

**Step 4: Update `ClientEmitter.EmitUrlBuilding` — switch to `HeapPooledList<char>`**

Replace the entire `EmitUrlBuilding` method:

```csharp
private static void EmitUrlBuilding(StringBuilder sb, string route,
    List<ParameterModel> pathParams, List<ParameterModel> queryParams)
{
    // Build route with path parameter substitution
    var routeExpr = route;
    foreach (var p in pathParams)
        routeExpr = routeExpr.Replace("{" + p.Name + "}", $"{{Uri.EscapeDataString({p.Name}.ToString())}}");

    if (pathParams.Count == 0 && queryParams.Count == 0)
    {
        sb.AppendLine($"        var url = \"{route}\";");
        return;
    }

    if (queryParams.Count == 0)
    {
        sb.AppendLine($"        var url = $\"{routeExpr}\";");
        return;
    }

    // Use HeapPooledList<char> for zero-allocation URL building (per-request rental, thread-safe)
    sb.AppendLine($"        var urlBase = $\"{routeExpr}\";");
    sb.AppendLine("        using var urlBuilder = new ZeroAlloc.Collections.HeapPooledList<char>(urlBase.Length + 64);");
    sb.AppendLine("        ZeroAlloc.Rest.Internal.HeapPooledListExtensions.Append(urlBuilder, urlBase.AsSpan());");
    sb.AppendLine("        var hasQuery = false;");
    foreach (var q in queryParams)
    {
        var sep = $"(hasQuery ? '&' : '?')";
        if (q.IsNullable)
        {
            sb.AppendLine($"        if ({q.Name} != null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            ZeroAlloc.Rest.Internal.HeapPooledListExtensions.Append(urlBuilder, {sep});");
            sb.AppendLine($"            ZeroAlloc.Rest.Internal.HeapPooledListExtensions.Append(urlBuilder, \"{q.QueryName}=\".AsSpan());");
            sb.AppendLine($"            ZeroAlloc.Rest.Internal.HeapPooledListExtensions.Append(urlBuilder, Uri.EscapeDataString({q.Name}!.ToString()!).AsSpan());");
            sb.AppendLine("            hasQuery = true;");
            sb.AppendLine("        }");
        }
        else
        {
            sb.AppendLine($"        ZeroAlloc.Rest.Internal.HeapPooledListExtensions.Append(urlBuilder, {sep});");
            sb.AppendLine($"        ZeroAlloc.Rest.Internal.HeapPooledListExtensions.Append(urlBuilder, \"{q.QueryName}=\".AsSpan());");
            sb.AppendLine($"        ZeroAlloc.Rest.Internal.HeapPooledListExtensions.Append(urlBuilder, Uri.EscapeDataString({q.Name}.ToString()!).AsSpan());");
            sb.AppendLine("        hasQuery = true;");
        }
    }
    sb.AppendLine("        var url = new string(urlBuilder.AsReadOnlySpan());");
}
```

> Note: `HeapPooledListExtensions.Append` is called with fully-qualified name because it's an `internal` extension — the generated code lives in the user's assembly, not in `ZeroAlloc.Rest`, so extension method resolution won't find it automatically. Use the static call syntax.

**Step 5: Run generator tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ZeroAlloc.Rest.Generator.Tests.csproj 2>&1 | tail -10
```

Expected: all pass (19+2 new = 21 tests).

**Step 6: Run all tests**

```bash
dotnet test ZeroAlloc.Rest.sln -c Release 2>&1 | tail -10
```

Expected: all pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs
git commit -m "feat(generator): emit Result<T,HttpError> wrapping and HeapPooledList<char> URL builder"
```

---

### Task 5: Update integration tests to use `Result<T, HttpError>`

**Files:**
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs`
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs`

**Step 1: Inspect the current test interface**

Read `tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs` to see the current return types.

**Step 2: Add a `Result`-returning method to the test interface**

In `IUserApi.cs`, add a new method alongside the existing ones:

```csharp
[Get("/users/{id}/result")]
Task<ZeroAlloc.Results.Result<UserDto, ZeroAlloc.Rest.HttpError>> GetUserResultAsync(
    int id, CancellationToken ct = default);
```

**Step 3: Add integration test for `Result<T, HttpError>`**

Add to `UserApiTests.cs`:

```csharp
[Fact]
public async Task GetUser_WithResult_Success_ReturnsSuccessResult()
{
    _server.Given(Request.Create().WithPath("/users/1/result").UsingGet())
           .RespondWith(Response.Create()
               .WithStatusCode(200)
               .WithHeader("Content-Type", "application/json")
               .WithBody(JsonSerializer.Serialize(new UserDto(1, "Alice"), s_camelCase)));

    var result = await _client.GetUserResultAsync(1);
    Assert.True(result.IsSuccess);
    Assert.Equal(1, result.Value.Id);
    Assert.Equal("Alice", result.Value.Name);
}

[Fact]
public async Task GetUser_WithResult_NotFound_ReturnsFailureResult()
{
    _server.Given(Request.Create().WithPath("/users/99/result").UsingGet())
           .RespondWith(Response.Create()
               .WithStatusCode(404)
               .WithHeader("Content-Type", "application/json")
               .WithBody(""));

    var result = await _client.GetUserResultAsync(99);
    Assert.False(result.IsSuccess);
    Assert.Equal(System.Net.HttpStatusCode.NotFound, result.Error.StatusCode);
}
```

**Step 4: Add `ZeroAlloc.Results` reference to integration test project**

In `tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj` add:

```xml
<PackageReference Include="ZeroAlloc.Results" />
```

**Step 5: Run integration tests**

```bash
dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ZeroAlloc.Rest.Integration.Tests.csproj 2>&1 | tail -10
```

Expected: all integration tests pass (4 existing + 2 new = 6).

**Step 6: Commit**

```bash
git add tests/ZeroAlloc.Rest.Integration.Tests/
git commit -m "test(integration): add Result<T,HttpError> integration tests"
```

---

### Task 6: Fix ZeroAlloc.Analyzers diagnostics

**Files:** Various — determined at runtime by the analyzer output.

**Step 1: Build and collect all analyzer diagnostics**

```bash
cd c:/Projects/Prive/ZeroAlloc.Rest
dotnet build ZeroAlloc.Rest.sln -c Release 2>&1 | grep "warning ZA\|error ZA" | sort | uniq
```

List every unique `ZA####` rule that fires. These are the `ZeroAlloc.Analyzers` rules.

**Step 2: For each diagnostic, fix the underlying code**

Common expected fixes based on the codebase:

**String building in emitters** — `ClientEmitter.cs` and `DiEmitter.cs` use `StringBuilder.Append()` in tight loops. The analyzer may suggest using `string.Create` or span-based appending. Since these run at *compile time* inside the generator (not at runtime), allocation is acceptable. Add a targeted suppression per file with a comment:

```csharp
// Suppress: ZA string-building rules do not apply to compile-time code generation
```

**Async state machine elision** — if any method does `return await SomeAsync()` as the only await, the analyzer suggests removing `async`/`await`. Example fix:

```csharp
// Before:
public async Task<string> GetAsync() => await _client.GetStringAsync(url);
// After:
public Task<string> GetAsync() => _client.GetStringAsync(url);
```

**Static lambda caching** — lambdas captured in hot paths that can be made `static`. The analyzer will point to specific lines.

**Boxing warnings** — if any value type is passed to an `object` parameter in a hot loop.

For each diagnostic:
1. Read the diagnostic rule description (`ZA####`)
2. Fix the code as the rule suggests
3. Run build to confirm that specific warning is gone
4. Do NOT add `NoWarn` unless the code genuinely cannot be fixed (e.g., it's a generator internal where compile-time allocation is intentional)

**Step 3: Run all tests after fixes**

```bash
dotnet test ZeroAlloc.Rest.sln -c Release 2>&1 | tail -10
```

Expected: all tests still pass.

**Step 4: Commit**

```bash
git add -A
git commit -m "fix(analyzers): resolve ZeroAlloc.Analyzers diagnostics throughout codebase"
```

---

### Task 7: Update documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/advanced.md`
- Modify: `docs/getting-started.md` (if it mentions `ApiResponse<T>`)

**Step 1: Update README.md features list**

Find the `ApiResponse<T>` bullet and replace:

```markdown
- **`ApiResponse<T>`** — access status code and headers alongside the deserialized body
```

With:

```markdown
- **`Result<T, HttpError>`** — zero-alloc `readonly struct` result type from ZeroAlloc.Results; access status code, headers, and value without exceptions
```

**Step 2: Update `docs/advanced.md`**

Replace the entire `## ApiResponse<T>` section with:

```markdown
## Result&lt;T, HttpError&gt;

When you need the HTTP status code or response headers alongside the body — or want to handle non-OK responses without exceptions — change the return type from `Task<T>` to `Task<Result<T, HttpError>>`:

```csharp
[Get("/users/{id}")]
Task<Result<UserDto, HttpError>> GetUserAsync(int id, CancellationToken ct = default);
```

`Result<T, HttpError>` is a `readonly struct` from `ZeroAlloc.Results` — no heap allocation on either the success or failure path.

```csharp
var result = await api.GetUserAsync(1);

if (result.IsSuccess)
{
    Console.WriteLine(result.Value.Name);
}
else
{
    Console.WriteLine($"Error {result.Error.StatusCode}: {result.Error.Message}");
}

// Or with combinators:
var name = result.Map(u => u.Name).GetValueOrDefault("unknown");
```

`HttpError` carries:

```csharp
public sealed record HttpError(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Message = null);
```

`Result<T, HttpError>` does **not** call `EnsureSuccessStatusCode`. Non-2xx responses are returned as `Failure(error)`.
```

**Step 3: Run full build and tests**

```bash
dotnet build ZeroAlloc.Rest.sln -c Release 2>&1 | tail -5
dotnet test ZeroAlloc.Rest.sln --no-build -c Release 2>&1 | tail -5
```

Expected: 0 errors, all tests pass.

**Step 4: Commit**

```bash
git add README.md docs/advanced.md docs/getting-started.md
git commit -m "docs: update ApiResponse<T> references to Result<T, HttpError>"
```

---

## Final verification

```bash
cd c:/Projects/Prive/ZeroAlloc.Rest
dotnet build ZeroAlloc.Rest.sln -c Release 2>&1 | grep -E "error|Error|warning ZA" | head -20
dotnet test ZeroAlloc.Rest.sln -c Release 2>&1 | tail -10
git log --oneline -8
```

Expected: 0 errors, 0 `ZA` analyzer warnings, all tests pass.
