# ZeroAlloc.Rest: Fixes & Benchmarks Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix four code-quality issues found in the final review and add BenchmarkDotNet benchmarks comparing ZeroAlloc.Rest against Refit and raw `HttpClient`.

**Architecture:** Each fix is isolated to one or two files in the generator or tools project. The benchmark project is a new `Exe` project that uses an in-memory `HttpMessageHandler` to isolate library overhead from network overhead.

**Tech Stack:** .NET 10, Roslyn incremental generators, BenchmarkDotNet 0.14.0, Refit 7.2.1, System.Text.Json.

---

## Task 1: Fix non-nullable query param null guard

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`

**Bug:** `[Query] int page` emits `if (page != null)` which is always true and triggers CS0472 under `TreatWarningsAsErrors=true` — a build error in user projects. The null guard must be skipped for non-nullable value types.

**Step 1: Write two failing tests in `GeneratorEmissionTests.cs`**

Add these two methods before the closing `}` of the class (before the `GetGeneratedSource` helper):

```csharp
[Fact]
public void QueryParam_NonNullableValueType_NoNullGuard()
{
    var source = """
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface ISearchApi
        {
            [Get("/items")]
            System.Threading.Tasks.Task<string> SearchAsync(
                [Query] int page,
                System.Threading.CancellationToken ct = default);
        }
        """;
    var output = GetGeneratedSource(source, "ISearchApi.g.cs");
    Assert.DoesNotContain("if (page != null)", output);
    Assert.Contains("urlBuilder.Append(\"page=\")", output);
}

[Fact]
public void QueryParam_NullableValueType_HasNullGuard()
{
    var source = """
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface ISearchApi
        {
            [Get("/items")]
            System.Threading.Tasks.Task<string> SearchAsync(
                [Query] int? page,
                System.Threading.CancellationToken ct = default);
        }
        """;
    var output = GetGeneratedSource(source, "ISearchApi.g.cs");
    Assert.Contains("if (page != null)", output);
}
```

**Step 2: Run the new tests to verify they fail**

```bash
cd c:/Projects/Prive/ZeroAlloc.Rest
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ --filter "QueryParam"
```

Expected: `QueryParam_NonNullableValueType_NoNullGuard` FAILS because `if (page != null)` IS present.

**Step 3: Add `IsNullable` to `ParameterModel`**

Replace the entire content of `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs`:

```csharp
namespace ZeroAlloc.Rest.Generator.Models;

internal enum ParameterKind { Path, Query, Body, Header, CancellationToken }

internal record ParameterModel(
    string Name,
    string TypeName,
    ParameterKind Kind,
    string? HeaderName = null,
    string? QueryName = null,
    bool IsNullable = true);
```

**Step 4: Compute `IsNullable` in `ModelExtractor.ExtractParameters`**

In `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`, replace line 150 (the final `result.Add` in `ExtractParameters`):

Old:
```csharp
result.Add(new ParameterModel(param.Name, typeName, kind, headerName, queryName ?? param.Name));
```

New:
```csharp
// Non-nullable value types (int, bool, Guid…) can never be null at runtime.
// Nullable value types (int?) have OriginalDefinition == System.Nullable<T>.
bool isNullable = !param.Type.IsValueType
    || param.Type.OriginalDefinition.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Nullable_T;
result.Add(new ParameterModel(param.Name, typeName, kind, headerName, queryName ?? param.Name, isNullable));
```

**Step 5: Update `ClientEmitter.EmitUrlBuilding` to only emit the null guard when `IsNullable`**

In `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`, replace line 107:

Old:
```csharp
sb.AppendLine($"        if ({q.Name} != null) urlBuilder.Append(\"{q.QueryName}=\").Append(Uri.EscapeDataString({q.Name}.ToString())).Append('&');");
```

New:
```csharp
if (q.IsNullable)
    sb.AppendLine($"        if ({q.Name} != null) urlBuilder.Append(\"{q.QueryName}=\").Append(Uri.EscapeDataString({q.Name}!.ToString()!)).Append('&');");
else
    sb.AppendLine($"        urlBuilder.Append(\"{q.QueryName}=\").Append(Uri.EscapeDataString({q.Name}.ToString())).Append('&');");
```

(The `!` null-forgiving operators suppress CS8602 on the nullable-reference-type path.)

**Step 6: Run all tests**

```bash
dotnet test ZeroAlloc.Rest.sln
```

Expected: All 63 tests PASS (61 existing + 2 new).

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs \
        src/ZeroAlloc.Rest.Generator/ModelExtractor.cs \
        src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
        tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs
git commit -m "fix: skip null guard for non-nullable value type query parameters"
```

---

