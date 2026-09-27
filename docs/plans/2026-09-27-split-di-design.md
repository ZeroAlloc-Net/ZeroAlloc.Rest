# Split DI out of ZeroAlloc.Rest core (#336), design

Status: approved by the maintainer, 2026-09-27. Ships in Rest 3.0 with the OpenAPI work of #363.

## Problem

`ZeroAlloc.Rest` depends on `Microsoft.Extensions.Http`, `ZeroAlloc.Serialisation` and
`ZeroAlloc.ValueObjects`. `Microsoft.Extensions.Http` alone brings DI, Logging, Configuration,
Options, Diagnostics and Primitives: 17 transitive packages in total. A library such as
ZeroAlloc.Jev that builds its generated client by hand, `new JevApiClient(httpClient, serializer,
errorMapper)`, uses none of it but still ships all of it to its consumers and raises their version
floors.

## Packages after the split

### `ZeroAlloc.Rest` (core)

Keeps: the attributes, `HttpError`, `HttpErrorKind`, `HttpErrorExtensions`, `IHttpErrorMapper<T>`,
`IRestSerializer`, `GeneratedRestClient.ReadErrorBodyAsync`, the internal `HeapPooledList` helpers,
the bundled generator and `build/ZeroAlloc.Rest.targets`.

Package dependencies: `ZeroAlloc.Results` and `ZeroAlloc.Collections` only. `ZeroAlloc.Analyzers`
and `Microsoft.CodeAnalysis.PublicApiAnalyzers` stay `PrivateAssets="all"`. Nothing from
`Microsoft.Extensions`.

Removed from core:

| Member | Where it goes |
| --- | --- |
| `ServiceCollectionExtensions`: `AddZeroAllocClient`, both `AddRestSerializer`, `DefaultClientName` | DI package |
| `ZeroAllocClientBuilder` | DI package |
| `ZeroAllocClientOptions` | DI package |
| `IGeneratedRestClient<TSelf>` | DI package |
| `RestSerializerServiceProviderExtensions.GetRequiredRestSerializer<TClient>` and the internal `PerClientRestSerializer<T>` | DI package |
| `GeneratedRestClient.Create<TClient>`, `.AddSerializers<TClient>`, `.AddPerClientSerializer<TInterface>` | DI package, on a new class `GeneratedRestClientRegistration` |
| `RestSerializerAdapter<T>` | deleted, with the `ZeroAlloc.Serialisation` dependency |
| `ZeroAlloc.ValueObjects` dependency | deleted; core never used it |

### `ZeroAlloc.Rest.DependencyInjection` (new)

`src/ZeroAlloc.Rest.DependencyInjection`, `IsAotCompatible=true`, public API tracked with
`PublicAPI.Shipped.txt` (empty) and `PublicAPI.Unshipped.txt`. It references core with a
`ProjectReference`, which becomes a package dependency on the same version, and
`Microsoft.Extensions.Http`. It does not bundle the generator.

It contains the moved types above, and `DependencyInjectionMarker`, the type the generator looks for.

**Namespaces.** Every moved type keeps the `ZeroAlloc.Rest` namespace, so an app that used
`Add{I}()` or `AddRestSerializer` only adds the package reference: no `using` changes, no renames.
Two assemblies cannot both declare `ZeroAlloc.Rest.GeneratedRestClient`, which is why the DI helpers
move to a class of their own, `ZeroAlloc.Rest.GeneratedRestClientRegistration`. Only generated code
calls it, and it is `[EditorBrowsable(Never)]` like `GeneratedRestClient`.

The marker is `public static class ZeroAlloc.Rest.DependencyInjection.DependencyInjectionMarker`,
`[EditorBrowsable(Never)]`, with no members. It is public so `GetTypeByMetadataName` resolves it from
any referencing compilation, and it lives in its own namespace so nobody imports it by accident.

### `ZeroAlloc.Rest.Resilience`

Audited: the package has exactly one public type, `RestResilienceServiceCollectionExtensions`, whose
only member, `AddRestResilience`, is an `IServiceCollection` extension built on
`IGeneratedRestClient<TSelf>` and `ZeroAllocClientOptions`. There is no DI-free policy wiring to keep
apart, so the whole package moves behind the DI package: its `ProjectReference` changes from core to
`ZeroAlloc.Rest.DependencyInjection`. Its source does not change. An app that references
`ZeroAlloc.Rest.Resilience` gets the DI package, and with it the marker, transitively.

### Serializer packages and tools

`ZeroAlloc.Rest.SystemTextJson`, `.MemoryPack` and `.MessagePack` use only `IRestSerializer`; they
keep depending on core alone. The tools packages are unaffected.

## Generator

`RestClientGenerator.Initialize` adds one value-equatable input:

```csharp
var dependencyInjection = context.CompilationProvider
    .Select(static (c, _) => c.GetTypeByMetadataName(DependencyInjectionMarkerName) is not null)
    .WithTrackingName("DependencyInjectionEnabled");
```

