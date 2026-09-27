---
id: migrating-to-v3
title: Migrating to 3.0
slug: /migrating-to-v3
sidebar_position: 12
description: ZeroAlloc.Rest 3.0 generates models, typed members and Result returns from OpenAPI specs, and makes IRestSerializer AOT-neutral.
---

# Migrating to 3.0

## Who is affected

- You generate clients with `zeroalloc generate` or `ZeroAlloc.Rest.Tools.MSBuild`: every section applies.
- You implement `IRestSerializer`: see [Serializer annotations](#irestserializer-carries-no-trim-annotations).
- You construct `SystemTextJsonSerializer` in a trimmed or Native AOT app: see [the constructors](#systemtextjsonserializer-reflection-constructors-are-annotated).
- You pass `DateTime`, `double`, `bool` or enum values in routes, queries or headers: see [value formatting](#route-query-and-header-values-are-written-invariantly).

## Generated methods return `Result`

Before, a generated method returned `Task<User>` and threw on a non-success status. Now it returns
`Task<Result<User, HttpError>>`, or `Task<UnitResult<HttpError>>` when the operation has no success
body:

```csharp
// 2.x
var user = await api.GetUserAsync(1);

// 3.0
var result = await api.GetUserAsync(1);
if (result.IsFailure)
    return Problem(result.Error.StatusCode);
var user = result.Value;
```

A non-success status, a transport failure, a timeout and a body that cannot be read, an unknown enum
value included, come back as an `HttpError`. Cancellation you asked for and a request body that
cannot be serialized still throw; see [What still throws](advanced.md#what-still-throws).

## An empty or null success body is no longer a null `T`

In 2.x, a 2xx response with an empty body, such as a 204, or a body of JSON `null` came back as a
`null` typed as a non-nullable `T`: `Success(null)` from a `Result<T, E>` method, `null` from a
`Task<T>` method. In 3.0 a method whose `T` does not accept null reports it instead: a `Result`
method returns a `Deserialization` failure, and a `Task<T>` method throws `InvalidOperationException`.
An empty body read as a non-nullable value type, such as `int`, is reported the same way instead of
reading as `0`. Declare `T?` where an empty body is a valid answer; see
[Empty and null success bodies](advanced.md#empty-and-null-success-bodies). A method generated from an
OpenAPI spec declares `T?` itself when the operation can also succeed with no body, such as 200 and
204, or when its response schema is `nullable`. An interface compiled without nullable annotations,
under `#nullable disable`, has no `T?` to declare: its reference types count as non-nullable, so
enable nullable annotations for it and declare `Pet?` to accept an empty body.

## Bodies and parameters are typed

`[Body] object body` becomes `[Body] Pet body` for a JSON body. Parameters follow their schema:
`int64` is `long`, `date-time` is `DateTimeOffset`, `uuid` is `Guid`, and an enum is a generated
`enum`. Optional query and header parameters are nullable. Update the call sites the compiler flags.

## Models are generated

Each schema the interface references now has a generated type, in the same namespace as the
interface. If you wrote those DTOs yourself, the build reports duplicate types. Either delete yours
and use the generated ones, or keep yours:

```sh
zeroalloc generate --spec openapi.yaml --namespace MyApp --output Generated/IMyApi.g.cs --models false
```

```xml
<ZeroAllocApiSpec Include="openapi.yaml" Namespace="MyApp" OutputPath="Generated/IMyApi.g.cs" GenerateModels="false" />
```

## Use the generated JSON context

The file ends with `{Name}JsonContext`. Pass it to the serializer:

```csharp
services.AddIMyApi(o => o.UseSerializer(new SystemTextJsonSerializer(MyApiJsonContext.Default)));
```

## `IRestSerializer` carries no trim annotations

`IRestSerializer.SerializeAsync` and `DeserializeAsync` no longer carry `[RequiresDynamicCode]` or
`[RequiresUnreferencedCode]`, and generated clients no longer carry `[UnconditionalSuppressMessage]`.
Remove the two attributes from your implementation's methods: the trim analyzer reports a mismatch
otherwise. If your serializer needs reflection, put the attributes on its constructor instead.

## Reflection-based serializer constructors are annotated

`new SystemTextJsonSerializer()` and `new SystemTextJsonSerializer(options)` now carry
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so a trimmed or AOT build warns where they
are called. Use `new SystemTextJsonSerializer(context)` or `new SystemTextJsonSerializer(resolver, options)`.

Both `MessagePackRestSerializer` constructors, `new MessagePackRestSerializer()` and
`new MessagePackRestSerializer(options)`, carry the same two attributes: the standard resolver builds
formatters with reflection, and the constructor cannot tell whether your options fall back to it.
`MemoryPackRestSerializer` carries neither; it needs its types registered, below.

## MemoryPackRestSerializer needs its types registered

`MemoryPackRestSerializer` serves only the types registered with it plus MemoryPack's built-in
types; `new MemoryPackRestSerializer()` serves only the built-ins. Pass a delegate that registers
every `[MemoryPackable]` type you serialize with it, through `T.RegisterFormatter()`, MemoryPack's
own generated method:

```csharp
services.AddSingleton(new MemoryPackRestSerializer(types => types.Add<User>()));
```

A type you never registered, and that MemoryPack has no built-in formatter for, throws
`InvalidOperationException` naming the type. A client using
`[Serializer(typeof(MemoryPackRestSerializer))]` registers the configured instance in DI before
`Add{Interface}` runs.

## Route, query and header values are written invariantly

Values are no longer written with `ToString()` in the current culture:

| Value | 2.x | 3.0 |
|---|---|---|
| `bool` | `True` | `true` |
| `double` under de-DE | `1,5` | `1.5` |
| `DateTimeOffset`, `DateTime`, `DateOnly`, `TimeOnly` | culture format | ISO 8601, `"O"` |
| enum member with `[JsonStringEnumMemberName("x")]` | member name | `x` |
| a `[Flags]` combination | member names joined by culture rules | the names System.Text.Json writes, joined with `, ` |
| an enum value no member names | the member's declared name or a culture-formatted number | the value's underlying number, invariant |
| `null` header | empty header | no header |

If a server relied on the old form, pass a `string` parameter formatted the way it expects.