## Task 2: Add Accept header to all requests

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`

**Bug:** No `Accept` header is sent. The design doc states `UseSerializer<T>()` sets the `Accept` header automatically. The `GetAsync` fast path also bypasses request creation, making it impossible to add the header there. Fix: always use `SendAsync` and set `Accept` on every request.

**IMPORTANT:** The existing test `GeneratedMethod_ContainsHttpGetCall` on line 52-66 asserts `Assert.Contains("GetAsync", output)`. That test must be updated to check for `SendAsync` when the fast path is removed.

**Step 1: Write a failing test in `GeneratorEmissionTests.cs`**

Add before the `GetGeneratedSource` helper:

```csharp
[Fact]
public void GeneratedMethod_SetsAcceptHeader()
{
    var source = """
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        [ZeroAllocRestClient]
        public interface IUserApi
        {
            [Get("/users")]
            System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct = default);
        }
        """;
    var output = GetGeneratedSource(source, "IUserApi.g.cs");
    Assert.Contains("request.Headers.Accept.Add", output);
    Assert.Contains("_serializer.ContentType", output);
}
```

**Step 2: Run to verify failure**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ --filter "AcceptHeader"
```

Expected: FAIL — `Accept` not in generated output.

**Step 3: Update `ClientEmitter.cs` — remove `GetAsync` fast path and add Accept header**

Replace `EmitMethod` (lines 50-82) with:

```csharp
private static void EmitMethod(StringBuilder sb, MethodModel method)
{
    var ctParam = FindCancellationToken(method.Parameters);
    var ctArg = ctParam != null ? ctParam.Name : "default";
    var pathParams = FilterParameters(method.Parameters, ParameterKind.Path);
    var queryParams = FilterParameters(method.Parameters, ParameterKind.Query);
    var bodyParam = FirstOrDefault(method.Parameters, ParameterKind.Body);
    var headerParams = FilterParameters(method.Parameters, ParameterKind.Header);

    sb.AppendLine("    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(\"Serialization of arbitrary types may require dynamic code.\")]");
    sb.AppendLine("    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(\"Serialization of arbitrary types may require unreferenced code.\")]");
    sb.AppendLine($"    public async {method.ReturnTypeName} {method.Name}({BuildParamList(method.Parameters)})");
    sb.AppendLine("    {");

    EmitUrlBuilding(sb, method.Route, pathParams, queryParams);
    EmitRequestCreation(sb, method, headerParams, bodyParam, ctArg);
    EmitSendAndResponse(sb, method, ctArg);

    sb.AppendLine("    }");
    sb.AppendLine();
}
```

Delete the now-unused `EmitGetAsyncAndResponse` method (lines 148-152) entirely.

Replace `EmitRequestCreation` (lines 114-140) with:

```csharp
private static void EmitRequestCreation(StringBuilder sb, MethodModel method,
    List<ParameterModel> headerParams, ParameterModel? bodyParam, string ctArg)
{
    sb.AppendLine($"        using var request = new System.Net.Http.HttpRequestMessage(");
    sb.AppendLine($"            System.Net.Http.HttpMethod.{Capitalize(method.HttpMethod)},");
    sb.AppendLine($"            url);");
    sb.AppendLine("        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(_serializer.ContentType));");

    foreach (var h in headerParams)
        sb.AppendLine($"        request.Headers.TryAddWithoutValidation(\"{h.HeaderName}\", {h.Name}?.ToString());");

    if (bodyParam != null)
    {
        sb.AppendLine("        var bodyStream = new System.IO.MemoryStream();");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine($"            await _serializer.SerializeAsync(bodyStream, {bodyParam.Name}, {ctArg}).ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch");
        sb.AppendLine("        {");
        sb.AppendLine("            bodyStream.Dispose();");
        sb.AppendLine("            throw;");
        sb.AppendLine("        }");
        sb.AppendLine("        bodyStream.Position = 0;");
        sb.AppendLine("        request.Content = new System.Net.Http.StreamContent(bodyStream);");
        sb.AppendLine("        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_serializer.ContentType);");
    }
}
```

**Step 4: Update the broken existing test `GeneratedMethod_ContainsHttpGetCall`**

In `GeneratorEmissionTests.cs`, find the test at line ~52:

```csharp
Assert.Contains("GetAsync", output);
```

Change to:

```csharp
Assert.Contains("SendAsync", output);
```

**Step 5: Run all tests**

```bash
dotnet test ZeroAlloc.Rest.sln
```

Expected: All 64 tests PASS.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
        tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs
git commit -m "fix: send Accept header on all requests, remove GetAsync fast path"
```

---

## Task 3: Per-method `[Serializer]` override

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/DiEmitter.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorDiTests.cs`

