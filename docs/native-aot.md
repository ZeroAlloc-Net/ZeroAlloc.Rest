---
id: native-aot
title: Native AOT
slug: /native-aot
sidebar_position: 6
description: AOT safety guarantees, source-generated serializers, and publish configuration.
---

# Native AOT

## What is Native AOT?

Native AOT compiles your .NET application to a self-contained native binary at publish time. There is no JIT compiler at runtime — all code paths must be statically reachable at compile time. This means **no reflection, no `DynamicMethod`, no IL emit, and no runtime type generation**.

## ZeroAlloc.Rest's AOT guarantee

The generated client classes (`UserApiClient`, etc.) contain **no runtime reflection**. The Roslyn source generator resolves all type information at compile time and emits plain C# code. The generator itself (`ZeroAlloc.Rest.Generator`) targets `netstandard2.0` and runs inside the compiler process, not at runtime.

The `IsAotCompatible=true` property on `ZeroAlloc.Rest.csproj` enables the SDK's AOT compatibility analysis.

`ZeroAlloc.Rest` itself depends only on `ZeroAlloc.Results` and `ZeroAlloc.Collections` — nothing
under `Microsoft.Extensions`. A consumer that builds its client by hand, `new MyApiClient(httpClient,
serializer)`, pulls in nothing else. Reference `ZeroAlloc.Rest.DependencyInjection` only when you want
the generated `Add{I}` extension and `IHttpClientFactory` integration; see
[Dependency Injection](dependency-injection.md) and
[samples/ZeroAlloc.Rest.CoreOnly.AotSmoke](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/tree/main/samples/ZeroAlloc.Rest.CoreOnly.AotSmoke)
for a worked core-only AOT publish.

## Serialization and AOT

`IRestSerializer` carries no trim or AOT annotations, and generated clients carry no suppressions,
so what the trimmer sees is exactly what your serializer does. Use one that needs no reflection:

- `SystemTextJsonSerializer` with a `JsonSerializerContext`. A client generated from an OpenAPI spec
  comes with one, `{Name}JsonContext`, covering every request and response type:

  ```csharp
  o.UseSerializer(new SystemTextJsonSerializer(PetStoreClientJsonContext.Default));
  ```

  For a hand-written interface, declare your own:

  ```csharp
  [JsonSerializable(typeof(UserDto))]
  [JsonSerializable(typeof(List<UserDto>))]
  internal partial class AppJsonContext : JsonSerializerContext;
  ```

- MemoryPack, with every `[MemoryPackable]` type registered; see [Serialization](serialization.md#memorypack).

The parameterless and options constructors of `SystemTextJsonSerializer` use reflection and are
marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so the build tells you where. So
are both `MessagePackRestSerializer` constructors: MessagePack's standard resolver uses reflection,
and the constructor cannot tell whether the options you pass fall back to it.

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

On win-x64, ILC links with the MSVC toolchain, so the "Desktop development with C++" Visual Studio
workload must be installed, and ILC also needs the directory of `vswhere.exe` on `PATH`, usually
`C:\Program Files (x86)\Microsoft Visual Studio\Installer`. Otherwise publish under WSL with
`-r linux-x64`.

## AOT checklist

- [ ] Use a source-generated `JsonSerializerContext`, such as the one generated from your OpenAPI spec, or MemoryPack with registered types
- [ ] Construct serializers without reflection: no IL2026 or IL3050 warning should remain; never suppress one
- [ ] Set `PublishAot=true` in the publish profile
- [ ] Test the native binary on the target OS — trim analysis may surface missing roots

MemoryPack needs explicit registration: `new MemoryPackRestSerializer(types => types.Add<User>())`;
see [MemoryPack](serialization.md#memorypack). MemoryPack.Core itself reports IL2104 and IL3053 on
every AOT publish that references it, regardless of registration; this is tracked upstream in
[#357](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/357), and the main AOT smoke keeps no
MemoryPack reference so it stays at 0 IL warnings.
