# Backlog Enhancements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement three generator enhancements: static method-level `[Header]`, multi-value `[Query]` collections, and form-encoded `[FormBody]`.

**Architecture:** Each enhancement follows the same three-layer pattern — attribute definition in `ZeroAlloc.Rest`, model extraction in `ModelExtractor`, code emission in `ClientEmitter`. All changes are tested at two levels: generator emission tests (assert on generated source text) and integration tests (assert on real HTTP behaviour via WireMock).

**Tech Stack:** C# 10 / .NET 10, Roslyn source generators (netstandard2.0 target), xUnit, WireMock.Net

---

## Task 1: Static `[Header]` on methods

The `HeaderAttribute` already has a `Value` property but the generator never reads it when the attribute is placed on a *method* (only on parameters). This task wires it up.

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`
- Modify: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs`
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs`

---

### Step 1: Write the failing emission test

Add to `GeneratorEmissionTests.cs`:

```csharp
[Fact]
public void StaticHeader_OnMethod_EmittedInGeneratedCode()
{
    var source = """
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IFileApi
        {
            [Get("/files/{id}")]
            [Header("Accept", Value = "application/octet-stream")]
            System.Threading.Tasks.Task<string> GetFileAsync(int id, System.Threading.CancellationToken ct = default);
        }
        """;
    var output = GetGeneratedSource(source, "IFileApi.g.cs");
    Assert.Contains("\"Accept\"", output);
    Assert.Contains("\"application/octet-stream\"", output);
    Assert.Contains("TryAddWithoutValidation", output);
}
```

### Step 2: Run test to verify it fails

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q --filter "StaticHeader_OnMethod_EmittedInGeneratedCode"
```
Expected: FAIL — the generated source won't contain the static header.

### Step 3: Add `StaticHeaders` to `MethodModel`

Replace the record in `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs`:

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
    IReadOnlyList<(string Name, string Value)> StaticHeaders);
```

### Step 4: Extract static headers in `ModelExtractor.ExtractMethod`

In `ModelExtractor.cs`, after the existing `httpMethod`/`route` extraction loop, add (before the early return):

```csharp
var staticHeaders = new List<(string, string)>();
foreach (var attr in method.GetAttributes())
{
    if (attr.AttributeClass?.ToDisplayString() != HeaderAttr) continue;
    if (attr.ConstructorArguments.Length == 0) continue;
    var headerName = attr.ConstructorArguments[0].Value as string;
    if (headerName is null) continue;
    string? headerValue = null;
    foreach (var namedArg in attr.NamedArguments)
    {
        if (namedArg.Key == "Value" && namedArg.Value.Value is string v)
        {
            headerValue = v;
            break;
        }
    }
    if (headerValue != null)
        staticHeaders.Add((headerName, headerValue));
}
```

Then update the `return new MethodModel(...)` call to pass `staticHeaders.AsReadOnly()` as the last argument.

### Step 5: Emit static headers in `ClientEmitter.EmitRequestCreation`

In `ClientEmitter.cs`, inside `EmitRequestCreation`, after the `Accept` header line and before the dynamic header params loop:

```csharp
foreach (var (name, value) in method.StaticHeaders)
    sb.AppendLine($"        request.Headers.TryAddWithoutValidation(\"{name}\", \"{value}\");");
```

Also update `EmitMethod` to pass `method.StaticHeaders` — it's part of `method` so no signature change needed there.

### Step 6: Run emission test to verify it passes

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q --filter "StaticHeader_OnMethod_EmittedInGeneratedCode"
```
Expected: PASS