**Bug:** `[Serializer(typeof(T))]` on a method is extracted by `ModelExtractor` into `MethodModel.SerializerTypeName` but neither `ClientEmitter` nor `DiEmitter` uses it — the annotation is silently ignored.

**Fix:** For each unique serializer type used in method-level `[Serializer]` annotations, inject a private field and a constructor parameter in the generated client class, and register the concrete type in DI.

**Step 1: Write failing tests**

In `GeneratorEmissionTests.cs`, add:

```csharp
[Fact]
public void MethodLevelSerializer_UsesOverrideSerializerField()
{
    var source = """
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        public class OverrideSerializer : IRestSerializer
        {
            public string ContentType => "application/octet-stream";
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
                => ValueTask.FromResult<T?>(default);
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
                => ValueTask.CompletedTask;
        }
        [ZeroAllocRestClient]
        public interface IUploadApi
        {
            [Post("/upload")]
            [Serializer(typeof(OverrideSerializer))]
            Task UploadAsync([Body] string data, CancellationToken ct = default);
        }
        """;
    var output = GetGeneratedSource(source, "IUploadApi.g.cs");
    Assert.Contains("_overrideSerializer", output);
    Assert.Contains("MyApp.OverrideSerializer overrideSerializer", output);
}
```

In `GeneratorDiTests.cs`, add:

```csharp
[Fact]
public void MethodLevelSerializer_RegistersOverrideTypeInDI()
{
    var source = """
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        public class OverrideSerializer : IRestSerializer
        {
            public string ContentType => "application/octet-stream";
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
                => ValueTask.FromResult<T?>(default);
            [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("")]
            [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("")]
            public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
                => ValueTask.CompletedTask;
        }
        [ZeroAllocRestClient]
        public interface IUploadApi
        {
            [Post("/upload")]
            [Serializer(typeof(OverrideSerializer))]
            Task UploadAsync([Body] string data, CancellationToken ct = default);
        }
        """;
    var output = RunAndGetSources(source);
    var diFile = output.Single(f => f.HintName == "IUploadApi.DI.g.cs");
    var content = diFile.SourceText.ToString();
    Assert.Contains("TryAddSingleton<MyApp.OverrideSerializer>", content);
}
```

**Step 2: Run to verify failure**

```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests/ --filter "MethodLevelSerializer"
```

Expected: Both tests FAIL.

**Step 3: Add `GetSerializerFieldName` helper to `ClientEmitter.cs`**

Add this private static method after the `Capitalize` method (after line 192):

```csharp
/// <summary>
/// Derives the private field name for an override serializer from its fully-qualified type name.
/// "MyApp.OverrideSerializer" → "_overrideSerializer"
/// </summary>
private static string GetSerializerFieldName(string fullTypeName)
{
    var simpleName = fullTypeName.Contains('.')
        ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
        : fullTypeName;
    return "_" + char.ToLower(simpleName[0]) + simpleName.Substring(1);
}
```

**Step 4: Update `ClientEmitter.Emit` to collect override serializers and emit fields/constructor params**

Replace the `Emit` method (lines 10-48) with:

```csharp
internal static void Emit(SourceProductionContext ctx, ClientModel model)
{
    // Collect unique method-level serializer types (preserving order, deduped)
    var overrideSerializers = new List<string>();
    foreach (var m in model.Methods)
    {
        if (m.SerializerTypeName != null && !overrideSerializers.Contains(m.SerializerTypeName))
            overrideSerializers.Add(m.SerializerTypeName);
    }

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Net.Http;");
    sb.AppendLine("using System.Net.Http.Headers;");
    sb.AppendLine("using System.Threading;");
    sb.AppendLine("using System.Threading.Tasks;");
    sb.AppendLine("using ZeroAlloc.Rest;");
    sb.AppendLine();

    if (!string.IsNullOrEmpty(model.Namespace))
    {
        sb.AppendLine($"namespace {model.Namespace};");
        sb.AppendLine();
    }

    sb.AppendLine($"public sealed partial class {model.ClassName} : {model.InterfaceName}");
    sb.AppendLine("{");
    sb.AppendLine("    private readonly System.Net.Http.HttpClient _httpClient;");
    sb.AppendLine("    private readonly ZeroAlloc.Rest.IRestSerializer _serializer;");
    foreach (var st in overrideSerializers)
        sb.AppendLine($"    private readonly {st} {GetSerializerFieldName(st)};");
    sb.AppendLine();

    // Build constructor parameter list
    var ctorParams = new System.Collections.Generic.List<string>
    {
        "System.Net.Http.HttpClient httpClient",
        "ZeroAlloc.Rest.IRestSerializer serializer"
    };
    foreach (var st in overrideSerializers)
    {
        var fieldName = GetSerializerFieldName(st);
        ctorParams.Add($"{st} {fieldName.Substring(1)}"); // strip leading '_' for param name
    }

    sb.AppendLine($"    public {model.ClassName}({string.Join(", ", ctorParams)})");
    sb.AppendLine("    {");
    sb.AppendLine("        _httpClient = httpClient;");
    sb.AppendLine("        _serializer = serializer;");
    foreach (var st in overrideSerializers)
    {
        var fieldName = GetSerializerFieldName(st);
        sb.AppendLine($"        {fieldName} = {fieldName.Substring(1)};");
    }
    sb.AppendLine("    }");
    sb.AppendLine();

    foreach (var method in model.Methods)
        EmitMethod(sb, method);

    sb.AppendLine("}");
    ctx.AddSource($"{model.InterfaceName}.g.cs", sb.ToString());
}
```