and combines it with the client models, `clientModels.Combine(dependencyInjection)`. The pair is a
`ClientModel`, already value-equatable since #319, and a `bool`, so the cache still hits: an edit
anywhere else re-runs the `Select` but yields an equal `bool`, and the output step is `Cached` or
`Unchanged`.

With the marker, the output is today's output, byte for byte, except that the generated calls into
`GeneratedRestClient.Create`, `.AddSerializers` and `.AddPerClientSerializer` now name
`GeneratedRestClientRegistration`.

Without the marker:

- `ClientEmitter` emits the client class implementing only the interface, not
  `IGeneratedRestClient<TSelf>`, and skips `EmitGeneratedClientMembers`: no `Create`, no
  `AddSerializers`;
- `DiEmitter` is not called, so there is no `{I}.DI.g.cs`;
- the constructor is unchanged: `HttpClient`, `IRestSerializer`, one `IRestSerializer` per
  method-level `[Serializer]` type, one `IHttpErrorMapper<E>` per mapped error type;
- the output names nothing in `Microsoft.Extensions`.

An interface-level `[Serializer]` or `[ErrorMapper]` still shapes the constructor and the methods
without DI; only the service resolution of those types is DI-specific.

## Tests

- **Generator, with and without the marker.** A new `GeneratorDependencyInjectionTests`: with the
  marker the client implements `IGeneratedRestClient<>` and `{I}.DI.g.cs` exists; without it neither,
  no generated source contains `Microsoft.Extensions`, and the output compiles with only core,
  Results, Collections and the BCL referenced. Existing tests that assert DI output reference the DI
  assembly.
- **Caching.** `GeneratorCachingTests` asserts that `DependencyInjectionEnabled` is tracked and
  `Cached` or `Unchanged` after an unrelated edit, and adds a case that adding the marker
  reference changes the output.
- **Package dependencies.** A test in `ZeroAlloc.Rest.DuplicateGeneratorTests`, which already reads
  `artifacts/local`, opens the packed nuspecs and asserts that `ZeroAlloc.Rest` depends on exactly
  `ZeroAlloc.Results` and `ZeroAlloc.Collections`, and `ZeroAlloc.Rest.DependencyInjection` on exactly
  `ZeroAlloc.Rest` and `Microsoft.Extensions.Http`.
- **Package consumer.** In the same project, a consumer scaffolded against the packed core package
  alone builds, runs a generated client over a stub handler, and has no `Microsoft.Extensions` library
  in its `project.assets.json`; adding the DI package makes `Add{I}` available.
- **Core-only sample, JIT and AOT.** `samples/ZeroAlloc.Rest.CoreOnly.AotSmoke` references core and
  the generator only, constructs a client by hand with a small source-generated System.Text.Json
  serializer, and sends a request over a stub handler. An MSBuild target in the sample fails the build
  if any resolved reference is a `Microsoft.Extensions` assembly. CI runs it under JIT in the build job
  and publishes it with `PublishAot=true` in a new `aot-smoke-core` job that fails on any `IL` warning.
- **DI tests.** A new `tests/ZeroAlloc.Rest.DependencyInjection.Tests` takes
  `ServiceCollectionExtensionsTests` from `ZeroAlloc.Rest.Tests`; the integration, Resilience and
  AotSmoke projects reference the DI package; all pass unchanged.
- **api-compat.** Generated with ApiCompat in suppression-file mode against the 2.2.0 baseline of
  each package, then annotated with a header comment and a justification per entry, in the style of
  Mediator 6.0 and Outbox 4.0. The 2.0 `CP0021` entry is dropped if unnecessary against 2.2.0. Only
  the moved and removed types are listed.

## Pipelines

- The DI project is added to `ZeroAlloc.Rest.slnx`, so the solution-level `Pack` steps in `ci.yml`
  and `release-please.yml` and the discovery loop in `publish-from-manifest.yml` pick it up.
- The three "pack into local feed" steps also pack the DI package, for the consumer tests.
- New `aot-smoke-core` job in `ci.yml`.

## Migration and docs

`docs/migrating-to-v3.md` gets a section: apps that call `Add{I}()`, `AddZeroAllocClient`,
`AddRestSerializer` or `UseSerializer` add `ZeroAlloc.Rest.DependencyInjection`; nothing else
changes. Apps on `ZeroAlloc.Rest.Resilience` get it transitively. Libraries that build the client by
hand need nothing. `RestSerializerAdapter<T>` is gone: the guide shows a short hand-written adapter
and points to the context-based `SystemTextJsonSerializer`.

Every doc, sample and snippet that registers clients gains the package: `README.md`,
`docs/getting-started.md`, `docs/dependency-injection.md`, `docs/serialization.md`,
`docs/resilience.md`, `docs/native-aot.md`, `docs/testing.md`, `docs/advanced.md`, the cookbook and
any other page the audit finds. `docs/dependency-injection.md` explains the split and the
hand-constructed path.

## Out of this repo

ZeroAlloc.Templates references `ZeroAlloc.Rest.Resilience` in both templates, so it receives the DI
package transitively and needs no change for the split itself. ZeroAlloc.Jev builds its client by
hand and benefits without change. No other org repo references ZeroAlloc.Rest.