### Step 7: Run the full generator test suite

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q
```
Expected: All 19 (now 20) tests pass.

### Step 8: Add integration test interface method

In `tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs`, add to `IUserApi`:

```csharp
[Get("/users/{id}/raw")]
[Header("Accept", Value = "application/octet-stream")]
Task<string> GetUserRawAsync(int id, CancellationToken ct = default);
```

### Step 9: Write failing integration test

Add to `UserApiTests.cs`:

```csharp
[Fact]
public async Task StaticHeader_SentWithRequest()
{
    _server.Given(Request.Create()
                .WithPath("/users/1/raw")
                .WithHeader("Accept", "application/octet-stream")
                .UsingGet())
           .RespondWith(Response.Create()
               .WithStatusCode(200)
               .WithHeader("Content-Type", "application/json")
               .WithBody("\"raw-data\""));

    var result = await _client.GetUserRawAsync(1);
    Assert.Equal("raw-data", result);
}
```

### Step 10: Run integration test to verify it passes

```
dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ -q --filter "StaticHeader_SentWithRequest"
```
Expected: PASS

### Step 11: Commit

```bash
git add src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs \
        src/ZeroAlloc.Rest.Generator/ModelExtractor.cs \
        src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
        tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs \
        tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs \
        tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs
git commit -m "feat(generator): emit static [Header] values on methods"
```

---

## Task 2: Multi-value query parameters (`IEnumerable<T>` with `[Query]`)

Today `[Query] IEnumerable<string> tags` emits `.ToString()` which produces `"System.Collections.Generic.List\`1[…]"`. This task makes it emit repeated `?tags=a&tags=b` pairs.

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`
- Modify: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs`
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs`

---

### Step 1: Write the failing emission test

Add to `GeneratorEmissionTests.cs`:

```csharp
[Fact]
public void QueryParam_Collection_EmitsForEachLoop()
{
    var source = """
        using System.Collections.Generic;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface ISearchApi
        {
            [Get("/items")]
            System.Threading.Tasks.Task<string> SearchAsync(
                [Query] IEnumerable<string> tags,
                System.Threading.CancellationToken ct = default);
        }
        """;
    var output = GetGeneratedSource(source, "ISearchApi.g.cs");
    Assert.Contains("foreach", output);
    Assert.Contains("\"tags=\"", output);
}
```

### Step 2: Run test to verify it fails

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q --filter "QueryParam_Collection_EmitsForEachLoop"
```
Expected: FAIL

### Step 3: Add `IsCollection` flag to `ParameterModel`

In `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs`:

```csharp
namespace ZeroAlloc.Rest.Generator.Models;

internal enum ParameterKind { Path, Query, Body, FormBody, Header, CancellationToken }

internal record ParameterModel(
    string Name,
    string TypeName,
    ParameterKind Kind,
    string? HeaderName = null,
    string? QueryName = null,
    bool IsNullable = true,
    bool IsCollection = false);
```

### Step 4: Detect `IEnumerable<T>` in `ModelExtractor.ExtractParameters`

In `ModelExtractor.cs`, after setting `kind = ParameterKind.Query` and resolving `queryName`, add:

```csharp
// Detect IEnumerable<T> (but not string — string is IEnumerable<char>)
bool isCollection = false;
if (kind == ParameterKind.Query && param.Type.SpecialType != Microsoft.CodeAnalysis.SpecialType.System_String)
{
    foreach (var iface in param.Type.AllInterfaces)
    {
        if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
        {
            isCollection = true;
            break;
        }
    }
}
```

Then pass `isCollection` to the `ParameterModel` constructor (last positional arg).

### Step 5: Emit foreach loop in `ClientEmitter.EmitUrlBuilding`

In `ClientEmitter.cs`, inside the `foreach (var q in queryParams)` loop, replace the existing nullable/non-nullable branches with a three-way check:

```csharp
if (q.IsCollection)
{
    sb.AppendLine($"        if ({q.Name} != null)");
    sb.AppendLine($"        {{");
    sb.AppendLine($"            foreach (var __item in {q.Name})");
    sb.AppendLine($"            {{");
    sb.AppendLine($"                AppendToUrl(urlBuilder, hasQuery ? '&' : '?');");
    sb.AppendLine($"                AppendToUrl(urlBuilder, \"{q.QueryName}=\".AsSpan());");
    sb.AppendLine($"                AppendToUrl(urlBuilder, System.Uri.EscapeDataString(__item?.ToString() ?? string.Empty).AsSpan());");
    sb.AppendLine($"                hasQuery = true;");
    sb.AppendLine($"            }}");
    sb.AppendLine($"        }}");
}
else if (q.IsNullable)
{
    // existing nullable branch
}
else
{
    // existing non-nullable branch
}
```

Also update `hasQueryParams` detection at the top of `Emit` — the `HeapPooledList` using is already triggered by any `ParameterKind.Query`, so no change needed there.

### Step 6: Run emission test to verify it passes

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q --filter "QueryParam_Collection_EmitsForEachLoop"
```
Expected: PASS