**Step 5: Update `EmitMethod` to compute and use `serializerExpr`**

At the top of `EmitMethod`, after computing `headerParams`, add:

```csharp
// If the method has its own [Serializer], use that field; otherwise fall back to _serializer.
var serializerExpr = method.SerializerTypeName != null
    ? GetSerializerFieldName(method.SerializerTypeName)
    : "_serializer";
```

Then pass `serializerExpr` to `EmitRequestCreation` and `EmitSendAndResponse`.

Update `EmitRequestCreation` signature to accept `serializerExpr`:

```csharp
private static void EmitRequestCreation(StringBuilder sb, MethodModel method,
    List<ParameterModel> headerParams, ParameterModel? bodyParam, string ctArg, string serializerExpr)
```

Replace all `_serializer` references inside `EmitRequestCreation` with `{serializerExpr}`.

The Accept line becomes:
```csharp
sb.AppendLine($"        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue({serializerExpr}.ContentType));");
```

The body serialization line becomes:
```csharp
sb.AppendLine($"            await {serializerExpr}.SerializeAsync(bodyStream, {bodyParam.Name}, {ctArg}).ConfigureAwait(false);");
```

The `Content-Type` line becomes:
```csharp
sb.AppendLine($"        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue({serializerExpr}.ContentType);");
```

Update `EmitSendAndResponse` to accept and forward `serializerExpr`:

```csharp
private static void EmitSendAndResponse(StringBuilder sb, MethodModel method, string ctArg, string serializerExpr)
{
    sb.AppendLine($"        using var response = await _httpClient.SendAsync(request, {ctArg}).ConfigureAwait(false);");
    EmitResponseHandling(sb, method, ctArg, serializerExpr);
}
```

Update `EmitResponseHandling` to accept and use `serializerExpr`:

```csharp
private static void EmitResponseHandling(StringBuilder sb, MethodModel method, string ctArg, string serializerExpr)
{
    if (method.ReturnsVoid)
    {
        sb.AppendLine("        response.EnsureSuccessStatusCode();");
    }
    else if (method.ReturnsApiResponse)
    {
        sb.AppendLine($"        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);");
        sb.AppendLine($"        var content = await {serializerExpr}.DeserializeAsync<{method.InnerTypeName}>(responseStream, {ctArg}).ConfigureAwait(false);");
        sb.AppendLine("        var headers = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>();");
        sb.AppendLine("        foreach (var header in response.Headers)");
        sb.AppendLine("            headers[header.Key] = new System.Collections.Generic.List<string>(header.Value).AsReadOnly();");
        sb.AppendLine($"        return new ZeroAlloc.Rest.ApiResponse<{method.InnerTypeName}>(content, response.StatusCode, headers);");
    }
    else
    {
        sb.AppendLine("        response.EnsureSuccessStatusCode();");
        sb.AppendLine($"        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);");
        sb.AppendLine($"        return (await {serializerExpr}.DeserializeAsync<{method.InnerTypeName}>(responseStream, {ctArg}).ConfigureAwait(false))!;");
    }
}
```

Also update the call sites in `EmitMethod`:
```csharp
EmitRequestCreation(sb, method, headerParams, bodyParam, ctArg, serializerExpr);
EmitSendAndResponse(sb, method, ctArg, serializerExpr);
```

**Step 6: Update `DiEmitter.cs` to register override serializer types**

In `src/ZeroAlloc.Rest.Generator/DiEmitter.cs`, replace the `Emit` method with:

```csharp
internal static void Emit(SourceProductionContext ctx, ClientModel model)
{
    // Collect unique method-level serializer types
    var overrideSerializers = new List<string>();
    foreach (var m in model.Methods)
    {
        if (m.SerializerTypeName != null && !overrideSerializers.Contains(m.SerializerTypeName))
            overrideSerializers.Add(m.SerializerTypeName);
    }

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
    sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
    sb.AppendLine("using ZeroAlloc.Rest;");
    sb.AppendLine();

    if (!string.IsNullOrEmpty(model.Namespace))
    {
        sb.AppendLine($"namespace {model.Namespace};");
        sb.AppendLine();
    }

    sb.AppendLine("public static partial class GeneratedRestClientExtensions");
    sb.AppendLine("{");
    sb.AppendLine($"    public static IHttpClientBuilder Add{model.InterfaceName}(");
    sb.AppendLine($"        this IServiceCollection services,");
    sb.AppendLine($"        System.Action<ZeroAllocClientOptions>? configure = null)");
    sb.AppendLine("    {");
    sb.AppendLine("        var options = new ZeroAllocClientOptions();");
    sb.AppendLine("        configure?.Invoke(options);");
    sb.AppendLine("        if (options.SerializerType is not null)");
    sb.AppendLine("            services.TryAddSingleton(typeof(IRestSerializer), options.SerializerType);");
    foreach (var st in overrideSerializers)
        sb.AppendLine($"        services.TryAddSingleton<{st}>();");
    sb.AppendLine($"        return services.AddHttpClient<{model.InterfaceName}, {model.ClassName}>(client =>");
    sb.AppendLine("        {");
    sb.AppendLine("            if (options.BaseAddress is not null)");
    sb.AppendLine("                client.BaseAddress = options.BaseAddress;");
    sb.AppendLine("        });");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    ctx.AddSource($"{model.InterfaceName}.DI.g.cs", sb.ToString());
}
```

**Step 7: Run all tests**

```bash
dotnet test ZeroAlloc.Rest.sln
```

Expected: All 66 tests PASS (64 + 2 new).

**Step 8: Commit**

```bash
git add src/ZeroAlloc.Rest.Generator/ClientEmitter.cs \
        src/ZeroAlloc.Rest.Generator/DiEmitter.cs \
        tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs \
        tests/ZeroAlloc.Rest.Generator.Tests/GeneratorDiTests.cs
git commit -m "feat: implement per-method [Serializer] override"
```

---

## Task 4: OpenAPI codegen improvements

**Files:**
- Modify: `src/ZeroAlloc.Rest.Tools/OpenApiInterfaceGenerator.cs`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/OpenApiInterfaceGeneratorTests.cs`

**Issues:**
1. `Task<object>` hardcoded — should map response schema types using `$ref` names and schema type
2. Parse errors silently discarded via `out _`
3. `ToPascalCase` only uppercases the first letter — snake_case/kebab-case operation IDs get broken identifiers

**Step 1: Write failing tests in `OpenApiInterfaceGeneratorTests.cs`**

Add these three tests to the existing test class:

```csharp
[Fact]
public void Generate_WithObjectResponseRef_MapsTypeName()
{
    var yaml = """
        openapi: 3.0.0
        info:
          title: Test
          version: "1"
        paths:
          /users/{id}:
            get:
              operationId: getUser
              parameters:
                - in: path
                  name: id
                  schema:
                    type: integer
              responses:
                '200':
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/UserDto'
        components:
          schemas:
            UserDto:
              type: object
        """;
    var result = OpenApiInterfaceGenerator.Generate(yaml, "MyApp", "IMyApi");
    Assert.Contains("Task<UserDto>", result);
    Assert.DoesNotContain("Task<object>", result);
}

[Fact]
public void Generate_WithArrayResponse_MapsListType()
{
    var yaml = """
        openapi: 3.0.0
        info:
          title: Test
          version: "1"
        paths:
          /users:
            get:
              operationId: listUsers
              responses:
                '200':
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          $ref: '#/components/schemas/UserDto'
        components:
          schemas:
            UserDto:
              type: object
        """;
    var result = OpenApiInterfaceGenerator.Generate(yaml, "MyApp", "IMyApi");
    Assert.Contains("Task<List<UserDto>>", result);
}

[Fact]
public void Generate_WithSnakeCaseOperationId_ProducesPascalCaseMethodName()
{
    var yaml = """
        openapi: 3.0.0
        info:
          title: Test
          version: "1"
        paths:
          /users:
            get:
              operationId: get_all_users
              responses:
                '200':
                  description: ok
        """;
    var result = OpenApiInterfaceGenerator.Generate(yaml, "MyApp", "IMyApi");
    Assert.Contains("GetAllUsersAsync", result);
}
```

**Step 2: Run to verify failure**

```bash
dotnet test tests/ZeroAlloc.Rest.Tools.Tests/ --filter "WithObjectResponse|WithArrayResponse|WithSnakeCase"
```

Expected: All three FAIL.

**Step 3: Replace `OpenApiInterfaceGenerator.cs`**

Replace the entire file with:

```csharp
using System.Text;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace ZeroAlloc.Rest.Tools;

