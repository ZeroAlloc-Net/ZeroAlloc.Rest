---
id: native-aot
title: Native AOT
slug: /native-aot
sidebar_position: 6
description: AOT safety guarantees, RequiresDynamicCode annotations, and publish configuration.
---

# Native AOT

## What is Native AOT?

Native AOT compiles your .NET application to a self-contained native binary at publish time. There is no JIT compiler at runtime — all code paths must be statically reachable at compile time. This means **no reflection, no `DynamicMethod`, no IL emit, and no runtime type generation**.

## ZeroAlloc.Rest's AOT guarantee

The generated client classes (`UserApiClient`, etc.) contain **no runtime reflection**. The Roslyn source generator resolves all type information at compile time and emits plain C# code. The generator itself (`ZeroAlloc.Rest.Generator`) targets `netstandard2.0` and runs inside the compiler process, not at runtime.

The `IsAotCompatible=true` property on `ZeroAlloc.Rest.csproj` enables the SDK's AOT compatibility analysis.

## Serialization and AOT

Serializers are the one area where AOT requires care. The `IRestSerializer` interface methods carry `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]` annotations, which the generated client re-emits:

```csharp
[RequiresDynamicCode("Serialization of arbitrary types may require dynamic code.")]
[RequiresUnreferencedCode("Serialization of arbitrary types may require unreferenced code.")]
public async Task<UserDto> GetUserAsync(int id, CancellationToken ct = default)
{ ... }
```

These are warnings, not errors. To suppress them in an AOT application, use a serializer with source-generated AOT support (e.g. `System.Text.Json` with `[JsonSerializable]`):

```csharp
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(CreateUserRequest))]
internal partial class AppJsonContext : JsonSerializerContext { }
```

Then create a serializer adapter that uses the AOT-safe source-generated context instead of the reflection-based default.

## Publishing as Native AOT

Add to your application `.csproj`:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

Publish:

```sh
dotnet publish -c Release -r linux-x64
```

The output is a single self-contained native binary with no .NET runtime dependency.

## AOT checklist

- [ ] Use a source-generated `JsonSerializerContext` (or MemoryPack / MessagePack which have AOT-safe source generators)
- [ ] Suppress `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` warnings after verifying your serializer is AOT-safe
- [ ] Set `PublishAot=true` in the publish profile
- [ ] Test the native binary on the target OS — trim analysis may surface missing roots