### Step 7: Run the full generator test suite

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q
```
Expected: All tests pass.

### Step 8: Add integration test interface method

In `IUserApi.cs`, add:

```csharp
[Get("/users")]
Task<List<UserDto>> ListUsersByTagsAsync([Query] IEnumerable<string> tags, CancellationToken ct = default);
```

### Step 9: Write failing integration test

Add to `UserApiTests.cs`:

```csharp
[Fact]
public async Task ListUsers_WithCollectionQuery_RepeatsKey()
{
    _server.Given(Request.Create()
                .WithPath("/users")
                .WithParam("tags", "admin", "active")
                .UsingGet())
           .RespondWith(Response.Create()
               .WithStatusCode(200)
               .WithHeader("Content-Type", "application/json")
               .WithBody(JsonSerializer.Serialize(new List<UserDto> { new(1, "Alice") }, s_camelCase)));

    var result = await _client.ListUsersByTagsAsync(["admin", "active"]);
    Assert.Single(result);
}
```

### Step 10: Run integration test to verify it passes

```
dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ -q --filter "ListUsers_WithCollectionQuery_RepeatsKey"
```
Expected: PASS

### Step 11: Commit

```bash
git add src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs \
        src/ZeroAlloc.Rest.Generator/ModelExtractor.cs \
        src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
        tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs \
        tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs \
        tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs
git commit -m "feat(generator): support IEnumerable<T> [Query] params as repeated keys"
```

---

## Task 3: Form-encoded body (`[FormBody]`)

Add a `[FormBody]` attribute that accepts a `Dictionary<string, string>` (or any `IEnumerable<KeyValuePair<string, string>>`). The emitter produces `new FormUrlEncodedContent(param)` — fully AOT-safe, no reflection.

**Files:**
- Modify: `src/ZeroAlloc.Rest/Attributes/ParameterAttributes.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs` (add `FormBody` to enum — done in Task 2)
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`
- Modify: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs`
- Modify: `tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs`

---

### Step 1: Write the failing emission test

Add to `GeneratorEmissionTests.cs`:

```csharp
[Fact]
public void FormBody_EmitsFormUrlEncodedContent()
{
    var source = """
        using System.Collections.Generic;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface ITokenApi
        {
            [Post("/oauth/token")]
            System.Threading.Tasks.Task<string> GetTokenAsync(
                [FormBody] Dictionary<string, string> form,
                System.Threading.CancellationToken ct = default);
        }
        """;
    var output = GetGeneratedSource(source, "ITokenApi.g.cs");
    Assert.Contains("FormUrlEncodedContent", output);
    Assert.DoesNotContain("SerializeAsync", output);
}
```

### Step 2: Run test to verify it fails

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q --filter "FormBody_EmitsFormUrlEncodedContent"
```
Expected: FAIL — `FormBodyAttribute` doesn't exist yet.

### Step 3: Add `FormBodyAttribute`