public static class OpenApiInterfaceGenerator
{
    public static string Generate(string yamlOrJson, string @namespace, string interfaceName)
    {
        var reader = new OpenApiStringReader();
        var document = reader.Read(yamlOrJson, out var diagnostic);

        if (document == null || diagnostic.Errors.Count > 0)
        {
            var errors = diagnostic.Errors.Count > 0
                ? string.Join("; ", diagnostic.Errors.Select(e => e.Message))
                : "Failed to parse OpenAPI document.";
            throw new InvalidOperationException($"OpenAPI parse errors: {errors}");
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using ZeroAlloc.Rest.Attributes;");
        sb.AppendLine();
        sb.AppendLine($"namespace {@namespace};");
        sb.AppendLine();
        sb.AppendLine("[ZeroAllocRestClient]");
        sb.AppendLine($"public interface {interfaceName}");
        sb.AppendLine("{");

        if (document.Paths != null)
        {
            foreach (var (path, pathItem) in document.Paths)
                foreach (var (operationType, operation) in pathItem.Operations)
                    EmitMethod(sb, path, operationType, operation);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string GenerateFromFile(string filePath, string @namespace, string interfaceName)
    {
        var content = File.ReadAllText(filePath);
        return Generate(content, @namespace, interfaceName);
    }

    public static async Task<string> GenerateFromFileAsync(
        string filePath, string @namespace, string interfaceName,
        CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return Generate(content, @namespace, interfaceName);
    }

    public static async Task<string> GenerateFromUrlAsync(
        string url, string @namespace, string interfaceName,
        CancellationToken ct = default)
    {
        using var http = new HttpClient();
        var content = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        return Generate(content, @namespace, interfaceName);
    }

    private static void EmitMethod(StringBuilder sb, string path,
        OperationType operationType, OpenApiOperation operation)
    {
        var httpAttr = operationType switch
        {
            OperationType.Get    => "Get",
            OperationType.Post   => "Post",
            OperationType.Put    => "Put",
            OperationType.Patch  => "Patch",
            OperationType.Delete => "Delete",
            _ => null
        };
        if (httpAttr is null) return;

        sb.AppendLine($"    [{httpAttr}(\"{path}\")]");

        var methodName = ToPascalCase(operation.OperationId
            ?? $"{httpAttr}{path.Replace("/", "_").Replace("{", "").Replace("}", "")}") + "Async";

        var parameters = new List<string>();

        if (operation.Parameters != null)
        {
            foreach (var param in operation.Parameters)
            {
                var typeName = MapSchemaType(param.Schema);
                var paramName = ToCamelCase(param.Name);
                var paramStr = param.In switch
                {
                    ParameterLocation.Query  => $"[Query] {typeName} {paramName}",
                    ParameterLocation.Header => $"[Header(\"{param.Name}\")] string {paramName}",
                    _ => $"{typeName} {paramName}"
                };
                parameters.Add(paramStr);
            }
        }

        if (operation.RequestBody != null)
            parameters.Add("[Body] object body");

        parameters.Add("CancellationToken ct = default");

        var returnType = GetReturnType(operation);
        sb.AppendLine($"    {returnType} {methodName}({string.Join(", ", parameters)});");
        sb.AppendLine();
    }

    private static string GetReturnType(OpenApiOperation operation)
    {
        foreach (var (statusCode, response) in operation.Responses)
        {
            if (!statusCode.StartsWith("2", StringComparison.Ordinal)) continue;
            if (response.Content == null || response.Content.Count == 0) return "Task";
            foreach (var (_, content) in response.Content)
            {
                if (content.Schema == null) continue;
                return $"Task<{MapSchemaTypeForReturn(content.Schema)}>";
            }
        }
        return "Task";
    }

    private static string MapSchemaTypeForReturn(OpenApiSchema schema)
    {
        if (schema.Type == "array" && schema.Items != null)
            return $"List<{MapSchemaTypeForReturn(schema.Items)}>";
        if (schema.Reference != null)
            return ToPascalCase(schema.Reference.Id);
        return schema.Type switch
        {
            "integer" => "int",
            "number"  => "double",
            "boolean" => "bool",
            "string"  => "string",
            _         => "object"
        };
    }

    private static string MapSchemaType(OpenApiSchema? schema) => schema?.Type switch
    {
        "integer" => "int",
        "number"  => "double",
        "boolean" => "bool",
        _ => "string"
    };

    /// <summary>Converts snake_case, kebab-case, or plain strings to PascalCase.</summary>
    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var parts = s.Split('_', '-');
        var result = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            result.Append(char.ToUpper(part[0]));
            result.Append(part.Substring(1));
        }
        return result.Length > 0 ? result.ToString() : s;
    }

    private static string ToCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToLower(s[0]) + s.Substring(1);
    }
}
```

**Important note:** `OpenApiStringReader` in v1.6.28 may NOT populate `diagnostic.Errors` for completely invalid content — it may return an empty document instead. If the invalid-spec test fails with no exception, change the null check to:

```csharp
if (document?.Paths == null && (diagnostic.Errors.Count > 0 || document == null))
```

Or check `document == null` only:

```csharp
if (document == null)
    throw new InvalidOperationException("Failed to parse OpenAPI document.");
if (diagnostic.Errors.Count > 0)
{
    var errors = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
    throw new InvalidOperationException($"OpenAPI parse errors: {errors}");
}
```

Run the invalid-spec test in isolation first to see what actually happens, then adjust accordingly.

**Step 4: Run all tests**

```bash
dotnet test ZeroAlloc.Rest.sln
```

Expected: All 69 tests PASS (66 + 3 new).

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Rest.Tools/OpenApiInterfaceGenerator.cs \
        tests/ZeroAlloc.Rest.Tools.Tests/OpenApiInterfaceGeneratorTests.cs
git commit -m "fix: map OpenAPI response types, expose parse errors, fix PascalCase for snake_case operation IDs"
```

---

## Task 5: BenchmarkDotNet benchmarks

**Files:**
- Create: `tests/ZeroAlloc.Rest.Benchmarks/ZeroAlloc.Rest.Benchmarks.csproj`
- Create: `tests/ZeroAlloc.Rest.Benchmarks/InMemoryHandler.cs`
- Create: `tests/ZeroAlloc.Rest.Benchmarks/RestClientBenchmarks.cs`
- Create: `tests/ZeroAlloc.Rest.Benchmarks/Program.cs`
- Modify: `Directory.Packages.props`
- Modify: `ZeroAlloc.Rest.sln`

**Context:** This project runs via `dotnet run -c Release`, NOT `dotnet test`. It benchmarks ZeroAlloc.Rest vs Refit vs raw `HttpClient` using an in-memory `HttpMessageHandler` so results measure library overhead only — no network latency.

**Step 1: Add BenchmarkDotNet and Refit to `Directory.Packages.props`**

Inside the existing `<ItemGroup>`, add at the end (before `</ItemGroup>`):

```xml
<!-- Benchmarks -->
<PackageVersion Include="BenchmarkDotNet" Version="0.14.0" />
<PackageVersion Include="Refit" Version="7.2.1" />
```

**Step 2: Create `tests/ZeroAlloc.Rest.Benchmarks/ZeroAlloc.Rest.Benchmarks.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <!-- Benchmarks are not AOT-targeted; suppress AOT call-site warnings -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <ProjectReference Include="../../src/ZeroAlloc.Rest.SystemTextJson/ZeroAlloc.Rest.SystemTextJson.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" />
    <PackageReference Include="Refit" />
    <PackageReference Include="Microsoft.Extensions.Http" />
  </ItemGroup>
</Project>
```

**Step 3: Create `tests/ZeroAlloc.Rest.Benchmarks/InMemoryHandler.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ZeroAlloc.Rest.Benchmarks;

/// <summary>
/// Returns a fixed pre-serialized JSON response for every request.
/// Eliminates network latency so benchmarks measure library overhead only.
/// </summary>
internal sealed class InMemoryHandler : HttpMessageHandler
{
    private readonly byte[] _responseBody;

    public InMemoryHandler(object responseObject)
    {
        _responseBody = JsonSerializer.SerializeToUtf8Bytes(responseObject);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_responseBody)
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        return Task.FromResult(response);
    }
}
```

**Step 4: Create `tests/ZeroAlloc.Rest.Benchmarks/RestClientBenchmarks.cs`**