In `src/ZeroAlloc.Rest/Attributes/ParameterAttributes.cs`, add:

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FormBodyAttribute : Attribute { }
```

### Step 4: Add `FormBody` to `ParameterKind` (if not done in Task 2)

In `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs`, `ParameterKind` should already have `FormBody` from Task 2. Confirm it reads:

```csharp
internal enum ParameterKind { Path, Query, Body, FormBody, Header, CancellationToken }
```

### Step 5: Add `FormBodyAttr` constant and detection in `ModelExtractor`

In `ModelExtractor.cs`, add the constant alongside the others:

```csharp
private const string FormBodyAttr = "ZeroAlloc.Rest.Attributes.FormBodyAttribute";
```

In `ExtractParameters`, inside the per-attribute loop, add detection after the `BodyAttr` check:

```csharp
if (attrClass == FormBodyAttr)
{
    kind = ParameterKind.FormBody;
    break;
}
```

### Step 6: Emit `FormUrlEncodedContent` in `ClientEmitter.EmitRequestCreation`

In `ClientEmitter.cs`, add a helper to find the form body parameter (alongside `FirstOrDefault` for `Body`):

```csharp
var formBodyParam = FirstOrDefault(method.Parameters, ParameterKind.FormBody);
```

Then in `EmitRequestCreation`, add after the existing `bodyParam` block:

```csharp
if (formBodyParam != null)
{
    sb.AppendLine($"        request.Content = new System.Net.Http.FormUrlEncodedContent({formBodyParam.Name});");
}
```

And update the `EmitMethod` call to pass `formBodyParam` through — or simply call `FirstOrDefault` inside `EmitRequestCreation` directly since it already receives `method`.

The cleanest change: replace the `bodyParam` parameter with two parameters in `EmitRequestCreation`:

```csharp
private static void EmitRequestCreation(StringBuilder sb, MethodModel method,
    List<ParameterModel> headerParams, ParameterModel? bodyParam, ParameterModel? formBodyParam,
    string ctArg, string serializerExpr)
```

And in `EmitMethod`:

```csharp
var formBodyParam = FirstOrDefault(method.Parameters, ParameterKind.FormBody);
// ...
EmitRequestCreation(sb, method, headerParams, bodyParam, formBodyParam, ctArg, serializerExpr);
```

### Step 7: Run emission test to verify it passes

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q --filter "FormBody_EmitsFormUrlEncodedContent"
```
Expected: PASS

### Step 8: Run the full generator test suite

```
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ -q
```
Expected: All tests pass.

### Step 9: Add integration test interface method

In `IUserApi.cs`, add:

```csharp
[Post("/oauth/token")]
Task<UserDto> GetTokenAsync([FormBody] Dictionary<string, string> form, CancellationToken ct = default);
```

### Step 10: Write failing integration test

Add to `UserApiTests.cs`:

```csharp
[Fact]
public async Task FormBody_SendsFormEncodedContent()
{
    _server.Given(Request.Create()
                .WithPath("/oauth/token")
                .WithBody(b => b.Contains("grant_type=client_credentials"))
                .UsingPost())
           .RespondWith(Response.Create()
               .WithStatusCode(200)
               .WithHeader("Content-Type", "application/json")
               .WithBody(JsonSerializer.Serialize(new UserDto(1, "token"), s_camelCase)));

    var result = await _client.GetTokenAsync(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = "my-app"
    });
    Assert.Equal(1, result.Id);
}
```

### Step 11: Run integration test to verify it passes

```
dotnet test tests/ZeroAlloc.Rest.Integration.Tests/ -q --filter "FormBody_SendsFormEncodedContent"
```
Expected: PASS

### Step 12: Run all tests

```
dotnet test tests/ -q
```
Expected: All tests pass.

### Step 13: Commit

```bash
git add src/ZeroAlloc.Rest/Attributes/ParameterAttributes.cs \
        src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs \
        src/ZeroAlloc.Rest.Generator/ModelExtractor.cs \
        src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
        tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs \
        tests/ZeroAlloc.Rest.Integration.Tests/TestInterfaces/IUserApi.cs \
        tests/ZeroAlloc.Rest.Integration.Tests/UserApiTests.cs
git commit -m "feat(generator): add [FormBody] attribute for form-encoded requests"
```