```csharp
using BenchmarkDotNet.Attributes;
using Refit;
using System.Text.Json;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Benchmarks;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// ── ZeroAlloc.Rest interface — source generator emits ZeroAllocUserApiClient ──

[ZeroAllocRestClient]
public interface IZeroAllocUserApi
{
    [Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id, CancellationToken ct = default);

    [Post("/users")]
    Task<UserDto> CreateUserAsync([Body] UserDto body, CancellationToken ct = default);
}

// ── Refit interface — reflection-based client ─────────────────────────────────

public interface IRefitUserApi
{
    [Refit.Get("/users/{id}")]
    Task<UserDto> GetUserAsync(int id);

    [Refit.Post("/users")]
    Task<UserDto> CreateUserAsync([Refit.Body] UserDto body);
}

// ── Benchmarks ────────────────────────────────────────────────────────────────

[MemoryDiagnoser]
[SimpleJob]
public class RestClientBenchmarks
{
    private static readonly Uri s_baseUri = new("http://localhost/");
    private static readonly UserDto s_testUser = new() { Id = 1, Name = "Alice" };
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private IZeroAllocUserApi _zeroAlloc = null!;
    private IRefitUserApi _refit = null!;
    private HttpClient _rawHttp = null!;

    [GlobalSetup]
    public void Setup()
    {
        var handler = new InMemoryHandler(s_testUser);

        // ZeroAlloc.Rest: instantiate the source-generated client directly
        _zeroAlloc = new ZeroAllocUserApiClient(
            new HttpClient(handler) { BaseAddress = s_baseUri },
            new SystemTextJsonSerializer());

        // Refit: reflection-based client
        _refit = RestService.For<IRefitUserApi>(
            new HttpClient(handler) { BaseAddress = s_baseUri });

        // Raw HttpClient: manual JSON deserialization (true baseline)
        _rawHttp = new HttpClient(handler) { BaseAddress = s_baseUri };
    }

    [GlobalCleanup]
    public void Cleanup() => _rawHttp.Dispose();

    // ── GET benchmarks ────────────────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public async Task<UserDto?> RawHttpClient_Get()
    {
        using var response = await _rawHttp.GetAsync("/users/1").ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<UserDto>(stream, s_jsonOptions)
            .ConfigureAwait(false);
    }

    [Benchmark]
    public Task<UserDto> ZeroAlloc_Get() => _zeroAlloc.GetUserAsync(1);

    [Benchmark]
    public Task<UserDto> Refit_Get() => _refit.GetUserAsync(1);

    // ── POST benchmarks ───────────────────────────────────────────────────────

    [Benchmark]
    public async Task<UserDto?> RawHttpClient_Post()
    {
        var json = JsonSerializer.Serialize(s_testUser, s_jsonOptions);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _rawHttp.PostAsync("/users", content).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<UserDto>(stream, s_jsonOptions)
            .ConfigureAwait(false);
    }

    [Benchmark]
    public Task<UserDto> ZeroAlloc_Post() => _zeroAlloc.CreateUserAsync(s_testUser);

    [Benchmark]
    public Task<UserDto> Refit_Post() => _refit.CreateUserAsync(s_testUser);
}
```

**Step 5: Create `tests/ZeroAlloc.Rest.Benchmarks/Program.cs`**

```csharp
using BenchmarkDotNet.Running;
using ZeroAlloc.Rest.Benchmarks;

BenchmarkRunner.Run<RestClientBenchmarks>();
```

**Step 6: Add the benchmark project to the solution**

```bash
cd c:/Projects/Prive/ZeroAlloc.Rest
dotnet sln add tests/ZeroAlloc.Rest.Benchmarks/ZeroAlloc.Rest.Benchmarks.csproj
```

**Step 7: Build the benchmark project in Release mode**

```bash
dotnet build tests/ZeroAlloc.Rest.Benchmarks/ -c Release
```

Expected: Build succeeded, 0 errors.

**Step 8: Verify the full solution still compiles and all tests pass**

```bash
dotnet build ZeroAlloc.Rest.sln
dotnet test ZeroAlloc.Rest.sln
```

Expected: Build succeeded, all 69 tests PASS. (Benchmark project has no xUnit tests — it runs via `dotnet run`.)

**Step 9: Commit**

```bash
git add tests/ZeroAlloc.Rest.Benchmarks/ Directory.Packages.props ZeroAlloc.Rest.sln
git commit -m "feat: add BenchmarkDotNet benchmarks comparing ZeroAlloc.Rest vs Refit vs HttpClient"
```

To run the benchmarks:

```bash
cd tests/ZeroAlloc.Rest.Benchmarks
dotnet run -c Release
```

---

## Summary

```
✅ Task 1: Fix non-nullable query param null guard (CS0472 build error for int/bool query params)
✅ Task 2: Accept header on all requests + remove GetAsync fast path (design goal from design doc)
✅ Task 3: Per-method [Serializer] override (design feature, previously silently ignored)
✅ Task 4: OpenAPI codegen improvements (Task<object> → proper types, parse errors exposed, snake_case PascalCase)
✅ Task 5: BenchmarkDotNet benchmarks (ZeroAlloc.Rest vs Refit vs raw HttpClient, GET + POST)
```
