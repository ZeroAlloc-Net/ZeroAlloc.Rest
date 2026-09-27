# OpenAPI Models and AOT-Safe Serialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `zeroalloc generate` and `ZeroAlloc.Rest.Tools.MSBuild` produce a complete, compiling, AOT-safe client from an OpenAPI 3.0 spec: typed parameters and bodies, generated models, a generated `JsonSerializerContext`, and `Result`-returning methods, plus an AOT-safe `SystemTextJsonSerializer` constructor that takes that context (#351).

**Architecture:** The tool's single `OpenApiInterfaceGenerator` is split into units in `ZeroAlloc.Rest.Tools.Shared`: `TypeMapper` maps a schema to a `TypeRef`, `SchemaModelBuilder` reads the schemas the interface references into a value-equal intermediate model, `ModelEmitter` and `UnionEmitter` write records, enums, polymorphic hierarchies and union wrappers, `JsonContextEmitter` writes the context, and `OperationEmitter` writes the interface methods. On the runtime side, `IRestSerializer` loses its `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` annotations, subject to the Task 1 prototype, so generated clients need no trim suppression, and the Rest source generator learns `UnitResult<E>` returns and wire-formats route, query and header values.

**Tech Stack:** .NET 10, C# latest, Microsoft.OpenApi.Readers 1.6.31, System.Text.Json source generation, Roslyn incremental source generator on netstandard2.0, ZeroAlloc.Results 1.2.3, xUnit 2.9.3, Basic.Reference.Assemblies.Net100, Native AOT.

**Spec:** `docs/plans/2026-09-27-openapi-models-design.md`

## Global Constraints

From the spec and the maintainer:

- **Release:** Rest **3.0.0**. Exactly **one** `feat!` commit, Task 14, carries the whole `BREAKING CHANGE` footer. Every other commit is `feat:`, `fix:`, `test:` or `docs:`.
- **Target:** net10.0 everywhere the repo already targets it; the generator stays netstandard2.0.
- **Warnings:** `TreatWarningsAsErrors` is on, and every build ends with `0 Warning(s) 0 Error(s)`.
- **No analyzer suppressions of any kind.** No `#pragma warning disable`, no `NoWarn`, no `[SuppressMessage]`, no `[UnconditionalSuppressMessage]`, no `.editorconfig` severity change, and no `!` null-forgiving operator in new repository code. C# inside a test's probe string literal, compiled in memory as test input, is exempt. Fix every finding with real code. Pre-existing suppressions in files this plan does not otherwise touch stay as they are.
- **TDD:** every task writes its failing test first and watches it fail for the stated reason.
- **Public API:** every new public member is listed in the project's `PublicAPI.Unshipped.txt`. Projects that ship public API and have no tracking yet get it the first time this plan adds API to them: `ZeroAlloc.Rest.SystemTextJson` in Task 3, `ZeroAlloc.Rest.Tools.MSBuild` in Task 15. The api-compat check must pass without new entries in `apicompat-suppressions.xml`.
- **Diagnostics:** ZRT002 "schema mapped to JsonElement" is a warning, documented in `docs/advanced.md` next to ZRT001.
- **AOT:** the AOT smoke publishes with `PublishAot=true` and reports **0 trim or AOT warnings**.
- **Commits:** conventional; body lines at most 100 characters; no nested parentheses anywhere in a message, so write "an attribute naming typeof X", never the attribute itself with its arguments; no `claude.ai` links and no `Claude-Session:` trailer. Every message ends with:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- **Branch:** merge the spec PR #353 first, docs only, so this work is not stacked on it. Then implement in a worktree `C:\wt\rest-351` on branch `feat/351-openapi-models` from `origin/main`, and run every command from that root.
- **Release order:** the held Rest 2.2.0 (#328) ships before this merges. Task 19 checks it.

**Full-suite command.** Every task ends with it. It mirrors CI, where the package consumer tests read the local feed:

```bash
dotnet build ZeroAlloc.Rest.slnx -c Release -p:Version=3.0.0-dev
dotnet pack src/ZeroAlloc.Rest/ZeroAlloc.Rest.csproj --no-build -c Release -p:Version=3.0.0-dev -o artifacts/local
dotnet pack src/ZeroAlloc.Rest.Generator/ZeroAlloc.Rest.Generator.csproj --no-build -c Release -p:Version=3.0.0-dev -o artifacts/local
dotnet pack src/ZeroAlloc.Rest.Tools.MSBuild/ZeroAlloc.Rest.Tools.MSBuild.csproj --no-build -c Release -p:Version=3.0.0-dev -o artifacts/local
dotnet test ZeroAlloc.Rest.slnx --no-build -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`, then every test project reports `Passed!` with `Failed: 0`.

**AOT smoke command.** Tasks 1, 2 and 18 run it. On Windows, ILC links with the MSVC toolchain, so the Visual Studio "Desktop development with C++" workload must be installed; otherwise run it under WSL with `-r linux-x64`, as CI does. On win-x64, ILC also needs the directory of `vswhere.exe` on `PATH`, usually `C:\Program Files (x86)\Microsoft Visual Studio\Installer`.

```bash
mkdir -p artifacts
dotnet publish samples/ZeroAlloc.Rest.AotSmoke/ZeroAlloc.Rest.AotSmoke.csproj -r win-x64 -c Release -o artifacts/aot 2>&1 | tee artifacts/aot-publish.log
grep -cE "(warning|error) IL[0-9]+" artifacts/aot-publish.log
./artifacts/aot/ZeroAlloc.Rest.AotSmoke.exe
```

Expected: the `grep -c` prints `0`, and the binary prints `AOT smoke: PASS`.

---

## Design decisions this plan resolves

The spec leaves these open. Each resolution binds the tasks below.

1. **The AOT annotation question, spec §7,** is decided by a prototype in Task 1, recorded in the "AOT decision record" section below. The expected outcome is that `IRestSerializer` loses its annotations. A second, annotation-free interface such as `ITypeInfoRestSerializer` cannot reach 0 warnings: a generated client that prefers it must still call the annotated `IRestSerializer` in its fallback branch, and ILC warns at that call site. Only a suppression would hide it. Task 1 states the stop rule if the measurement disagrees.
2. **Where the reflection risk moves.** Once the interface is annotation-free, the risk sits where reflection is chosen. `SystemTextJsonSerializer()` and `SystemTextJsonSerializer(JsonSerializerOptions)` carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. The context and resolver constructors carry nothing. Every serializer method resolves `JsonTypeInfo<T>` through `JsonSerializerOptions.TryGetTypeInfo`, which is not annotated.
3. **Typed parameters need wire formatting.** Mapping `date-time` to `DateTimeOffset`, `boolean` to `bool` and enums to generated enums would regress the wire format. The generator formats values with `ToString()` today, which uses the current culture, writes `True`, and writes an enum's C# name. Task 5 makes the Rest generator format route, query and header values invariantly: ISO 8601 `"O"` for date and time types, `true`/`false` for `bool`, the `[JsonStringEnumMemberName]` value for enums, and `IFormattable.ToString(null, InvariantCulture)` otherwise. It also stops sending an empty header for a null value.
4. **`format: binary` and non-JSON content.** ZeroAlloc.Rest has no raw `Stream` body or response binding. `TypeMapper` maps `binary` to `Stream` in body position, as §5.1 says, but the interface cannot use it yet. A request body with no JSON media type, or one that maps to `Stream`, stays `[Body] object body`, today's behaviour and the spec's non-goal for XML, multipart and form bodies. A success response with no JSON media type, or one that maps to `Stream`, becomes `JsonElement` and reports ZRT002 with the reason. Task 19 files the issue for raw stream bodies and non-JSON responses.
5. **JSON media types** are `application/json`, `text/json`, any `+json` suffix, and `*/*`, which springdoc emits for JSON. Parameters after `;` are ignored.
6. **Which schemas become models.** An enum, a composition, or a schema with properties becomes a model. A named component with `type: object` and no properties and no `additionalProperties` schema becomes an empty record named after it. An inline `type: object` with no properties maps to `Dictionary<string, JsonElement>`. A component that is only a primitive with a format, such as `Id: {type: string, format: uuid}`, maps to the primitive. Only schemas the interface reaches are generated, per spec §2.
7. **Single-part wrappers.** `allOf`, `oneOf` or `anyOf` with exactly one part and no properties, often used to attach `nullable` or a description to a `$ref`, maps to the part itself, not to a new record.
8. **Names.** Inline schemas are named from their path: `User` + `address` gives `UserAddress`; a parameter `status` of `listPets` gives `ListPetsStatus`; an inline request body `CreatePetRequest`; an inline response `GetPetResponse`. An inline union whose variants are all `$ref`s is named after them, `PetOrError`, as in spec §5.6. Every type name also reserves `{Name}Converter`, and the interface, client and context names are reserved, so nothing the tool generates can collide. A property named like its record, or like a member every record has, such as `ToString`, gets the numeric suffix the spec's collision rule gives.
9. **The context name** is the interface name with its leading `I` removed, plus `JsonContext`: `IPetStoreClient` gives `PetStoreClientJsonContext`, as in spec §7's usage. This is the rule the Rest generator uses for the client name, `ModelExtractor.cs:42-44`, which gives `PetStoreClientClient`; both names are reserved for the models.
10. **Fully qualified names.** Model names come from the spec, so a schema called `Task`, `Header`, `Get` or `CancellationToken` would shadow a type the generated file uses. The generated file therefore uses no `using` directives and writes every framework and ZeroAlloc type as `global::`, attributes included. The file also starts with `#nullable enable`, since `<auto-generated/>` code is nullable-oblivious by default.
11. **Strict enums.** The stock `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` reads an integer as an undefined value, which breaks spec §5.3. Each string enum instead gets a generated `{Enum}Converter : JsonStringEnumConverter<T>` that passes `allowIntegerValues: false`. An integer enum gets a generated `JsonConverter<T>` that accepts only its declared values. Both throw `JsonException` for anything else.
12. **Discriminators.** The context sets `AllowOutOfOrderMetadataProperties = true`, so a discriminator need not be the first JSON property. STJ rejects a derived type that declares the discriminator as a property, so variants and the base drop it. A variant claimed by two discriminated bases is a generation error: a C# record has one base type.
13. **Union converters resolve variants through `options.GetTypeInfo`,** which is the generated context at run time and is not annotated. Every union variant type is registered in the context, because a type behind a custom converter is not otherwise reachable by the STJ generator.
14. **Parameter nullability.** A path parameter, or a required query or header parameter, is `T`. An optional one is `T?` with no default value, so parameter order stays the spec's. A request body is `T` when `required: true`, else `T?`.
15. **`NoWarn` "on the item"** in spec §5.7 and §8 means the existing mechanism: the project's `<NoWarn>` for the MSBuild task, and `--nowarn` for the CLI. No per-item `NoWarn` metadata is added.
16. **`--models false`** emits the interface and the JSON context but no models. A `$ref` becomes its bare PascalCase name, as today, so hand-written DTOs in the same namespace satisfy it. An inline object, enum or composition then has no type and maps to `JsonElement` with ZRT002.
17. **Round-trip tests** compile the emitted code in memory with the Rest generator, the STJ generator from the targeting pack, and RouteTemplateAnalyzer, emit it into a collectible `AssemblyLoadContext`, and run a small `Probe.Run()` compiled alongside it. Typed checks stay in C#, and no second test project is needed.
18. **The AOT smoke client** is generated from `samples/ZeroAlloc.Rest.AotSmoke/petstore.yaml` and checked in. A Tools test fails when the checked-in file is not what the tool generates now. The CI `aot-smoke` job stays a plain publish, now with a guard that fails on any IL warning. "A real request against a stub server" is a real loopback `TcpListener`.
19. **One PR, squash-merged, with `BEGIN_COMMIT_OVERRIDE`.** A squash merge keeps only the PR title's changelog entry, so the PR body lists the entries, with the one `feat!` carrying the footer.

## AOT decision record

Measured on 2026-09-27 with SDK 10.0.401, win-x64.

| Step | Configuration | IL warnings |
|---|---|---|
| M0 | today: annotated interface, suppressed generated methods | 0 |
| M1 | suppression removed | IL2026 9, IL3050 9 |
| M2 | interface, `RestSerializerAdapter` and smoke serializer without annotations | 0 |
| M3 | each in-repo serializer with IsAotCompatible, without annotations | Rest 0, STJ 0, MemoryPack 2 (IL2091), MessagePack 0 |
| M4 | M2 plus a context-backed SystemTextJsonSerializer call | 0, then `AOT smoke: PASS` |

`RestSerializerAdapter` moved into the M2 measurement alongside `IRestSerializer`: they share an
assembly, and the C# compiler's interface/override trim-annotation consistency check requires an
implementation to match its interface member exactly, so the two cannot be staged independently.

**Decision: A, with MemoryPack by explicit registration.** `IRestSerializer` and every in-repo
serializer carry no trim annotations, the generated client carries no suppression, and the
reflection-based `SystemTextJsonSerializer` constructors carry `[RequiresUnreferencedCode]` and
`[RequiresDynamicCode]`. `MemoryPackSerializer.Deserialize<T>` carries `DynamicallyAccessedMemberTypes.All`
on `T` in every overload, because MemoryPack finds `RegisterFormatter` by reflection; putting that
annotation on `IRestSerializer` instead was measured at 47 to 74 percent AOT binary growth and 6
IL3050 warnings per array-returning endpoint, so it was rejected, and an unannotated reader path
builds clean but fails at run time under Native AOT. The maintainer chose explicit registration
instead, Task 20. An ITypeInfoRestSerializer was not needed: its fallback call to the annotated
interface would have kept a warning in every generated method.

## File Structure

**Tool, shared source** in `src/ZeroAlloc.Rest.Tools.Shared/`, compiled into the CLI and the MSBuild task. The `.projitems` glob becomes recursive in Task 6.

| File | Responsibility | Task |
|---|---|---|
| `CSharpNames.cs` | Identifiers, PascalCase, keyword escaping, literals, unique names, XML doc comments | 6 |
| `ISchemaTypeNamer.cs` | What `TypeMapper` asks for a schema that needs a generated type | 6 |
| `TypeMapper.cs` | Schema to `TypeRef`, §5.1; `Declare` applies §5.2 nullability | 6 |
| `Models/EquatableList.cs` | Value-equal list for the intermediate model | 6 |
| `Models/TypeRef.cs`, `Models/TypeRefKind.cs` | A C# type reference | 6 |
| `Models/ModelDefinition.cs`, `RecordModel.cs`, `PropertyModel.cs`, `EnumModel.cs`, `EnumMemberModel.cs` | Intermediate model | 7 |
| `Models/PolymorphicModel.cs`, `DerivedTypeModel.cs` | Discriminated hierarchies | 9 |
| `Models/UnionModel.cs`, `UnionVariantModel.cs`, `JsonKind.cs` | Union wrappers | 10 |
| `SchemaModelBuilder.cs` | Naming, collisions, lazy building, allOf, polymorphism, unions | 7 to 10 |
| `ModelEmitter.cs` | Records, enums, enum converters, polymorphic bases | 11, 12 |
| `UnionEmitter.cs` | Union wrappers and their converters | 13 |
| `JsonContextEmitter.cs` | The `JsonSerializerContext` | 11 |
| `OperationEmitter.cs` | One interface method per operation | 14 |
| `ReferenceOnlyNamer.cs` | The namer for `--models false` | 14 |
| `GenerationOptions.cs` | `GenerateModels` | 14 |
| `OpenApiInterfaceGenerator.cs` | Parse, then assemble interface, models and context | 6, 14 |
| `OpenApiWarning.cs` | ZRT002 | 7 |

**Runtime and generator**

| File | Change | Task |
|---|---|---|
| `src/ZeroAlloc.Rest/IRestSerializer.cs`, `RestSerializerAdapter.cs` | No trim annotations | 2 |
| `src/ZeroAlloc.Rest.SystemTextJson/SystemTextJsonSerializer.cs` | TypeInfo resolution; annotated reflection constructors; context and resolver constructors | 2, 3 |
| `src/ZeroAlloc.Rest.SystemTextJson/PublicAPI.*.txt` | New tracking | 3 |
| `src/ZeroAlloc.Rest.MemoryPack/*`, `src/ZeroAlloc.Rest.MessagePack/*` | No trim annotations, `IsAotCompatible` | 2 |
| `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs` | No trim suppression; `UnitResult`; `__FormatValue` helpers | 2, 4, 5 |
| `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs`, `Models/MethodModel.cs`, `Models/ParameterModel.cs`, `Models/ValueFormatModel.cs` | `UnitResult`; value formats | 4, 5 |
| `src/ZeroAlloc.Rest.Tools/Program.cs`, `src/ZeroAlloc.Rest.Tools.MSBuild/*` | `--models`, `GenerateModels` | 15 |

**Tests**

| File | Task |
|---|---|
| `tests/ZeroAlloc.Rest.Tests/Serializers/SerializerAotAnnotationTests.cs` | 2, 3 |
| `tests/ZeroAlloc.Rest.Tests/Serializers/SystemTextJsonContextTests.cs`, `WidgetJsonContext.cs` | 3 |
| `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorUnitResultTests.cs`, `GeneratorValueFormatTests.cs` | 4, 5 |
| `tests/ZeroAlloc.Rest.Integration.Tests/UnitResultTests.cs`, `ValueFormatTests.cs` | 4, 5 |
| `tests/ZeroAlloc.Rest.Tools.Tests/TypeMapperTests.cs` | 6 |
| `tests/ZeroAlloc.Rest.Tools.Tests/ModelFixture.cs`, `SchemaModelBuilderTests.cs` | 7 to 10 |
| `tests/ZeroAlloc.Rest.Tools.Tests/GeneratedCode.cs`, `ModelEmitterTests.cs`, `UnionEmitterTests.cs` | 11 to 13 |
| `tests/ZeroAlloc.Rest.Tools.Tests/OpenApiClientGenerationTests.cs` | 14 |
| `tests/ZeroAlloc.Rest.Tools.Tests/RealWorldSpecTests.cs`, `Specs/*` | 17 |
| `tests/ZeroAlloc.Rest.Tools.Tests/AotSmokeClientTests.cs` | 18 |

---

### Task 1: Prototype the AOT annotations and record the decision

Spec §7 asks for this first, and Tasks 2, 3, 14 and 18 depend on its answer. It is a measurement on a throwaway branch. Only the decision record is committed.

**Files:**
- Spike only, never pushed: worktree `C:\wt\rest-351-aot-spike`, branch `spike/351-aot-annotations`
- Modify: `docs/plans/2026-09-27-openapi-models.md`, the "AOT decision record" section

**Interfaces:**
- Consumes: nothing.
- Produces: the decision, A or stop, that Task 2 applies.

- [ ] **Step 1: Create the spike worktree**

```bash
git -C C:/wt/rest-351 worktree add C:/wt/rest-351-aot-spike -b spike/351-aot-annotations HEAD
cd C:/wt/rest-351-aot-spike
```

- [ ] **Step 2: Measure the baseline**

Run the AOT smoke command from the Global Constraints in the spike worktree. Record the `grep -c` count as **M0**. Expected: `0`. The generated methods carry `[UnconditionalSuppressMessage]` for IL2026 and IL3050, `ClientEmitter.cs:278-279`, which hides the annotated `IRestSerializer` calls.

- [ ] **Step 3: Measure with the suppression removed**

Delete the two `sb.AppendLine("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(...` lines in `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`, and the comment above them. The smoke promotes IL2026 and IL3050 to errors, so publish with them demoted to count every one:

```bash
dotnet publish samples/ZeroAlloc.Rest.AotSmoke/ZeroAlloc.Rest.AotSmoke.csproj -r win-x64 -c Release -o artifacts/aot -p:WarningsAsErrors= 2>&1 | tee artifacts/aot-publish.log
grep -oE "(warning|error) IL[0-9]+" artifacts/aot-publish.log | sort | uniq -c
```

Record the counts as **M1**. Expected: IL2026 and IL3050, at least one of each per generated method that calls the serializer. This is what the suppression hid.

- [ ] **Step 4: Measure with an annotation-free `IRestSerializer`**

Delete the `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` lines from `src/ZeroAlloc.Rest/IRestSerializer.cs`, from `src/ZeroAlloc.Rest/RestSerializerAdapter.cs` and from `samples/ZeroAlloc.Rest.AotSmoke/SmokeSerializer.cs`. The adapter is in the same assembly as the interface, so its methods must lose the attributes together with the interface's, or the build reports the annotation mismatch IL2046/IL3051 instead of measuring anything. Publish as in Step 3 and record **M2**. Expected: no IL lines at all.

- [ ] **Step 5: Measure the in-repo serializers without annotations**

Delete the same attributes from `src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs` and `src/ZeroAlloc.Rest.MessagePack/MessagePackRestSerializer.cs`. Replace `src/ZeroAlloc.Rest.SystemTextJson/SystemTextJsonSerializer.cs` with the Task 2 Step 5 version. Then build each with the trim and AOT analyzers on:

```bash
for p in ZeroAlloc.Rest ZeroAlloc.Rest.SystemTextJson ZeroAlloc.Rest.MemoryPack ZeroAlloc.Rest.MessagePack; do
  dotnet build src/$p/$p.csproj -c Release -p:IsAotCompatible=true -p:TreatWarningsAsErrors=false 2>&1 | grep -oE "warning IL[0-9]+" | sort | uniq -c | sed "s/^/$p: /"
done
dotnet test tests/ZeroAlloc.Rest.Tests/ZeroAlloc.Rest.Tests.csproj -c Release --filter "FullyQualifiedName~Serializers"
```

Record per-project counts as **M3**. Expected: no output from the loop, and the serializer tests pass. A reflection check of MemoryPack 1.21.4, MessagePack 3.1.10 and `JsonSerializerOptions.TryGetTypeInfo` found no trim annotations on the generic APIs these serializers call.

- [ ] **Step 6: Measure a context-based call end to end**

In the spike's smoke, add a context and call a generated client through a context-backed serializer:

```csharp
// samples/ZeroAlloc.Rest.AotSmoke/SpikeJsonContext.cs
using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.AotSmoke;

[JsonSerializable(typeof(string))]
internal sealed partial class SpikeJsonContext : JsonSerializerContext;
```

Give the spike's `SystemTextJsonSerializer` the Task 3 Step 4 context constructor. In `Program.cs`, before the final PASS line, bind through the interface — the generated client's own `ct` parameter has no default, unlike the interface's — and call it: `IUserApi client = new UserApiClient(new HttpClient(new UnprocessableHandler()) { BaseAddress = new Uri("http://localhost/") }, new SystemTextJsonSerializer(SpikeJsonContext.Default)); await client.TryGetUserAsync(1)`, and expect `HttpErrorKind.Status`. Add a `ProjectReference` to `ZeroAlloc.Rest.SystemTextJson`. Run the full AOT smoke command and record **M4**. Expected: `0`, then `AOT smoke: PASS`.

- [ ] **Step 7: Apply the decision rule**

- **Decision A,** when M2 and M4 are 0 and every M3 count is 0: remove the annotations from `IRestSerializer` and every in-repo implementation, stop emitting the suppression, and mark the reflection-based `SystemTextJsonSerializer` constructors instead. Task 2 does exactly this.
- **Stop,** when any count is not 0: the implementation that warns calls an annotated API with no unannotated alternative. Do not start Task 2. Write the measurements into the record, open a "Needs decision" comment on #351 naming the warning, the member and the options you see, and wait for the maintainer. Do not reach for a suppression, and do not introduce `ITypeInfoRestSerializer`: design decision 1 explains why it cannot reach 0.

- [ ] **Step 8: Write the decision record**

Remove the spike and return to the implementation worktree:

```bash
cd C:/wt/rest-351
git worktree remove --force C:/wt/rest-351-aot-spike
git branch -D spike/351-aot-annotations
```

Replace the body of "## AOT decision record" in this plan with, filling in the measured numbers:

```markdown
Measured on <date> with SDK <dotnet --version>, win-x64 or linux-x64.

| Step | Configuration | IL warnings |
|---|---|---|
| M0 | today: annotated interface, suppressed generated methods | <n> |
| M1 | suppression removed | <per-code counts> |
| M2 | interface and smoke serializer without annotations | <n> |
| M3 | each in-repo serializer with IsAotCompatible, without annotations | Rest <n>, STJ <n>, MemoryPack <n>, MessagePack <n> |
| M4 | M2 plus a context-backed SystemTextJsonSerializer call | <n> |

**Decision: A.** IRestSerializer and its implementations carry no trim annotations, the generated
client carries no suppression, and the reflection-based SystemTextJsonSerializer constructors carry
[RequiresUnreferencedCode] and [RequiresDynamicCode]. An ITypeInfoRestSerializer was not needed:
its fallback call to the annotated interface would have kept a warning in every generated method.
```

- [ ] **Step 9: Commit**

```bash
git add docs/plans/2026-09-27-openapi-models.md
git commit -m "$(cat <<'EOF'
docs: record the AOT annotation decision for Rest 3.0

The prototype measured the AOT smoke with the generated trim suppression removed, then with
IRestSerializer and every in-repo serializer free of trim annotations. The plan's decision
record holds the numbers and the resulting choice.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Annotation-free `IRestSerializer`, and generated clients without a trim suppression

Applies decision A. If Task 1 ended in "Stop", this task waits.

**Files:**
- Modify: `src/ZeroAlloc.Rest/IRestSerializer.cs`
- Modify: `src/ZeroAlloc.Rest/RestSerializerAdapter.cs:26-27,40-41`
- Modify: `src/ZeroAlloc.Rest.SystemTextJson/SystemTextJsonSerializer.cs`, `ZeroAlloc.Rest.SystemTextJson.csproj`
- Modify: `src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs:15-16,24-25`
- Modify: `src/ZeroAlloc.Rest.MessagePack/MessagePackRestSerializer.cs:22-23,30-31`, `ZeroAlloc.Rest.MessagePack.csproj`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs:270-279`
- Modify: `samples/ZeroAlloc.Rest.AotSmoke/SmokeSerializer.cs`
- Modify: every test serializer that carries the attributes: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorDiTests.cs`, `GeneratorEmissionTests.cs`, `GeneratorInterfaceSerializerTests.cs`, `tests/ZeroAlloc.Rest.Integration.Tests/ResultTransportErrorTests.cs`, `SerializerIsolationTests.cs`, `tests/ZeroAlloc.Rest.Resilience.Tests/RestResilienceSerializerTests.cs`, `tests/ZeroAlloc.Rest.Tests/ServiceCollectionExtensionsTests.cs`
- Modify: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorErrorMapperTests.cs:693-699`, the `MethodText` helper
- Test: `tests/ZeroAlloc.Rest.Tests/Serializers/SerializerAotAnnotationTests.cs`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorEmissionTests.cs`

**Interfaces:**
- Consumes: the Task 1 decision.
- Produces: `IRestSerializer` with no trim annotations; `SystemTextJsonSerializer` resolving `JsonTypeInfo<T>` through its options; a generated client with no `[UnconditionalSuppressMessage]`.

- [ ] **Step 1: Write the failing runtime test**

```csharp
// tests/ZeroAlloc.Rest.Tests/Serializers/SerializerAotAnnotationTests.cs
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.MemoryPack;
using ZeroAlloc.Rest.MessagePack;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Tests.Serializers;

// Rest 3.0: the serializer contract is AOT-neutral, so a generated client calls it without a trim
// suppression. The risk sits where reflection is chosen: the reflection-based constructors.
public class SerializerAotAnnotationTests
{
    public static TheoryData<Type> Serializers => new()
    {
        typeof(IRestSerializer),
        typeof(SystemTextJsonSerializer),
        typeof(MemoryPackRestSerializer),
        typeof(MessagePackRestSerializer),
        typeof(RestSerializerAdapter<string>),
    };

    [Theory]
    [MemberData(nameof(Serializers))]
    public void SerializerMethods_CarryNoTrimAnnotations(Type type)
    {
        foreach (var name in new[] { "SerializeAsync", "DeserializeAsync" })
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Null(method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
            Assert.Null(method.GetCustomAttribute<RequiresDynamicCodeAttribute>());
        }
    }

    // A lone Type[] argument would bind to InlineData's params array itself, so the case is a bool.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflectionBasedJsonConstructors_AreAnnotated(bool withOptions)
    {
        var constructor = typeof(SystemTextJsonSerializer).GetConstructor(
            withOptions ? [typeof(JsonSerializerOptions)] : Type.EmptyTypes);
        Assert.NotNull(constructor);
        Assert.NotNull(constructor.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
        Assert.NotNull(constructor.GetCustomAttribute<RequiresDynamicCodeAttribute>());
    }
}
```

- [ ] **Step 2: Write the failing generator test**

Append to `GeneratorEmissionTests`, which already has the helper `GetGeneratedSource(string source, string hintName)`:

```csharp
    // Rest 3.0: IRestSerializer carries no trim annotations, so a generated method calls it with
    // no suppression, and ILC has nothing to hide.
    [Fact]
    public void GeneratedMethods_CarryNoTrimSuppression()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IPingApi
            {
                [Get("/ping")]
                Task<string> PingAsync(CancellationToken ct = default);
            }
            """;

        var generated = GetGeneratedSource(source, "IPingApi.g.cs");

        Assert.Contains("PingAsync(", generated);
        Assert.DoesNotContain("UnconditionalSuppressMessage", generated);
    }
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Tests -c Release --filter "FullyQualifiedName~SerializerAotAnnotationTests"`
Expected: FAIL. `SerializerMethods_CarryNoTrimAnnotations` finds `RequiresUnreferencedCodeAttribute`; `ReflectionBasedJsonConstructors_AreAnnotated` finds none.

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests -c Release --filter "FullyQualifiedName~GeneratedMethods_CarryNoTrimSuppression"`
Expected: FAIL, the generated text contains `UnconditionalSuppressMessage`.

- [ ] **Step 4: Make `IRestSerializer` annotation-free**

```csharp
// src/ZeroAlloc.Rest/IRestSerializer.cs
namespace ZeroAlloc.Rest;

/// <summary>
/// Reads and writes request and response bodies for a generated client.
/// </summary>
/// <remarks>
/// The contract carries no trim or AOT annotations, so generated clients call it without a
/// suppression. An implementation that needs reflection says so where reflection is chosen, on its
/// constructor, as the reflection-based <c>SystemTextJsonSerializer</c> constructors do.
/// </remarks>
public interface IRestSerializer
{
    string ContentType { get; }

    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);

    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default);
}
```

In `RestSerializerAdapter.cs`, `MemoryPackRestSerializer.cs`, `MessagePackRestSerializer.cs` and `samples/ZeroAlloc.Rest.AotSmoke/SmokeSerializer.cs`, delete every `[RequiresDynamicCode(...)]` and `[RequiresUnreferencedCode(...)]` line, then any `using System.Diagnostics.CodeAnalysis;` left unused. Do the same in each test serializer listed under Files.

Add `<IsAotCompatible>true</IsAotCompatible>` to the first `<PropertyGroup>` of `ZeroAlloc.Rest.SystemTextJson.csproj` and `ZeroAlloc.Rest.MessagePack.csproj`, so the trim analyzer checks them from now on. MemoryPack gets `IsAotCompatible` in Task 20, not here; Task 2 only strips its method annotations.

- [ ] **Step 5: Resolve STJ type info through the options**

```csharp
// src/ZeroAlloc.Rest.SystemTextJson/SystemTextJsonSerializer.cs
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.SystemTextJson;

/// <summary>An <see cref="IRestSerializer"/> for application/json, backed by System.Text.Json.</summary>
public sealed class SystemTextJsonSerializer : IRestSerializer
{
    private const string ReflectionMessage =
        "Reflection-based JSON serialization needs members the trimmer may remove and code Native AOT "
        + "cannot generate. Pass a JsonSerializerContext, such as the one generated from your OpenAPI spec.";

    private readonly JsonSerializerOptions _options;

    /// <summary>Serializes with reflection and <see cref="JsonSerializerDefaults.Web"/>.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public SystemTextJsonSerializer()
        : this(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    {
    }

    /// <summary>
    /// Serializes with these options. Without a type info resolver on them, reflection supplies one.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public SystemTextJsonSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Locks the options with the reflection resolver when they have none. This is the only
        // place reflection is chosen, which is why this constructor carries the annotations.
        if (!options.IsReadOnly)
            options.MakeReadOnly(populateMissingResolver: true);
        _options = options;
    }

    public string ContentType => "application/json";

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (stream.CanSeek && stream.Position >= stream.Length) return default;
        return await JsonSerializer.DeserializeAsync(stream, TypeInfo<T>(), ct).ConfigureAwait(false);
    }

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
        => await JsonSerializer.SerializeAsync(stream, value, TypeInfo<T>(), ct).ConfigureAwait(false);

    // TryGetTypeInfo is not annotated: it only asks the resolver the options already have.
    private JsonTypeInfo<T> TypeInfo<T>()
    {
        if (_options.TryGetTypeInfo(typeof(T), out var typeInfo))
            return (JsonTypeInfo<T>)typeInfo;
        throw new InvalidOperationException(
            $"{typeof(T)} is not registered with the JSON type info resolver this SystemTextJsonSerializer "
            + $"was created with. Add [JsonSerializable(typeof({typeof(T).Name}))] to your JsonSerializerContext. "
            + "For a client generated from an OpenAPI spec, regenerate it: its generated JsonContext covers "
            + "every request and response type.");
    }
}
```

- [ ] **Step 6: Stop emitting the suppression**

In `ClientEmitter.EmitMethod`, delete the comment block that starts `// IL3051/IL2046: Previously we emitted` and the two `sb.AppendLine("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(...` lines after it.

In `GeneratorErrorMapperTests.MethodText`, the start anchor was the suppression attribute. Anchor on the method's own line instead:

```csharp
    private static string MethodText(string generated, string methodName)
    {
        var signature = generated.IndexOf($" {methodName}(", StringComparison.Ordinal);
        var start = generated.LastIndexOf("    public ", signature, StringComparison.Ordinal);
        var end = generated.IndexOf("\n    }\n", signature, StringComparison.Ordinal);
        return generated.Substring(start, end - start);
    }
```

- [ ] **Step 7: Run the new tests, then the full suite and the AOT smoke**

Run the two commands from Step 3. Expected: PASS.

Run the full-suite command. Tests that constructed `new SystemTextJsonSerializer()` still pass: the trim analyzer is off in test projects, and the reflection resolver is enabled under the JIT.

Run the AOT smoke command. Expected: `0`, then `AOT smoke: PASS`.

- [ ] **Step 8: Commit**

```bash
git add -A src tests samples
git commit -m "$(cat <<'EOF'
feat: make IRestSerializer AOT-neutral and drop the generated trim suppression

IRestSerializer and the in-repo serializers carry no RequiresDynamicCode or
RequiresUnreferencedCode, so generated clients no longer emit UnconditionalSuppressMessage
to hide them. The reflection-based SystemTextJsonSerializer constructors carry the
annotations instead, and every method resolves JsonTypeInfo through the options.
SystemTextJson and MessagePack now build with IsAotCompatible; MemoryPack follows in Task 20.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 20: AOT-safe `MemoryPackRestSerializer` through explicit registration

Runs after Task 2 and before Task 3. The maintainer's decision on the Task 1 stop, recorded in the AOT decision record.
Task 1 measured 2 IL2091 in `ZeroAlloc.Rest.MemoryPack`: every `MemoryPackSerializer.Deserialize<T>` overload in
MemoryPack 1.21.4 carries `[DynamicallyAccessedMembers(All)]` on `T`, because MemoryPack finds a type's
`RegisterFormatter` through reflection. Calling MemoryPack's unannotated reader APIs builds clean but fails at run time
under Native AOT, since nothing registered the formatter. Explicit registration removes the reflection instead of hiding it.

**Files:**
- Modify: `src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs`, `ZeroAlloc.Rest.MemoryPack.csproj`
- Create: `src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestTypes.cs` (the registration builder; the implementer may pick a
  better name that fits the repo), `src/ZeroAlloc.Rest.MemoryPack/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`
  if the project has no tracking yet, following the pattern of the projects that do
- Create: `samples/ZeroAlloc.Rest.MemoryPack.AotSmoke/*`: a MemoryPack round trip and a missing-registration check
  under Native AOT, in its own sample and CI job `aot-smoke-memorypack`, because MemoryPack.Core itself reports
  IL2104 and IL3053 on every AOT publish. The main AOT smoke keeps no MemoryPack reference and 0 IL warnings.
- Modify: every caller that needs registered types: `tests/ZeroAlloc.Rest.Tests/Serializers/MemoryPackSerializerTests.cs`,
  `tests/ZeroAlloc.Rest.Integration.Tests/ResultTransportErrorTests.cs`, `tests/ZeroAlloc.Rest.Benchmarks/SerializerBenchmarks.cs`
- Modify: `docs/serialization.md`, `docs/dependency-injection.md`, `docs/advanced.md`, `docs/benchmarks.md` where they
  construct or auto-register `MemoryPackRestSerializer`

**Interfaces:**
- Consumes: Task 2's annotation-free `IRestSerializer`.
- Produces:
  - `public MemoryPackRestSerializer(Action<MemoryPackRestTypes> configure)`. The parameterless constructor stays,
    so api-compat passes; it registers no types, so it serves only MemoryPack's built-in types.
  - `public sealed class MemoryPackRestTypes` with `public MemoryPackRestTypes Add<T>() where T : IMemoryPackable<T>`,
    which calls `T.RegisterFormatter()`, MemoryPack's generated static registration, and records `typeof(T)`.
  - Serialize and deserialize go through MemoryPack's unannotated formatter path, such as
    `MemoryPackReaderOptionalStatePool.Rent`, `MemoryPackReader` and `ReadValue<T>`, with the same state handling
    and disposal `MemoryPackSerializer.Deserialize<T>` uses, and the unannotated `MemoryPackSerializer.Serialize<T>`.
  - A type that is neither registered through `Add<T>()` nor served by a MemoryPack built-in formatter throws
    `InvalidOperationException` before any MemoryPack call. The message names the type and shows the fix:
    `new MemoryPackRestSerializer(types => types.Add<TheType>())`. Built-in detection must not itself use reflection
    that fails under Native AOT; the AOT smoke proves it.
  - The project sets `IsAotCompatible` to `true` in a new `<PropertyGroup>` and builds with 0 IL warnings.

**DI.** `[Serializer(typeof(MemoryPackRestSerializer))]` makes the generated client call
`TryAddSingleton<MemoryPackRestSerializer>()`. That activates the parameterless constructor, which serves only built-in
types, so the caller registers a configured instance first:
`services.AddSingleton(new MemoryPackRestSerializer(types => types.Add<User>()));`, and `TryAddSingleton` then leaves
it in place. A caller who forgets gets the registration error at the first call with a `[MemoryPackable]` type.
Document it in `docs/dependency-injection.md` and `docs/serialization.md`. Add an integration test in which a
generated client with a MemoryPack method resolves through DI with a pre-registered instance and round-trips a value,
and one in which the auto-registered instance throws the registration error.

- [ ] **Step 1: Failing tests.** In `MemoryPackSerializerTests`: round trip of a registered `[MemoryPackable]` type;
  `DeserializeAsync` and `SerializeAsync` of an unregistered `[MemoryPackable]` type throw `InvalidOperationException`
  whose message contains the type name and `types.Add<`; a built-in type such as `int` or `string` round-trips without
  registration; `Add<T>()` returns the same builder so calls chain. Watch them fail to compile against the old constructor.
- [ ] **Step 2: Implement** the builder and the serializer. No `!`, no suppressions of any kind.
- [ ] **Step 3: PublicAPI.** List every new public member in `PublicAPI.Unshipped.txt`; the parameterless constructor
  stays in `PublicAPI.Shipped.txt`.
- [ ] **Step 4: Native AOT runtime proof.** In the new `samples/ZeroAlloc.Rest.MemoryPack.AotSmoke`, reference
  `ZeroAlloc.Rest.MemoryPack`. Declare a
  `[MemoryPackable]` partial record, register it, round-trip it through `IRestSerializer.SerializeAsync` and
  `DeserializeAsync`, and compare field by field. Then call `DeserializeAsync` for a second, unregistered `[MemoryPackable]`
  type and require the `InvalidOperationException` with the type's name. Any mismatch exits non-zero before the PASS line.
  Publish it with `-p:TrimmerSingleWarn=false`: every IL warning line must come from MemoryPack.Core, and the binary
  prints its PASS line. The main AOT smoke command from the Global Constraints still prints `0` and `AOT smoke: PASS`.
  On win-x64, ILC needs the directory of `vswhere.exe` on `PATH`, usually
  `C:\Program Files (x86)\Microsoft Visual Studio\Installer`.
- [ ] **Step 5: Callers and docs.** Update every caller that serializes a `[MemoryPackable]` type and every doc that
  says no registration is needed for MemoryPack.
- [ ] **Step 6: Full suite**, then commit:

```bash
git commit -m "$(cat <<'MSG'
feat: make MemoryPackRestSerializer AOT-safe through explicit type registration

MemoryPack finds a type's formatter through reflection unless it is registered. The serializer now
takes the types it serves up front, registers each through its generated static RegisterFormatter,
and reads through MemoryPack's unannotated formatter path. An unregistered type throws
InvalidOperationException naming the type. The parameterless constructor now serves only
MemoryPack's built-in types. The Native AOT proof lives in its own sample and CI job, because
MemoryPack.Core itself reports IL2104 and IL3053.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MSG
)"
```

Task 14's `BREAKING CHANGE` footer and Task 16's migration guide both gain an item:
`MemoryPackRestSerializer` serves only registered types plus MemoryPack built-ins,
`new MemoryPackRestSerializer(types => types.Add<User>())`, and a client using the Serializer
attribute for it registers that configured instance in DI before `Add{Interface}`.

---

### Task 3: `SystemTextJsonSerializer` context and resolver constructors

**Files:**
- Modify: `src/ZeroAlloc.Rest.SystemTextJson/SystemTextJsonSerializer.cs`
- Modify: `src/ZeroAlloc.Rest.SystemTextJson/ZeroAlloc.Rest.SystemTextJson.csproj`
- Create: `src/ZeroAlloc.Rest.SystemTextJson/PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`
- Test: `tests/ZeroAlloc.Rest.Tests/Serializers/SystemTextJsonContextTests.cs`
- Test: `tests/ZeroAlloc.Rest.Tests/Serializers/WidgetJsonContext.cs`
- Modify: `tests/ZeroAlloc.Rest.Tests/Serializers/SerializerAotAnnotationTests.cs`

**Interfaces:**
- Consumes: `SystemTextJsonSerializer.TypeInfo<T>` from Task 2.
- Produces: `public SystemTextJsonSerializer(JsonSerializerContext context)` and `public SystemTextJsonSerializer(IJsonTypeInfoResolver resolver, JsonSerializerOptions? options = null)`. Tasks 14 and 18 construct the first with a generated context.

- [ ] **Step 1: Write the test context**

```csharp
// tests/ZeroAlloc.Rest.Tests/Serializers/WidgetJsonContext.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Tests.Serializers;

public sealed record Widget(int Id, string Name);

public sealed record Unregistered(int Id);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Widget))]
internal sealed partial class WidgetJsonContext : JsonSerializerContext;
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tests/Serializers/SystemTextJsonContextTests.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Tests.Serializers;

// Spec §7: a context or resolver constructor serializes through JsonTypeInfo<T> only, and never
// falls back to reflection.
public class SystemTextJsonContextTests
{
    public static TheoryData<string> Constructions => new() { "context", "resolver", "resolver with options" };

    private static SystemTextJsonSerializer Create(string construction) => construction switch
    {
        "context" => new SystemTextJsonSerializer(WidgetJsonContext.Default),
        "resolver" => new SystemTextJsonSerializer((IJsonTypeInfoResolver)WidgetJsonContext.Default),
        _ => new SystemTextJsonSerializer(WidgetJsonContext.Default, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };

    [Theory]
    [MemberData(nameof(Constructions))]
    public async Task RegisteredType_RoundTrips(string construction)
    {
        var serializer = Create(construction);
        using var stream = new MemoryStream();

        await serializer.SerializeAsync(stream, new Widget(7, "gear"));
        stream.Position = 0;
        var json = Encoding.UTF8.GetString(stream.ToArray());
        var read = await serializer.DeserializeAsync<Widget>(stream);

        Assert.Equal("""{"id":7,"name":"gear"}""", json);
        Assert.Equal(new Widget(7, "gear"), read);
    }

    [Theory]
    [MemberData(nameof(Constructions))]
    public async Task UnregisteredType_ThrowsNamingTheTypeAndTheContext(string construction)
    {
        var serializer = Create(construction);
        using var stream = new MemoryStream();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await serializer.SerializeAsync(stream, new Unregistered(1)));

        Assert.Contains("ZeroAlloc.Rest.Tests.Serializers.Unregistered", error.Message, StringComparison.Ordinal);
        Assert.Contains("[JsonSerializable(typeof(Unregistered))]", error.Message, StringComparison.Ordinal);
        Assert.Contains("generated JsonContext", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyBody_DeserializesToDefault()
    {
        var serializer = new SystemTextJsonSerializer(WidgetJsonContext.Default);
        using var stream = new MemoryStream();

        Assert.Null(await serializer.DeserializeAsync<Widget>(stream));
    }

    [Fact]
    public void ResolverConstructor_DoesNotLockTheCallersOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        _ = new SystemTextJsonSerializer(WidgetJsonContext.Default, options);

        Assert.False(options.IsReadOnly);
    }
}
```

Append to `SerializerAotAnnotationTests`:

```csharp
    [Theory]
    [InlineData(typeof(System.Text.Json.Serialization.JsonSerializerContext))]
    [InlineData(typeof(System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver))]
    public void ContextAndResolverConstructors_AreNotAnnotated(Type parameter)
    {
        var constructor = typeof(SystemTextJsonSerializer).GetConstructors()
            .Single(c => c.GetParameters() is [var first, ..] && first.ParameterType == parameter);
        Assert.Null(constructor.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
        Assert.Null(constructor.GetCustomAttribute<RequiresDynamicCodeAttribute>());
    }
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tests -c Release`
Expected: FAIL with CS1503 or CS1729: no constructor takes a `JsonSerializerContext` or an `IJsonTypeInfoResolver`.

- [ ] **Step 4: Add the constructors**

Insert after the options constructor in `SystemTextJsonSerializer.cs`:

```csharp
    /// <summary>
    /// Serializes through the context's source-generated metadata only. A type the context does not
    /// cover throws <see cref="InvalidOperationException"/>; nothing falls back to reflection.
    /// </summary>
    public SystemTextJsonSerializer(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _options = context.Options;
    }

    /// <summary>
    /// Serializes through <paramref name="resolver"/> only, with a copy of <paramref name="options"/>,
    /// or of <see cref="JsonSerializerDefaults.Web"/>, whose resolver is replaced by it.
    /// </summary>
    public SystemTextJsonSerializer(IJsonTypeInfoResolver resolver, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _options = new JsonSerializerOptions(options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web))
        {
            TypeInfoResolver = resolver,
        };
        _options.MakeReadOnly();
    }
```

`new SystemTextJsonSerializer(context)` binds the `JsonSerializerContext` overload, which needs no default argument, although a context is also an `IJsonTypeInfoResolver`.

- [ ] **Step 5: Track the package's public API**

In `ZeroAlloc.Rest.SystemTextJson.csproj` add, as in `ZeroAlloc.Rest.csproj`:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <AdditionalFiles Include="PublicAPI.Shipped.txt" />
    <AdditionalFiles Include="PublicAPI.Unshipped.txt" />
  </ItemGroup>
```

`PublicAPI.Shipped.txt`, the API 2.x shipped:

```text
#nullable enable
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer.ContentType.get -> string!
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer.DeserializeAsync<T>(System.IO.Stream! stream, System.Threading.CancellationToken ct = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<T?>
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer.SerializeAsync<T>(System.IO.Stream! stream, T value, System.Threading.CancellationToken ct = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer.SystemTextJsonSerializer() -> void
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer.SystemTextJsonSerializer(System.Text.Json.JsonSerializerOptions! options) -> void
```

`PublicAPI.Unshipped.txt`:

```text
#nullable enable
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer.SystemTextJsonSerializer(System.Text.Json.Serialization.JsonSerializerContext! context) -> void
ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer.SystemTextJsonSerializer(System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver! resolver, System.Text.Json.JsonSerializerOptions? options = null) -> void
```

If RS0016 or RS0017 reports a line that differs by spelling, take the analyzer's spelling: it is the source of truth.

- [ ] **Step 6: Run the tests, then the full suite**

Run: `dotnet test tests/ZeroAlloc.Rest.Tests -c Release --filter "FullyQualifiedName~Serializers"`
Expected: PASS. Then run the full-suite command.

- [ ] **Step 7: Commit**

```bash
git add -A src/ZeroAlloc.Rest.SystemTextJson tests/ZeroAlloc.Rest.Tests
git commit -m "$(cat <<'EOF'
feat: add context and resolver constructors to SystemTextJsonSerializer

SystemTextJsonSerializer now takes a JsonSerializerContext, or an IJsonTypeInfoResolver
with optional options, and serializes through JsonTypeInfo only. A type the resolver does
not cover throws InvalidOperationException naming it; nothing falls back to reflection.
The package now tracks its public API.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `UnitResult<E>` returns in the Rest source generator

Spec §6: an operation with no 2xx schema returns `Task<UnitResult<HttpError>>`, and the generator must support it, `[ErrorMapper]` included.

**Files:**
- Modify: `src/ZeroAlloc.Rest.Generator/Models/MethodModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs:26,359-401,409-415`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs:469-543`
- Modify: `docs/advanced.md`, after "### What still throws"
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorUnitResultTests.cs`
- Test: `tests/ZeroAlloc.Rest.Integration.Tests/UnitResultTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `MethodModel.ReturnsUnitResult`. A method returning `Task<UnitResult<E>>` or `ValueTask<UnitResult<E>>` gets `ReturnsResult = true`, `ReturnsUnitResult = true` and `InnerTypeName = null`, and returns `ZeroAlloc.Results.UnitResult<E>.Success()` on any 2xx without reading the body. Task 14 emits these return types.

- [ ] **Step 1: Write the failing generator tests**

```csharp
// tests/ZeroAlloc.Rest.Generator.Tests/GeneratorUnitResultTests.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Spec §6 of the OpenAPI models design: a method with no success body returns UnitResult<E>.
// Failures map exactly as for Result<T, E>; success reads no body.
public class GeneratorUnitResultTests
{
    private static readonly MetadataReference[] References =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        public sealed record JevError(string Code);
        public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new(error.Kind.ToString());
        }

        """;

    private const string HttpErrorApi = Usings + """
        [ZeroAllocRestClient]
        public interface IThingApi
        {
            [Delete("/things/{id}")]
            Task<UnitResult<HttpError>> DeleteAsync(int id, CancellationToken ct = default);
        }
        """;

    private const string MappedApi = Usings + """
        [ZeroAllocRestClient]
        [ErrorMapper(typeof(JevErrorMapper))]
        public interface IThingApi
        {
            [Delete("/things/{id}")]
            ValueTask<UnitResult<JevError>> DeleteAsync(int id, CancellationToken ct = default);
        }
        """;

    [Fact]
    public void UnitResultMethod_Compiles_AndReturnsSuccessWithoutReadingTheBody()
    {
        var run = Run(HttpErrorApi);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.Client;
        Assert.Contains("return ZeroAlloc.Results.UnitResult<ZeroAlloc.Rest.HttpError>.Success();", client);
        Assert.DoesNotContain("DeserializeAsync<", client);
        Assert.DoesNotContain("HttpErrorKind.Deserialization", client);
    }

    [Fact]
    public void UnitResultMethod_ReturnsStatusTimeoutAndTransportFailures()
    {
        var client = Run(HttpErrorApi).Client;

        Assert.Contains("return ZeroAlloc.Results.UnitResult<ZeroAlloc.Rest.HttpError>.Failure(", client);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, __response, null, __errorBody.Body, __errorBody.Truncated)", client);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Timeout, null, __ex)", client);
        Assert.Contains("__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Transport, null, __ex)", client);
    }

    [Fact]
    public void MappedUnitResultMethod_MapsOnceThroughTheErrorMapper()
    {
        var run = Run(MappedApi);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        Assert.Contains("return ZeroAlloc.Results.UnitResult<global::MyApp.JevError>.Success();", run.Client);
        Assert.Contains(
            "return ZeroAlloc.Results.UnitResult<global::MyApp.JevError>.Failure(_jevErrorMapper.Map(__httpError));",
            run.Client);
    }

    [Fact]
    public void UnitResultWithUnmappedErrorType_ReportsZra002()
    {
        var run = Run(MappedApi.Replace("[ErrorMapper(typeof(JevErrorMapper))]", "", StringComparison.Ordinal));

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA002", diagnostic.Id);
    }

    private static GeneratorRun Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "Api.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var client = driver.GetRunResult().Results[0].GeneratedSources
            .First(s => string.Equals(s.HintName, "IThingApi.g.cs", StringComparison.Ordinal))
            .SourceText.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        // Consumers build with TreatWarningsAsErrors, so a warning counts.
        var problems = output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToImmutableArray();
        return new GeneratorRun(client, generatorDiagnostics, problems);
    }

    private sealed record GeneratorRun(string Client, ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> Problems);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests -c Release --filter "FullyQualifiedName~GeneratorUnitResultTests"`
Expected: FAIL. Today `UnitResult<HttpError>` is treated as a body type, so the client calls `DeserializeAsync<ZeroAlloc.Results.UnitResult<ZeroAlloc.Rest.HttpError>>` and no `.Success()` is emitted; the ZRA002 test finds no diagnostic.

- [ ] **Step 3: Carry `ReturnsUnitResult` in the model**

In `Models/MethodModel.cs` add a last positional parameter `bool ReturnsUnitResult`, and extend the comment above `HttpErrorTypeName`:

```csharp
    // ReturnsUnitResult is set for UnitResult<E>: ReturnsResult is also set, so the failure paths are
    // exactly those of Result<T, E>, and InnerTypeName is null because success reads no body.
```

- [ ] **Step 4: Recognise `UnitResult<E>` in `ModelExtractor`**

Next to `ResultOpenType` add:

```csharp
    private const string UnitResultOpenType = "ZeroAlloc.Results.UnitResult<E>";
```

Declare `var returnsUnitResult = false;` with the other locals, and replace the `if (returnType.TypeArguments.Length == 1) { ... }` block with:

```csharp
        if (returnType.TypeArguments.Length == 1)
        {
            var inner = returnType.TypeArguments[0] as INamedTypeSymbol;
            innerTypeName = inner?.ToDisplayString();
            var innerDefinition = inner?.OriginalDefinition.ToDisplayString();
            returnsUnitResult = innerDefinition == UnitResultOpenType;
            returnsResult = returnsUnitResult || innerDefinition == ResultOpenType;

            ITypeSymbol? errorType = null;
            if (returnsUnitResult && inner?.TypeArguments.Length == 1)
            {
                innerTypeName = null;
                errorType = inner.TypeArguments[0];
            }
            else if (returnsResult && inner?.TypeArguments.Length == 2)
            {
                innerTypeName = inner.TypeArguments[0].ToDisplayString();
                errorType = inner.TypeArguments[1];
            }

            if (errorType is not null)
            {
                errorTypeName = ErrorTypeKey(errorType);
                declaredErrorTypeName = AnnotatedErrorTypeName(errorType);

                // An unresolved error type already has its own compiler error.
                if (errorTypeName != MethodModel.HttpErrorTypeName && errorType.TypeKind != TypeKind.Error)
                {
                    if (mappers.MapperByError.TryGetValue(errorTypeName, out var mapper))
                    {
                        errorMapperTypeName = mapper.MapperTypeName;
                        mapperErrorTypeName = AnnotatedErrorTypeName(mapper.ErrorType);
                        // A mapper declared over JevError? may return null, which a method returning
                        // Result<T, JevError> must not pass on as its error. An oblivious E counts as
                        // not nullable: the generated code is compiled with nullable enabled.
                        mappedErrorNeedsNullCheck = !mapper.ErrorType.IsValueType
                            && mapper.ErrorType.NullableAnnotation == NullableAnnotation.Annotated
                            && errorType.NullableAnnotation != NullableAnnotation.Annotated;
                    }
                    else if (!mappers.HasUnresolvedMapper && !mappers.ClaimedByInvalidMapper.Contains(errorTypeName))
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MissingErrorMapper, location,
                            Args(method.Name, errorType.ToDisplayString())));
                }
            }
        }
```

Pass `returnsUnitResult` as the new last argument of `new MethodModel(...)`.

- [ ] **Step 5: Emit the `UnitResult` response handling**

In `ClientEmitter.cs`, replace `ResultTypeName`:

```csharp
    // A mapped method repeats its E as declared, nullable annotation included: Result<T, E> and
    // UnitResult<E> are structs, so Result<T, JevError> does not convert to Result<T, JevError?>.
    private static string ResultTypeName(MethodModel method)
    {
        var error = method.MapsError ? method.DeclaredErrorTypeName : "ZeroAlloc.Rest.HttpError";
        return method.ReturnsUnitResult
            ? $"ZeroAlloc.Results.UnitResult<{error}>"
            : $"ZeroAlloc.Results.Result<{method.InnerTypeName}, {error}>";
    }
```

In `EmitResponseHandling`, move the body of the `ReturnsResult` branch's `else { ... }`, the status failure, into a new method so both branches share it:

```csharp
    // A non-success status: read the capped body, when asked, before the response is disposed.
    private static void EmitStatusFailure(StringBuilder sb, MethodModel method, string indent, string ctArg, int maxErrorBodyBytes)
    {
        if (maxErrorBodyBytes > 0)
        {
            // The helper caps the body, turns a failed read into an empty body, and still throws on
            // caller cancellation.
            var cap = maxErrorBodyBytes.ToString(CultureInfo.InvariantCulture);
            sb.AppendLine($"{indent}var __errorBody = await global::ZeroAlloc.Rest.GeneratedRestClient.ReadErrorBodyAsync(__response.Content, {cap}, {ctArg}).ConfigureAwait(false);");
            EmitFailure(sb, indent, method, "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, __response, null, __errorBody.Body, __errorBody.Truncated)");
        }
        else
        {
            EmitFailure(sb, indent, method, "__CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, __response, null)");
        }
    }
```

Replace the `ReturnsResult` branch's `else` body with `EmitStatusFailure(sb, method, i1, ctArg, maxErrorBodyBytes);`, and insert this branch between `if (method.ReturnsVoid)` and `else if (method.ReturnsResult)`:

```csharp
        else if (method.ReturnsUnitResult)
        {
            // No body to read, so nothing can fail to deserialize: success is the status alone.
            sb.AppendLine($"{indent}if (__response.IsSuccessStatusCode)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{i1}return {ResultTypeName(method)}.Success();");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine($"{indent}else");
            sb.AppendLine($"{indent}{{");
            EmitStatusFailure(sb, method, i1, ctArg, maxErrorBodyBytes);
            sb.AppendLine($"{indent}}}");
        }
```

The timeout and transport catches come from `EmitResultCatches`, which already runs for every `ReturnsResult` method. A mapped method's `else` stores `__httpError` and falls through to the single mapping site after the `try`, as for `Result<T, E>`.

- [ ] **Step 6: Run the generator tests**

Run the Step 2 command. Expected: PASS. Then `dotnet test tests/ZeroAlloc.Rest.Generator.Tests -c Release`: the existing emission tests still pass, since `Result<T, E>` output is unchanged character for character.

- [ ] **Step 7: Write the failing integration tests**

```csharp
// tests/ZeroAlloc.Rest.Integration.Tests/UnitResultTests.cs
using System.Net;
using System.Net.Http;
using System.Text;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;
using Xunit;

namespace ZeroAlloc.Rest.Integration.Tests;

[ZeroAllocRestClient]
public interface IUnitApi
{
    [Delete("/things/{id}")]
    Task<UnitResult<HttpError>> DeleteThingAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(DomainErrorMapper))]
public interface IMappedUnitApi
{
    [Delete("/things/{id}")]
    Task<UnitResult<DomainError>> DeleteThingAsync(int id, CancellationToken ct = default);
}

public sealed class UnitResultTests
{
    [Theory]
    [InlineData(HttpStatusCode.NoContent, "")]
    [InlineData(HttpStatusCode.OK, "{ not json")]
    public async Task Success_ReadsNoBody(HttpStatusCode status, string body)
    {
        using var http = Client((_, _) => Respond(status, body));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        var result = await api.DeleteThingAsync(1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Status_IsAFailureWithTheBody()
    {
        using var http = Client((_, _) => Respond(HttpStatusCode.NotFound, "gone"));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        var result = await api.DeleteThingAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.StatusCode);
        Assert.Equal("gone", Encoding.UTF8.GetString(result.Error.Body.Span));
    }

    [Fact]
    public async Task Transport_IsAFailure()
    {
        using var http = Client((_, _) => throw new HttpRequestException("refused"));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        var result = await api.DeleteThingAsync(1);

        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
    }

    [Fact]
    public async Task CallerCancellation_StillThrows()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var http = Client((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.DeleteThingAsync(1, cts.Token));
    }

    [Fact]
    public async Task MappedStatus_ReachesTheMapper()
    {
        using var http = Client((_, _) => Respond(HttpStatusCode.UnprocessableEntity, ResultErrorMapperTests.ProblemJson));
        IMappedUnitApi api = new MappedUnitApiClient(http, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await api.DeleteThingAsync(1);

        Assert.Equal(
            new DomainError(HttpErrorKind.Status, HttpStatusCode.UnprocessableEntity, "field_required", "library"),
            result.Error);
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new StubHandler(send)) { BaseAddress = ResultErrorMapperTests.BaseAddress };

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body)
        => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
}
```

- [ ] **Step 8: Run to verify, then document**

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests -c Release --filter "FullyQualifiedName~UnitResultTests"`
Expected: PASS. Revert Step 5's new branch locally to see `Success_ReadsNoBody` fail, then restore it.

In `docs/advanced.md`, insert before "## Your own error type: `[ErrorMapper]`":

````markdown
### Operations with no response body: `UnitResult<HttpError>`

A method that has nothing to return on success returns `UnitResult<HttpError>`, from
ZeroAlloc.Results. Any 2xx status is a success, and the body is not read, so a success can never be
a `Deserialization` failure. Every other failure is exactly as for `Result<T, HttpError>`: a
non-success status, a transport failure and a timeout come back as an `HttpError`, and what still
throws is listed above.

```csharp
[Delete("/users/{id}")]
Task<UnitResult<HttpError>> DeleteUserAsync(int id, CancellationToken ct = default);
```

`UnitResult<TError>` works with an `[ErrorMapper]` in the same way as `Result<T, TError>`. The code
generator returns it for every operation whose 2xx responses have no schema.
````

In "## Void methods (no response body)", add as the last paragraph: "To get failures as values instead of exceptions, return `UnitResult<HttpError>`; see [Operations with no response body](#operations-with-no-response-body-unitresulthttperror)."

- [ ] **Step 9: Run the full suite, then commit**

```bash
git add -A src/ZeroAlloc.Rest.Generator tests docs/advanced.md
git commit -m "$(cat <<'EOF'
feat: support UnitResult returns in generated clients

A method returning a Task or ValueTask of UnitResult now gets the Result failure handling:
status, timeout and transport failures come back as the error, mapped through an
ErrorMapper when the error type is not HttpError. A 2xx returns Success and reads no
body. Missing mappers report ZRA002 as for Result.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Wire-format route, query and header values

Design decision 3. Without it, the typed parameters of Task 14 would send culture-dependent dates and numbers, `True`, and C# enum names.

**Files:**
- Create: `src/ZeroAlloc.Rest.Generator/Models/ValueFormatModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/Models/ParameterModel.cs`
- Modify: `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs:430-475`, `ExtractParameters`
- Modify: `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs`: `Emit` after the methods loop, `EmitUrlBuilding`, `EmitRequestCreation`, `RouteExpression`
- Modify: `tests/ZeroAlloc.Rest.Generator.Tests/RouteTemplateAnalyzerTests.cs:126-144,227-229,308`, `GeneratorEmissionTests.cs:701-745`
- Test: `tests/ZeroAlloc.Rest.Generator.Tests/GeneratorValueFormatTests.cs`
- Test: `tests/ZeroAlloc.Rest.Integration.Tests/ValueFormatTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: in every generated client, one `private static string __FormatValue(T value)` per type used in a route, query or header value, plus a `T?` overload for value types. Task 14's typed parameters rely on it.

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/ZeroAlloc.Rest.Integration.Tests/ValueFormatTests.cs
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;
using Xunit;

namespace ZeroAlloc.Rest.Integration.Tests;

public enum Mood
{
    [JsonStringEnumMemberName("very-happy")]
    VeryHappy,
    Sad,
}

[ZeroAllocRestClient]
public interface IFormatApi
{
    [Get("/at/{when}")]
    Task<Result<string, HttpError>> AtAsync(
        DateTimeOffset when,
        [Query] double ratio,
        [Query] bool active,
        [Query] Mood mood,
        [Query] Mood other,
        [Query] int? limit,
        [Query] List<DateOnly> days,
        [Header("X-Retry")] int retry,
        [Header("X-Trace")] string? trace,
        CancellationToken ct = default);
}

// Design decision 3 of the OpenAPI models plan: values are written the same in every culture,
// in the form an OpenAPI server expects.
public sealed class ValueFormatTests
{
    [Fact]
    public async Task Values_AreWrittenInvariantly_WithIsoDatesLowerCaseBooleansAndWireEnumNames()
    {
        HttpRequestMessage? sent = null;
        using var http = new HttpClient(new StubHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("\"ok\"", Encoding.UTF8, "application/json"),
            });
        }))
        { BaseAddress = ResultErrorMapperTests.BaseAddress };
        IFormatApi api = new FormatApiClient(http, new SystemTextJsonSerializer());
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            await api.AtAsync(
                new DateTimeOffset(2026, 9, 27, 10, 30, 0, TimeSpan.FromHours(2)),
                ratio: 1.5,
                active: true,
                mood: Mood.VeryHappy,
                other: Mood.Sad,
                limit: null,
                days: [new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28)],
                retry: 3,
                trace: null);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.NotNull(sent);
        Assert.Equal(
            "/at/2026-09-27T10%3A30%3A00.0000000%2B02%3A00?ratio=1.5&active=true&mood=very-happy&other=Sad&days=2026-09-27&days=2026-09-28",
            sent.RequestUri?.PathAndQuery);
        Assert.Equal("3", Assert.Single(sent.Headers.GetValues("X-Retry")));
        Assert.False(sent.Headers.Contains("X-Trace"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ZeroAlloc.Rest.Integration.Tests -c Release --filter "FullyQualifiedName~ValueFormatTests"`
Expected: FAIL. The URL holds `27.09.2026 10:30:00 +02:00` escaped, `ratio=1,5`, `active=True` and `mood=VeryHappy`, and an empty `X-Trace` header is sent.

- [ ] **Step 3: Model the value format**

```csharp
// src/ZeroAlloc.Rest.Generator/Models/ValueFormatModel.cs
namespace ZeroAlloc.Rest.Generator.Models;

internal enum ValueFormat { Text, String, Boolean, Iso8601, Invariant, Enum }

// How a route, query or header value is written into the request. TypeName is the value's type,
// or a collection's element type, without Nullable<T>, fully qualified. EnumMembers pairs each
// member that has a [JsonStringEnumMemberName] with that name.
internal sealed record ValueFormatModel(
    string TypeName,
    bool IsValueType,
    ValueFormat Format,
    EquatableArray<(string Member, string Wire)> EnumMembers);
```

In `Models/ParameterModel.cs` add a last parameter `ValueFormatModel? Format = null` to `ParameterModel`.

- [ ] **Step 4: Compute it in `ModelExtractor`**

Add next to the other attribute-name constants:

```csharp
    private const string JsonStringEnumMemberNameAttr = "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute";
    private const string EnumerableOpenType = "System.Collections.Generic.IEnumerable<T>";
```

In `ExtractParameters`, pass the format as the new last argument:

```csharp
            var format = kind is ParameterKind.Path or ParameterKind.Query or ParameterKind.Header
                ? FormatOf(param.Type, isCollection)
                : null;
            result.Add(new ParameterModel(param.Name, typeName, kind, headerName, queryName ?? param.Name, isNullable, isCollection, format));
```

and add:

```csharp
    // The format of a value, or of a collection's elements, with Nullable<T> unwrapped. The enum
    // check comes before IFormattable, which enums also implement.
    private static ValueFormatModel FormatOf(ITypeSymbol type, bool isCollection)
    {
        if (isCollection)
            type = ElementType(type) ?? type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var enumMembers = new List<(string, string)>();
        var format = type.SpecialType switch
        {
            SpecialType.System_String => ValueFormat.String,
            SpecialType.System_Boolean => ValueFormat.Boolean,
            SpecialType.System_DateTime => ValueFormat.Iso8601,
            _ when type.TypeKind == TypeKind.Enum => EnumFormat(type, enumMembers),
            _ when name is "global::System.DateTimeOffset" or "global::System.DateOnly" or "global::System.TimeOnly" => ValueFormat.Iso8601,
            _ when type.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable") => ValueFormat.Invariant,
            _ => ValueFormat.Text,
        };
        return new ValueFormatModel(name, type.IsValueType, format, ToEquatable(enumMembers));
    }

    private static ValueFormat EnumFormat(ITypeSymbol type, List<(string, string)> members)
    {
        foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (!member.HasConstantValue) continue;
            foreach (var attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == JsonStringEnumMemberNameAttr
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is string wire)
                    members.Add((member.Name, wire));
            }
        }
        return ValueFormat.Enum;
    }

    private static ITypeSymbol? ElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return array.ElementType;
        if (type is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == EnumerableOpenType)
            return named.TypeArguments[0];
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.OriginalDefinition.ToDisplayString() == EnumerableOpenType)
                return iface.TypeArguments[0];
        }
        return null;
    }
```

Add `using System.Linq;` if the file lacks it.

- [ ] **Step 5: Emit the helpers and call them**

In `ClientEmitter.Emit`, directly after `foreach (var method in model.Methods) EmitMethod(...);`, add `EmitFormatHelpers(sb, model);`, and add:

```csharp
    // One __FormatValue overload per type a route, query or header value has, so a value is written
    // the same wherever it goes: invariant culture, ISO 8601 dates and times, lower-case booleans, and
    // an enum member's [JsonStringEnumMemberName]. A value type also gets a Nullable<T> overload, which
    // the null-checked call sites bind to.
    private static void EmitFormatHelpers(StringBuilder sb, ClientModel model)
    {
        var emitted = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var method in model.Methods)
        {
            foreach (var parameter in method.Parameters)
            {
                if (parameter.Format is { } format && emitted.Add(format.TypeName))
                    EmitFormatHelper(sb, format);
            }
        }
    }

    private static void EmitFormatHelper(StringBuilder sb, ValueFormatModel format)
    {
        var type = format.TypeName;
        sb.Append("    private static string __FormatValue(").Append(type).Append(" value) => ");
        switch (format.Format)
        {
            case ValueFormat.String:
                sb.AppendLine("value;");
                break;
            case ValueFormat.Boolean:
                sb.AppendLine("value ? \"true\" : \"false\";");
                break;
            case ValueFormat.Iso8601:
                sb.AppendLine("value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture);");
                break;
            case ValueFormat.Invariant:
                sb.AppendLine("value.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture);");
                break;
            case ValueFormat.Enum when format.EnumMembers.Count > 0:
                sb.AppendLine("value switch");
                sb.AppendLine("    {");
                foreach (var (member, wire) in format.EnumMembers)
                    sb.AppendLine($"        {type}.{EscapeKeyword(member)} => {Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(wire, quote: true)},");
                sb.AppendLine("        _ => value.ToString(),");
                sb.AppendLine("    };");
                break;
            default:
                sb.AppendLine(format.IsValueType ? "value.ToString();" : "value.ToString() ?? string.Empty;");
                break;
        }
        if (format.IsValueType)
            sb.AppendLine($"    private static string __FormatValue({type}? value) => value.HasValue ? __FormatValue(value.GetValueOrDefault()) : string.Empty;");
        sb.AppendLine();
    }

    private static string EscapeKeyword(string name)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? "@" + name
            : name;
```

Change the call sites:

- `RouteExpression`: `sb.Append("{Uri.EscapeDataString(__FormatValue(").Append(Identifier(parameter)).Append("))}");`
- `EmitUrlBuilding`, collection branch: `System.Uri.EscapeDataString(__FormatValue(__item))`.
- `EmitUrlBuilding`, nullable branch: `System.Uri.EscapeDataString(__FormatValue({Identifier(q)}))`, inside the existing `!= null` check.
- `EmitUrlBuilding`, plain branch: `System.Uri.EscapeDataString(__FormatValue({Identifier(q)}))`.
- `EmitRequestCreation`, header parameters:

```csharp
        foreach (var h in headerParams)
        {
            var value = Identifier(h);
            // A null value sends no header, rather than the header with an empty value.
            var add = $"__request.Headers.TryAddWithoutValidation(\"{h.HeaderName}\", __FormatValue({value}));";
            sb.AppendLine(h.IsNullable ? $"        if ({value} is not null) {add}" : $"        {add}");
        }
```

- [ ] **Step 6: Write the generator tests and update the old expectations**

```csharp
// tests/ZeroAlloc.Rest.Generator.Tests/GeneratorValueFormatTests.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

public class GeneratorValueFormatTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json.Serialization;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        public enum Mood { [JsonStringEnumMemberName("very-happy")] VeryHappy, Sad }
        [ZeroAllocRestClient]
        public interface IFormatApi
        {
            [Get("/at/{when}")]
            Task<string> AtAsync(DateTimeOffset when, [Query] bool active, [Query] Mood mood,
                [Query] int? limit, [Query] List<DateOnly> days, [Header("X-Trace")] string? trace,
                CancellationToken ct = default);
        }
        """;

    [Fact]
    public void EachValueType_GetsOneFormatHelper()
    {
        var client = Generate();

        Assert.Contains("private static string __FormatValue(global::System.DateTimeOffset value) => value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture);", client);
        Assert.Contains("private static string __FormatValue(bool value) => value ? \"true\" : \"false\";", client);
        Assert.Contains("global::MyApp.Mood.VeryHappy => \"very-happy\",", client);
        Assert.Contains("private static string __FormatValue(int? value) => value.HasValue ? __FormatValue(value.GetValueOrDefault()) : string.Empty;", client);
        Assert.Contains("private static string __FormatValue(global::System.DateOnly value)", client);
        Assert.Contains("private static string __FormatValue(string value) => value;", client);
    }

    [Fact]
    public void CallSites_UseTheHelper()
    {
        var client = Generate();

        Assert.Contains("{Uri.EscapeDataString(__FormatValue(when))}", client);
        Assert.Contains("System.Uri.EscapeDataString(__FormatValue(__item))", client);
        Assert.Contains("if (trace is not null) __request.Headers.TryAddWithoutValidation(\"X-Trace\", __FormatValue(trace));", client);
        Assert.DoesNotContain(".ToString()!", client);
    }

    private static string Generate()
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(Source)],
            [
                .. Basic.Reference.Assemblies.Net100.References.All,
                MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new RestClientGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return driver.GetRunResult().Results[0].GeneratedSources
            .First(s => string.Equals(s.HintName, "IFormatApi.g.cs", StringComparison.Ordinal)).SourceText.ToString();
    }
}
```

Update the old expectations to the new text:
- `RouteTemplateAnalyzerTests.cs` lines 126, 132, 138, 144, 227, 229 and 308: `{Uri.EscapeDataString(id.ToString())}` becomes `{Uri.EscapeDataString(__FormatValue(id))}`.
- `GeneratorEmissionTests.cs` around lines 701 to 745: read each header and query assertion and change `.ToString()` call-site text to the `__FormatValue(...)` form above. The assertion at 745, `DoesNotContain("__item?.ToString() ?? string.Empty")`, stays true.

- [ ] **Step 7: Run the tests, then the full suite**

Run: `dotnet test tests/ZeroAlloc.Rest.Generator.Tests tests/ZeroAlloc.Rest.Integration.Tests -c Release`
Expected: PASS, `ValueFormatTests` included. Then run the full-suite command and the AOT smoke command: the smoke's clients now carry `__FormatValue` helpers, which are reflection-free.

- [ ] **Step 8: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Generator tests
git commit -m "$(cat <<'EOF'
fix: write route, query and header values in their wire format

Generated clients called ToString on every value, so the URL depended on the current
culture, a bool was sent as True, and an enum as its C# name. Each client now has a
__FormatValue overload per value type: invariant culture, ISO 8601 for date and time
types, true and false, and an enum member's JsonStringEnumMemberName. A null header
value now sends no header instead of an empty one.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Shared naming helpers, `TypeRef` and `TypeMapper`

**Files:**
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/ZeroAlloc.Rest.Tools.Shared.projitems:12`
- Create: `src/ZeroAlloc.Rest.Tools.Shared/CSharpNames.cs`
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/OpenApiInterfaceGenerator.cs`: remove `Unique`, `ToIdentifier`, `Escape`, `Literal`, `Keywords`, `ToPascalCase`, call `CSharpNames`
- Create: `src/ZeroAlloc.Rest.Tools.Shared/Models/EquatableList.cs`, `Models/TypeRef.cs`, `Models/TypeRefKind.cs`
- Create: `src/ZeroAlloc.Rest.Tools.Shared/ISchemaTypeNamer.cs`, `TypeMapper.cs`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/TypeMapperTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `internal static class CSharpNames` with `string Pascal(string name, string fallback)`, `string ToIdentifier(string name, bool upperFirst, string? fallback = null)`, `string Escape(string identifier)`, `string Literal(string value)`, `string Unique(string identifier, HashSet<string> used)`, `string ToPascalCase(string s)`, `void AppendDocComment(StringBuilder sb, string indent, string? text)`.
  - `internal sealed class EquatableList<T> : IReadOnlyList<T>, IEquatable<EquatableList<T>>`, with `EquatableList(IEnumerable<T> items)` and `static EquatableList<T> Empty`.
  - `internal enum TypeRefKind { Primitive, Model, List, Dictionary, JsonElement, Stream }`.
  - `internal sealed record TypeRef(string Name, TypeRefKind Kind, bool IsValueType)` with statics `Int`, `Long`, `Float`, `Double`, `Decimal`, `Bool`, `String`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Guid`, `Uri`, `Bytes`, `Stream`, `JsonElement`.
  - `internal interface ISchemaTypeNamer { TypeRef Named(OpenApiSchema schema, string contextName, string path); void Unsupported(string path, string reason); }`.
  - `internal static class TypeMapper` with `TypeRef Map(OpenApiSchema? schema, string contextName, string path, ISchemaTypeNamer namer, bool isBody = false)`, `string Declare(TypeRef type, bool required, bool nullable)`, `bool IsEnum(OpenApiSchema schema)`, `bool NeedsModel(OpenApiSchema schema)`, `OpenApiSchema Unwrap(OpenApiSchema schema)`.

- [ ] **Step 1: Make the shared glob recursive**

In `ZeroAlloc.Rest.Tools.Shared.projitems` change the compile item to:

```xml
    <Compile Include="$(MSBuildThisFileDirectory)**\*.cs" />
```

- [ ] **Step 2: Write the failing `TypeMapper` tests**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/TypeMapperTests.cs
using Microsoft.OpenApi.Models;
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §5.1: one test per row, plus nullability.
public class TypeMapperTests
{
    [Theory]
    [InlineData("integer", null, "int")]
    [InlineData("integer", "int32", "int")]
    [InlineData("integer", "int64", "long")]
    [InlineData("number", null, "double")]
    [InlineData("number", "double", "double")]
    [InlineData("number", "float", "float")]
    [InlineData("number", "decimal", "decimal")]
    [InlineData("string", null, "string")]
    [InlineData("string", "date-time", "global::System.DateTimeOffset")]
    [InlineData("string", "date", "global::System.DateOnly")]
    [InlineData("string", "time", "global::System.TimeOnly")]
    [InlineData("string", "uuid", "global::System.Guid")]
    [InlineData("string", "uri", "global::System.Uri")]
    [InlineData("string", "byte", "byte[]")]
    [InlineData("string", "binary", "string")]
    [InlineData("boolean", null, "bool")]
    public void Primitive_MapsByTypeAndFormat(string type, string? format, string expected)
    {
        var namer = new RecordingNamer();

        var mapped = TypeMapper.Map(new OpenApiSchema { Type = type, Format = format }, "Ctx", "#/x", namer);

        Assert.Equal(expected, mapped.Name);
        Assert.Empty(namer.Calls);
        Assert.Empty(namer.Reports);
    }

    [Fact]
    public void Binary_InABody_IsAStream()
    {
        var mapped = TypeMapper.Map(new OpenApiSchema { Type = "string", Format = "binary" }, "Ctx", "#/x", new RecordingNamer(), isBody: true);

        Assert.Equal(TypeRef.Stream, mapped);
    }

    [Fact]
    public void Array_IsAListOfItsItems()
    {
        var schema = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "integer", Format = "int64" } };

        Assert.Equal("global::System.Collections.Generic.List<long>", TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()).Name);
    }

    [Fact]
    public void Array_OfNullableValueTypes_AnnotatesTheElement()
    {
        var schema = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "integer", Nullable = true } };

        Assert.Equal("global::System.Collections.Generic.List<int?>", TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()).Name);
    }

    [Fact]
    public void ObjectWithAdditionalPropertiesOnly_IsADictionary()
    {
        var schema = new OpenApiSchema { Type = "object", AdditionalProperties = new OpenApiSchema { Type = "string" } };

        Assert.Equal("global::System.Collections.Generic.Dictionary<string, string>", TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()).Name);
    }

    [Fact]
    public void InlineObjectWithoutProperties_IsADictionaryOfJsonElement()
    {
        var mapped = TypeMapper.Map(new OpenApiSchema { Type = "object" }, "Ctx", "#/x", new RecordingNamer());

        Assert.Equal("global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.JsonElement>", mapped.Name);
    }

    [Fact]
    public void Ref_IsNamedByTheNamer()
    {
        var namer = new RecordingNamer();
        var schema = new OpenApiSchema
        {
            Type = "object",
            Reference = new OpenApiReference { Id = "Pet", Type = ReferenceType.Schema },
            Properties = { ["id"] = new OpenApiSchema { Type = "integer" } },
        };

        var mapped = TypeMapper.Map(schema, "Ctx", "#/x", namer);

        Assert.Equal("Pet", mapped.Name);
        Assert.Equal(("Ctx", "#/x"), Assert.Single(namer.Calls));
    }

    [Fact]
    public void RefToAPrimitive_MapsToThePrimitive()
    {
        var schema = new OpenApiSchema
        {
            Type = "string",
            Format = "uuid",
            Reference = new OpenApiReference { Id = "Id", Type = ReferenceType.Schema },
        };

        Assert.Equal(TypeRef.Guid, TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    public void Enum_IsNamedByTheNamer(string type)
    {
        var namer = new RecordingNamer();
        var schema = new OpenApiSchema { Type = type, Enum = { new Microsoft.OpenApi.Any.OpenApiString("a") } };

        TypeMapper.Map(schema, "Ctx", "#/x", namer);

        Assert.Single(namer.Calls);
    }

    [Fact]
    public void SingleAllOfWrapper_MapsToItsPart()
    {
        var schema = new OpenApiSchema { Nullable = true, AllOf = { new OpenApiSchema { Type = "boolean" } } };

        Assert.Equal(TypeRef.Bool, TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()));
    }

    [Fact]
    public void Not_IsJsonElement_AndReported()
    {
        var namer = new RecordingNamer();

        var mapped = TypeMapper.Map(new OpenApiSchema { Not = new OpenApiSchema { Type = "string" } }, "Ctx", "#/x", namer);

        Assert.Equal(TypeRef.JsonElement, mapped);
        Assert.Equal(("#/x", "it uses 'not', which has no C# equivalent"), Assert.Single(namer.Reports));
    }

    [Fact]
    public void NoTypeAndNoComposition_IsJsonElement_AndReported()
    {
        var namer = new RecordingNamer();

        Assert.Equal(TypeRef.JsonElement, TypeMapper.Map(new OpenApiSchema(), "Ctx", "#/x", namer));
        Assert.Equal(("#/x", "it has neither a type nor a composition"), Assert.Single(namer.Reports));
    }

    [Theory]
    [InlineData(true, false, "long")]
    [InlineData(true, true, "long?")]
    [InlineData(false, false, "long?")]
    [InlineData(false, true, "long?")]
    public void Declare_AppliesRequiredAndNullable(bool required, bool nullable, string expected)
        => Assert.Equal(expected, TypeMapper.Declare(TypeRef.Long, required, nullable));

    [Fact]
    public void Declare_AnnotatesReferenceTypes()
        => Assert.Equal("string?", TypeMapper.Declare(TypeRef.String, required: false, nullable: false));

    // Records what TypeMapper asks for: Calls for each schema it wants named, Reports for each ZRT002.
    private sealed class RecordingNamer : ISchemaTypeNamer
    {
        public List<(string ContextName, string Path)> Calls { get; } = [];

        public List<(string Path, string Reason)> Reports { get; } = [];

        public TypeRef Named(OpenApiSchema schema, string contextName, string path)
        {
            Calls.Add((contextName, path));
            return new TypeRef(schema.Reference?.Id ?? contextName, TypeRefKind.Model, TypeMapper.IsEnum(schema));
        }

        public void Unsupported(string path, string reason) => Reports.Add((path, reason));
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: FAIL with CS0246: `TypeMapper`, `TypeRef`, `ISchemaTypeNamer` do not exist.

- [ ] **Step 4: Move the naming helpers into `CSharpNames`**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/CSharpNames.cs
using System.Globalization;
using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Turns names from a spec into C# identifiers, literals and doc comments. The interface and the
// models share these, so a name is sanitised the same way wherever it appears.
internal static class CSharpNames
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    // A type or member name: PascalCase, sanitised, never a keyword since keywords are lower-case.
    internal static string Pascal(string name, string fallback)
        => ToIdentifier(ToPascalCase(name), upperFirst: true, fallback);

    // Turns a name from the spec into a C# identifier. The first letter is cased as asked. A
    // character an identifier cannot hold is dropped and the letter after it upper-cased, and a
    // leading digit gets an underscore: UserId and user-id both become userId.
    internal static string ToIdentifier(string name, bool upperFirst, string? fallback = null)
    {
        var sb = new StringBuilder(name.Length);
        var upperNext = false;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                upperNext = sb.Length > 0;
                continue;
            }
            if (sb.Length == 0)
                sb.Append(upperFirst ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            else
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }
        if (sb.Length == 0)
            return fallback ?? (upperFirst ? "Operation" : "value");
        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    // A keyword is escaped with @. The source generator matches a {token} against the name without it.
    internal static string Escape(string identifier) => Keywords.Contains(identifier) ? "@" + identifier : identifier;

    internal static string Literal(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ => c.ToString(),
            });
        }
        return sb.Append('"').ToString();
    }

    internal static string Unique(string identifier, HashSet<string> used)
    {
        var candidate = identifier;
        for (var n = 2; !used.Add(candidate); n++)
            candidate = identifier + n.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>Converts snake_case, kebab-case, or plain strings to PascalCase.</summary>
    internal static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var result = new StringBuilder();
        foreach (var part in s.Split('_', '-'))
        {
            if (part.Length == 0) continue;
            result.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }
        return result.Length > 0 ? result.ToString() : s;
    }

    // A description from the spec as a <summary>, one /// line per line, XML-escaped.
    internal static void AppendDocComment(StringBuilder sb, string indent, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.Append(indent).AppendLine("/// <summary>");
        foreach (var line in text.Trim().Split('\n'))
            sb.Append(indent).Append("/// ").AppendLine(XmlEscape(line.TrimEnd('\r')));
        sb.Append(indent).AppendLine("/// </summary>");
    }

    private static string XmlEscape(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
```

In `OpenApiInterfaceGenerator.cs`, delete the private `Unique`, `ToIdentifier`, `Escape`, `Literal`, `Keywords` and `ToPascalCase`, and prefix every call to them with `CSharpNames.`.

- [ ] **Step 5: Add the model types**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/EquatableList.cs
using System.Collections;

namespace ZeroAlloc.Rest.Tools;

// A read-only list compared by its items, so the intermediate model is value-equal.
internal sealed class EquatableList<T> : IReadOnlyList<T>, IEquatable<EquatableList<T>>
{
    private readonly T[] _items;

    internal EquatableList(IEnumerable<T> items) => _items = [.. items];

    internal static EquatableList<T> Empty { get; } = new([]);

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(EquatableList<T>? other)
        => other is not null && _items.AsSpan().SequenceEqual(other._items, EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => Equals(obj as EquatableList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => "[" + string.Join(", ", _items) + "]";
}
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/TypeRefKind.cs
namespace ZeroAlloc.Rest.Tools;

internal enum TypeRefKind
{
    Primitive,
    Model,
    List,
    Dictionary,
    JsonElement,
    Stream,
}
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/TypeRef.cs
namespace ZeroAlloc.Rest.Tools;

// A C# type the generated code uses: its name as emitted and what kind of type it is. Framework
// types are global::-qualified, because a model named from the spec may shadow them. Nullability is
// not part of it; TypeMapper.Declare adds the ? a property or parameter needs.
internal sealed record TypeRef(string Name, TypeRefKind Kind, bool IsValueType)
{
    internal static TypeRef Int { get; } = new("int", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Long { get; } = new("long", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Float { get; } = new("float", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Double { get; } = new("double", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Decimal { get; } = new("decimal", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Bool { get; } = new("bool", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef String { get; } = new("string", TypeRefKind.Primitive, IsValueType: false);
    internal static TypeRef DateTimeOffset { get; } = new("global::System.DateTimeOffset", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef DateOnly { get; } = new("global::System.DateOnly", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef TimeOnly { get; } = new("global::System.TimeOnly", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Guid { get; } = new("global::System.Guid", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Uri { get; } = new("global::System.Uri", TypeRefKind.Primitive, IsValueType: false);
    internal static TypeRef Bytes { get; } = new("byte[]", TypeRefKind.Primitive, IsValueType: false);
    internal static TypeRef Stream { get; } = new("global::System.IO.Stream", TypeRefKind.Stream, IsValueType: false);
    internal static TypeRef JsonElement { get; } = new("global::System.Text.Json.JsonElement", TypeRefKind.JsonElement, IsValueType: true);
}
```

- [ ] **Step 6: Add the namer contract and `TypeMapper`**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/ISchemaTypeNamer.cs
using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// What TypeMapper asks for when a schema needs a type it cannot spell by itself.
internal interface ISchemaTypeNamer
{
    // The type of a schema that needs a generated model: a component object, an inline object, an
    // enum or a composition. contextName names an inline schema; path locates it for messages.
    TypeRef Named(OpenApiSchema schema, string contextName, string path);

    // Reports a schema mapped to JsonElement, as warning ZRT002.
    void Unsupported(string path, string reason);
}
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/TypeMapper.cs
using System.Globalization;
using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// Maps a schema to the C# type the generated code uses for it, spec §5.1. A schema that needs a
// generated type is named by the namer; everything else maps structurally, so a component that is
// only a string with a format maps to its primitive, not to a model.
internal static class TypeMapper
{
    private const string List = "global::System.Collections.Generic.List<";
    private const string Dictionary = "global::System.Collections.Generic.Dictionary<string, ";

    internal static TypeRef Map(OpenApiSchema? schema, string contextName, string path, ISchemaTypeNamer namer, bool isBody = false)
    {
        if (schema is null)
            return Unsupported(namer, path, "it has no schema");
        if (schema.Not is not null)
            return Unsupported(namer, path, "it uses 'not', which has no C# equivalent");
        if (SingleWrapped(schema) is { } wrapped)
            return Map(wrapped.Part, contextName, path + wrapped.Suffix, namer, isBody);
        if (NeedsModel(schema))
            return namer.Named(schema, contextName, path);
        return schema.Type switch
        {
            "integer" => string.Equals(schema.Format, "int64", StringComparison.Ordinal) ? TypeRef.Long : TypeRef.Int,
            "number" => Number(schema.Format),
            "boolean" => TypeRef.Bool,
            "string" => Text(schema.Format, isBody),
            "array" => ListOf(schema, contextName, path, namer),
            "object" => DictionaryOf(schema, contextName, path, namer),
            null when schema.AdditionalProperties is not null => DictionaryOf(schema, contextName, path, namer),
            null => Unsupported(namer, path, "it has neither a type nor a composition"),
            _ => Unsupported(namer, path, $"its type '{schema.Type}' is not an OpenAPI 3.0 type"),
        };
    }

    // Spec §5.2: required and not nullable is T; anything that may be absent or null is T?.
    internal static string Declare(TypeRef type, bool required, bool nullable)
        => required && !nullable ? type.Name : type.Name + "?";

    internal static bool IsEnum(OpenApiSchema schema)
        => schema.Enum.Count > 0 && schema.Type is "string" or "integer";

    // Design decision 6: enums, compositions and objects with properties get a model, and so does a
    // named component object with no properties, which becomes an empty record named after it.
    internal static bool NeedsModel(OpenApiSchema schema)
        => IsEnum(schema)
            || schema.AllOf.Count > 0 || schema.OneOf.Count > 0 || schema.AnyOf.Count > 0
            || schema.Properties.Count > 0
            || (schema.Reference is not null
                && string.Equals(schema.Type, "object", StringComparison.Ordinal)
                && schema.AdditionalProperties is null);

    // The schema a single-part wrapper stands for, or the schema itself.
    internal static OpenApiSchema Unwrap(OpenApiSchema schema)
        => SingleWrapped(schema) is { } wrapped ? Unwrap(wrapped.Part) : schema;

    // Design decision 7: allOf, oneOf or anyOf with one part and nothing else, which specs use to
    // attach nullable or a description to a $ref, stands for that part. A named component is never
    // a wrapper: it gets its own type.
    private static (OpenApiSchema Part, string Suffix)? SingleWrapped(OpenApiSchema schema)
    {
        if (schema.Reference is not null || schema.Properties.Count > 0 || schema.Discriminator is not null)
            return null;
        if (schema.AllOf.Count + schema.OneOf.Count + schema.AnyOf.Count != 1)
            return null;
        if (schema.AllOf.Count == 1) return (schema.AllOf[0], "/allOf/0");
        if (schema.OneOf.Count == 1) return (schema.OneOf[0], "/oneOf/0");
        return (schema.AnyOf[0], "/anyOf/0");
    }

    private static TypeRef Number(string? format) => format switch
    {
        "float" => TypeRef.Float,
        "decimal" => TypeRef.Decimal,
        _ => TypeRef.Double,
    };

    private static TypeRef Text(string? format, bool isBody) => format switch
    {
        "date-time" => TypeRef.DateTimeOffset,
        "date" => TypeRef.DateOnly,
        "time" => TypeRef.TimeOnly,
        "uuid" => TypeRef.Guid,
        "uri" => TypeRef.Uri,
        "byte" => TypeRef.Bytes,
        "binary" when isBody => TypeRef.Stream,
        _ => TypeRef.String,
    };

    private static TypeRef ListOf(OpenApiSchema schema, string contextName, string path, ISchemaTypeNamer namer)
    {
        var item = Map(schema.Items, contextName + "Item", path + "/items", namer);
        return new TypeRef(List + Element(item, schema.Items) + ">", TypeRefKind.List, IsValueType: false);
    }

    private static TypeRef DictionaryOf(OpenApiSchema schema, string contextName, string path, ISchemaTypeNamer namer)
    {
        var value = schema.AdditionalProperties is null
            ? TypeRef.JsonElement
            : Map(schema.AdditionalProperties, contextName + "Value", path + "/additionalProperties", namer);
        return new TypeRef(Dictionary + Element(value, schema.AdditionalProperties) + ">", TypeRefKind.Dictionary, IsValueType: false);
    }

    // A nullable element is annotated only when it is a value type: the JSON context registers the
    // collection with typeof, which takes no nullable reference type annotations.
    private static string Element(TypeRef type, OpenApiSchema? schema)
        => schema?.Nullable == true && type.IsValueType ? type.Name + "?" : type.Name;

    private static TypeRef Unsupported(ISchemaTypeNamer namer, string path, string reason)
    {
        namer.Unsupported(path, reason);
        return TypeRef.JsonElement;
    }
}
```

`schema.Type is "string" or "integer"` binds as `schema.Type is ("string" or "integer")`; `IsEnum` needs no parentheses. Drop `using System.Globalization;` if the analyzer reports it unused.

- [ ] **Step 7: Run the tests, then the full suite**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: PASS, the existing interface and route binding tests included: the naming refactor changes no output. Then run the full-suite command.

- [ ] **Step 8: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: map OpenAPI schemas to C# types by type and format

TypeMapper maps a schema to a TypeRef per spec section 5.1: int64 to long, date-time to
DateTimeOffset, uuid to Guid, arrays to List, additionalProperties to Dictionary, and
anything unsupported to JsonElement with a reported reason. The naming helpers move to
CSharpNames so the interface and the models share them.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: `SchemaModelBuilder`: records, enums, naming and ZRT002

**Files:**
- Create: `src/ZeroAlloc.Rest.Tools.Shared/Models/ModelDefinition.cs`, `RecordModel.cs`, `PropertyModel.cs`, `EnumModel.cs`, `EnumMemberModel.cs`
- Create: `src/ZeroAlloc.Rest.Tools.Shared/SchemaModelBuilder.cs`
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/OpenApiWarning.cs`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/ModelFixture.cs`, `SchemaModelBuilderTests.cs`

**Interfaces:**
- Consumes: `TypeMapper`, `ISchemaTypeNamer`, `TypeRef`, `EquatableList<T>`, `CSharpNames` from Task 6.
- Produces:
  - `internal abstract record ModelDefinition(string Name, string? Description)`.
  - `internal sealed record RecordModel(string Name, string? Description, EquatableList<PropertyModel> Properties, string? BaseName = null) : ModelDefinition(Name, Description)`.
  - `internal sealed record PropertyModel(string Name, string WireName, TypeRef Type, bool Required, bool Nullable, string? Description)`.
  - `internal sealed record EnumModel(string Name, string? Description, bool IsString, string UnderlyingType, EquatableList<EnumMemberModel> Members) : ModelDefinition(Name, Description)`; `UnderlyingType` is `"int"` or `"long"`.
  - `internal sealed record EnumMemberModel(string Name, string WireValue)`; for an integer enum `WireValue` is the invariant integer literal.
  - `internal sealed class SchemaModelBuilder : ISchemaTypeNamer` with `SchemaModelBuilder(IEnumerable<string> reservedNames, List<OpenApiWarning> warnings)` and `EquatableList<ModelDefinition> Build()`.
  - `OpenApiWarning.SchemaMappedToJsonElement = "ZRT002"` and `static OpenApiWarning MappedToJsonElement(string path, string reason)`.
  - Test helper `ModelFixture.Build(string schemasYaml)` returning `(EquatableList<ModelDefinition> Models, List<OpenApiWarning> Warnings)`.

- [ ] **Step 1: Write the test fixture**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/ModelFixture.cs
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// Builds the models for the components of a small spec, as the generator does for the schemas
// its interface references: each component is mapped in declaration order, then built.
internal static class ModelFixture
{
    internal static readonly string[] ReservedNames = ["IMyApi", "MyApiClient", "MyApiJsonContext"];

    // schemasYaml holds the entries of components/schemas, each line indented by four spaces.
    internal static OpenApiDocument Parse(string schemasYaml)
    {
        var yaml = $"""
            openapi: 3.0.0
            info:
              title: Test
              version: "1"
            paths: {"{}"}
            components:
              schemas:
            {schemasYaml}
            """;
        var document = new OpenApiStringReader().Read(yaml, out var diagnostic);
        Assert.Empty(diagnostic.Errors);
        return document;
    }

    internal static (EquatableList<ModelDefinition> Models, List<OpenApiWarning> Warnings) Build(string schemasYaml)
    {
        var document = Parse(schemasYaml);
        var warnings = new List<OpenApiWarning>();
        var builder = new SchemaModelBuilder(ReservedNames, warnings);
        foreach (var (id, schema) in document.Components.Schemas)
            TypeMapper.Map(schema, id, "#/components/schemas/" + id, builder);
        return (builder.Build(), warnings);
    }

    internal static PropertyModel Required(string name, string wireName, TypeRef type)
        => new(name, wireName, type, Required: true, Nullable: false, Description: null);

    internal static PropertyModel Optional(string name, string wireName, TypeRef type)
        => new(name, wireName, type, Required: false, Nullable: false, Description: null);

    internal static TypeRef Model(string name) => new(name, TypeRefKind.Model, IsValueType: false);

    internal static TypeRef Enum(string name) => new(name, TypeRefKind.Model, IsValueType: true);

    internal static EquatableList<T> List<T>(params T[] items) => new(items);
}
```

The `{schemasYaml}` hole sits at the indentation of the other raw-string lines, which the closing quotes remove, so pass entries whose every line starts with four spaces, as the tests below do.

- [ ] **Step 2: Write the failing builder tests**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/SchemaModelBuilderTests.cs
using Xunit;
using static ZeroAlloc.Rest.Tools.Tests.ModelFixture;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §5.2 to §5.7 and §10.1: the intermediate model is value-equal, so each test states the
// whole model it expects.
public class SchemaModelBuilderTests
{
    [Fact]
    public void ObjectSchema_IsARecord_WithRequiredOptionalAndNullableProperties()
    {
        var (models, warnings) = Build("""
                Pet:
                  type: object
                  description: A pet.
                  required: [id, owner]
                  properties:
                    id:
                      type: integer
                      format: int64
                    name:
                      type: string
                      description: Its <name>.
                    owner:
                      type: string
                      nullable: true
            """);

        Assert.Empty(warnings);
        Assert.Equal(
            List<ModelDefinition>(new RecordModel("Pet", "A pet.", List(
                Required("Id", "id", TypeRef.Long),
                new PropertyModel("Name", "name", TypeRef.String, Required: false, Nullable: false, Description: "Its <name>."),
                new PropertyModel("Owner", "owner", TypeRef.String, Required: true, Nullable: true, Description: null)))),
            models);
    }

    [Fact]
    public void InlineObject_IsNamedFromItsPath()
    {
        var (models, _) = Build("""
                User:
                  type: object
                  properties:
                    address:
                      type: object
                      properties:
                        city:
                          type: string
            """);

        Assert.Equal(
            List<ModelDefinition>(
                new RecordModel("User", null, List(Optional("Address", "address", Model("UserAddress")))),
                new RecordModel("UserAddress", null, List(Optional("City", "city", TypeRef.String)))),
            models);
    }

    [Fact]
    public void NameCollisions_GetANumericSuffix()
    {
        var (models, _) = Build("""
                User:
                  type: object
                  properties:
                    address:
                      type: object
                      properties:
                        city:
                          type: string
                UserAddress:
                  type: object
                  properties:
                    line:
                      type: string
                MyApiJsonContext:
                  type: object
                  properties:
                    x:
                      type: string
            """);

        Assert.Equal(
            new[] { "User", "UserAddress", "MyApiJsonContext2", "UserAddress2" },
            models.Select(m => m.Name));
    }

    [Fact]
    public void PropertyNamedLikeItsRecordOrARecordMember_GetsANumericSuffix()
    {
        var (models, _) = Build("""
                Error:
                  type: object
                  properties:
                    error:
                      type: string
                    toString:
                      type: string
            """);

        var record = Assert.IsType<RecordModel>(Assert.Single(models));
        Assert.Equal(new[] { "Error2", "ToString2" }, record.Properties.Select(p => p.Name));
    }

    [Fact]
    public void EmptyComponentObject_IsAnEmptyRecord()
    {
        var (models, _) = Build("""
                Marker:
                  type: object
            """);

        Assert.Equal(List<ModelDefinition>(new RecordModel("Marker", null, EquatableList<PropertyModel>.Empty)), models);
    }

    [Fact]
    public void StringEnum_KeepsWireValues_AndSanitisesNames()
    {
        var (models, _) = Build("""
                Status:
                  type: string
                  enum: [available, pending review, "", "2fa"]
            """);

        Assert.Equal(
            List<ModelDefinition>(new EnumModel("Status", null, IsString: true, "int", List(
                new EnumMemberModel("Available", "available"),
                new EnumMemberModel("PendingReview", "pending review"),
                new EnumMemberModel("Empty", ""),
                new EnumMemberModel("_2fa", "2fa")))),
            models);
    }

    [Fact]
    public void IntegerEnum_NamesMembersByValue_OrByXEnumVarnames()
    {
        var (models, _) = Build("""
                Priority:
                  type: integer
                  enum: [1, 2, -3]
                Level:
                  type: integer
                  format: int64
                  enum: [10, 20]
                  x-enum-varnames: [Low, High]
            """);

        Assert.Equal(
            List<ModelDefinition>(
                new EnumModel("Priority", null, IsString: false, "int", List(
                    new EnumMemberModel("Value1", "1"),
                    new EnumMemberModel("Value2", "2"),
                    new EnumMemberModel("ValueMinus3", "-3"))),
                new EnumModel("Level", null, IsString: false, "long", List(
                    new EnumMemberModel("Low", "10"),
                    new EnumMemberModel("High", "20")))),
            models);
    }

    [Fact]
    public void EnumProperty_IsAValueTypeModel()
    {
        var (models, _) = Build("""
                Pet:
                  type: object
                  properties:
                    status:
                      type: string
                      enum: [a, b]
            """);

        Assert.Equal(Optional("Status", "status", Enum("PetStatus")), ((RecordModel)models[0]).Properties[0]);
        Assert.IsType<EnumModel>(models[1]);
    }

    [Fact]
    public void UnsupportedSchema_IsJsonElement_WithZrt002()
    {
        var (models, warnings) = Build("""
                Holder:
                  type: object
                  properties:
                    anything: {}
                    notString:
                      not:
                        type: string
            """);

        Assert.Equal(
            new[] { TypeRef.JsonElement, TypeRef.JsonElement },
            ((RecordModel)models[0]).Properties.Select(p => p.Type));
        Assert.Equal(new[] { "ZRT002", "ZRT002" }, warnings.Select(w => w.Code));
        Assert.Equal(
            "Schema '#/components/schemas/Holder/properties/anything' is mapped to JsonElement, because it has neither a type nor a composition.",
            warnings[0].Message);
    }

    [Fact]
    public void Models_AreValueEqual_AcrossBuilds()
    {
        const string Spec = """
                Pet:
                  type: object
                  properties:
                    tags:
                      type: array
                      items:
                        type: string
            """;

        Assert.Equal(Build(Spec).Models, Build(Spec).Models);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: FAIL with CS0246 for `SchemaModelBuilder`, `RecordModel`, `EnumModel` and the rest.

- [ ] **Step 4: Add the intermediate model**

One type per file, as Meziantou's MA0048 requires in the tool projects:

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/ModelDefinition.cs
namespace ZeroAlloc.Rest.Tools;

// A type the tool generates. The intermediate model holds no source text, so it is unit-tested
// on its own, and the emitters are pure functions of it.
internal abstract record ModelDefinition(string Name, string? Description);
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/RecordModel.cs
namespace ZeroAlloc.Rest.Tools;

// A public sealed record. BaseName is set for a variant of a discriminated hierarchy.
internal sealed record RecordModel(string Name, string? Description, EquatableList<PropertyModel> Properties, string? BaseName = null)
    : ModelDefinition(Name, Description);
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/PropertyModel.cs
namespace ZeroAlloc.Rest.Tools;

internal sealed record PropertyModel(string Name, string WireName, TypeRef Type, bool Required, bool Nullable, string? Description);
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/EnumModel.cs
namespace ZeroAlloc.Rest.Tools;

// A string enum, written by name, or an integer enum, written by value. UnderlyingType is int or long.
internal sealed record EnumModel(string Name, string? Description, bool IsString, string UnderlyingType, EquatableList<EnumMemberModel> Members)
    : ModelDefinition(Name, Description);
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/EnumMemberModel.cs
namespace ZeroAlloc.Rest.Tools;

// WireValue is the string sent for a string enum, or the invariant integer literal for an integer one.
internal sealed record EnumMemberModel(string Name, string WireValue);
```

- [ ] **Step 5: Add ZRT002**

In `OpenApiWarning.cs`, after `CookieParameterNotEmitted`:

```csharp
    // A schema the generated code cannot type, mapped to JsonElement.
    internal const string SchemaMappedToJsonElement = "ZRT002";

    internal static OpenApiWarning MappedToJsonElement(string path, string reason)
        => new(SchemaMappedToJsonElement, $"Schema '{path}' is mapped to JsonElement, because {reason}.");
```

- [ ] **Step 6: Write the builder**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/SchemaModelBuilder.cs
using System.Globalization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// Reads the schemas the interface references into the intermediate model, spec §4. It owns naming:
// every generated type gets a unique name, and a schema is built once however often it is
// referenced. Models are built lazily in the order they are first referenced, so the output
// follows the spec and holds only what the interface uses.
internal sealed class SchemaModelBuilder : ISchemaTypeNamer
{
    // Members every record has. A property of the same name would not compile, so it gets a suffix.
    private static readonly string[] RecordMembers =
        ["EqualityContract", "Equals", "GetHashCode", "ToString", "PrintMembers", "Deconstruct", "GetType", "MemberwiseClone", "Finalize"];

    private readonly List<OpenApiWarning> _warnings;
    private readonly HashSet<string> _typeNames;
    private readonly Dictionary<OpenApiSchema, TypeRef> _types = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<(OpenApiSchema Schema, string Name, string Path)> _pending = new();
    private readonly List<ModelDefinition> _models = [];

    // reservedNames are the names the generated file already uses: the interface, the client the
    // source generator derives from it, and the JSON context.
    internal SchemaModelBuilder(IEnumerable<string> reservedNames, List<OpenApiWarning> warnings)
    {
        _warnings = warnings;
        _typeNames = new HashSet<string>(reservedNames, StringComparer.Ordinal);
    }

    public TypeRef Named(OpenApiSchema schema, string contextName, string path)
    {
        if (_types.TryGetValue(schema, out var known))
            return known;
        var name = Reserve(schema.Reference?.Id ?? contextName);
        var type = new TypeRef(name, TypeRefKind.Model, IsValueType: TypeMapper.IsEnum(schema));
        _types.Add(schema, type);
        _pending.Enqueue((schema, name, schema.Reference is null ? path : "#/components/schemas/" + schema.Reference.Id));
        return type;
    }

    public void Unsupported(string path, string reason) => _warnings.Add(OpenApiWarning.MappedToJsonElement(path, reason));

    // Builds every model registered so far, and those their properties register in turn.
    internal EquatableList<ModelDefinition> Build()
    {
        while (_pending.TryDequeue(out var next))
            _models.Add(BuildModel(next.Schema, next.Name, next.Path));
        return new EquatableList<ModelDefinition>(_models);
    }

    // A type name and its {Name}Converter are reserved together, so a converter never clashes.
    private string Reserve(string name)
    {
        var baseName = CSharpNames.Pascal(name, "Model");
        var candidate = baseName;
        for (var n = 2; _typeNames.Contains(candidate) || _typeNames.Contains(candidate + "Converter"); n++)
            candidate = baseName + n.ToString(CultureInfo.InvariantCulture);
        _typeNames.Add(candidate);
        _typeNames.Add(candidate + "Converter");
        return candidate;
    }

    private ModelDefinition BuildModel(OpenApiSchema schema, string name, string path)
        => TypeMapper.IsEnum(schema) ? BuildEnum(schema, name) : BuildRecord(schema, name, path);

    private RecordModel BuildRecord(OpenApiSchema schema, string name, string path)
    {
        var used = new HashSet<string>(RecordMembers, StringComparer.Ordinal) { name };
        var properties = new List<PropertyModel>(schema.Properties.Count);
        foreach (var (wireName, propertySchema) in schema.Properties)
        {
            var identifier = CSharpNames.Unique(CSharpNames.Pascal(wireName, "Property"), used);
            var type = TypeMapper.Map(propertySchema, name + identifier, path + "/properties/" + wireName, this);
            properties.Add(new PropertyModel(identifier, wireName, type, schema.Required.Contains(wireName), propertySchema.Nullable, propertySchema.Description));
        }
        return new RecordModel(name, schema.Description, new EquatableList<PropertyModel>(properties));
    }

    private static EnumModel BuildEnum(OpenApiSchema schema, string name)
    {
        var isString = string.Equals(schema.Type, "string", StringComparison.Ordinal);
        var isInt64 = string.Equals(schema.Format, "int64", StringComparison.Ordinal);
        var varNames = VarNames(schema);
        var used = new HashSet<string>(StringComparer.Ordinal) { name };
        var members = new List<EnumMemberModel>(schema.Enum.Count);
        for (var i = 0; i < schema.Enum.Count; i++)
        {
            // A null in a nullable enum is not a member.
            if (WireValue(schema.Enum[i]) is not { } wire)
                continue;
            string member;
            if (isString)
            {
                member = CSharpNames.Pascal(wire, "Empty");
            }
            else
            {
                var value = long.Parse(wire, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                isInt64 |= value is < int.MinValue or > int.MaxValue;
                member = i < varNames.Count
                    ? CSharpNames.Pascal(varNames[i], "Value")
                    : (value < 0 ? "ValueMinus" + wire[1..] : "Value" + wire);
            }
            members.Add(new EnumMemberModel(CSharpNames.Unique(member, used), wire));
        }
        return new EnumModel(name, schema.Description, isString, isInt64 ? "long" : "int", new EquatableList<EnumMemberModel>(members));
    }

    private static string? WireValue(IOpenApiAny value) => value switch
    {
        OpenApiString text => text.Value,
        OpenApiInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
        OpenApiLong number => number.Value.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static List<string> VarNames(OpenApiSchema schema)
    {
        var names = new List<string>();
        if (schema.Extensions.TryGetValue("x-enum-varnames", out IOpenApiExtension? extension) && extension is OpenApiArray array)
        {
            foreach (var item in array)
                names.Add(item is OpenApiString text ? text.Value : "");
        }
        return names;
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~SchemaModelBuilderTests"`
Expected: PASS. If Microsoft.OpenApi parses `enum: [available, ...]` values of a string enum as another `IOpenApiAny` type, extend `WireValue` with that type's case rather than weakening the test.

- [ ] **Step 8: Run the full suite, then commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: build records and enums from OpenAPI schemas

SchemaModelBuilder reads the schemas an interface references into a value-equal model:
records with required, optional and nullable properties, string and integer enums, and
inline objects named from their path. Names are sanitised and collisions get a numeric
suffix. A schema the generator cannot type maps to JsonElement with warning ZRT002.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: `allOf` flattening, conflicts and recursion

**Files:**
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/SchemaModelBuilder.cs`: `Named`, `BuildRecord`; add `Collect`, `Shape`, `Properties`, `HasRecursiveAllOf`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/SchemaModelBuilderTests.cs`

**Interfaces:**
- Consumes: Task 7's builder.
- Produces: private `void Collect(OpenApiSchema schema, string path, string recordName, List<CollectedProperty> collected, HashSet<string> required, OpenApiSchema? skip)` and `EquatableList<PropertyModel> Properties(string recordName, List<CollectedProperty> collected, HashSet<string> required)`, which Tasks 9 and 10 reuse; a nested `private readonly record struct CollectedProperty(string WireName, OpenApiSchema Schema, string Path)`.

- [ ] **Step 1: Write the failing tests**

Append to `SchemaModelBuilderTests`:

```csharp
    [Fact]
    public void AllOf_IsFlattened_PartsFirst_RequiredIfAnyPartRequiresIt()
    {
        var (models, _) = Build("""
                NewPet:
                  type: object
                  required: [name]
                  properties:
                    name:
                      type: string
                    tag:
                      type: string
                Pet:
                  allOf:
                    - $ref: '#/components/schemas/NewPet'
                    - type: object
                      required: [id, tag]
                      properties:
                        id:
                          type: integer
                          format: int64
            """);

        Assert.Equal(
            new RecordModel("Pet", null, List(
                Required("Name", "name", TypeRef.String),
                Required("Tag", "tag", TypeRef.String),
                Required("Id", "id", TypeRef.Long))),
            models[1]);
    }

    [Fact]
    public void AllOf_WithConflictingPropertyTypes_IsAGenerationError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build("""
                A:
                  type: object
                  properties:
                    id:
                      type: integer
                B:
                  allOf:
                    - $ref: '#/components/schemas/A'
                    - type: object
                      properties:
                        id:
                          type: string
            """));

        Assert.Equal(
            "Schema 'B': property 'id' has conflicting types in its allOf parts, 'integer' and 'string'.",
            error.Message);
    }

    [Fact]
    public void AllOf_WithTheSamePropertyTwice_KeepsOne()
    {
        var (models, _) = Build("""
                B:
                  allOf:
                    - type: object
                      properties:
                        id:
                          type: integer
                    - type: object
                      required: [id]
                      properties:
                        id:
                          type: integer
            """);

        Assert.Equal(List(Required("Id", "id", TypeRef.Int)), ((RecordModel)models[0]).Properties);
    }

    [Fact]
    public void RecursiveAllOf_IsJsonElement_WithZrt002()
    {
        var (models, warnings) = Build("""
                Holder:
                  type: object
                  properties:
                    loop:
                      $ref: '#/components/schemas/A'
                A:
                  allOf:
                    - $ref: '#/components/schemas/B'
                B:
                  allOf:
                    - $ref: '#/components/schemas/A'
            """);

        Assert.Equal(TypeRef.JsonElement, ((RecordModel)models[0]).Properties[0].Type);
        Assert.DoesNotContain(models, m => m.Name is "A" or "B");
        Assert.Contains(warnings, w => w.Message.Contains("its allOf refers back to itself", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~SchemaModelBuilderTests"`
Expected: FAIL. `Pet` has no properties, because only its own `properties` are read; the conflict test throws nothing; the recursive test builds `A` and `B` as empty records.

- [ ] **Step 3: Guard recursive `allOf` in `Named`**

Insert at the top of `Named`, after the `_types` lookup:

```csharp
        if (HasRecursiveAllOf(schema, new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance)))
        {
            Unsupported(path, "its allOf refers back to itself");
            _types.Add(schema, TypeRef.JsonElement);
            return TypeRef.JsonElement;
        }
```

and add:

```csharp
    // Only allOf edges count: a record whose property refers back to it is fine.
    private static bool HasRecursiveAllOf(OpenApiSchema schema, HashSet<OpenApiSchema> visiting)
    {
        if (!visiting.Add(schema))
            return true;
        foreach (var part in schema.AllOf)
        {
            if (HasRecursiveAllOf(part, visiting))
                return true;
        }
        visiting.Remove(schema);
        return false;
    }
```

- [ ] **Step 4: Flatten `allOf`**

Replace `BuildRecord` with:

```csharp
    private RecordModel BuildRecord(OpenApiSchema schema, string name, string path)
    {
        var collected = new List<CollectedProperty>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema, path, name, collected, required, skip: null);
        return new RecordModel(name, schema.Description, Properties(name, collected, required));
    }

    private readonly record struct CollectedProperty(string WireName, OpenApiSchema Schema, string Path);

    // Spec §5.4: the properties of a schema and of every allOf part, parts first, in declaration
    // order. A property is required if any part requires it; the same property with two shapes is a
    // generation error. skip is an allOf part whose properties the record inherits instead.
    private static void Collect(
        OpenApiSchema schema, string path, string recordName,
        List<CollectedProperty> collected, HashSet<string> required, OpenApiSchema? skip)
    {
        for (var i = 0; i < schema.AllOf.Count; i++)
        {
            var part = schema.AllOf[i];
            if (!ReferenceEquals(part, skip))
                Collect(part, path + "/allOf/" + i.ToString(CultureInfo.InvariantCulture), recordName, collected, required, skip);
        }
        foreach (var (wireName, propertySchema) in schema.Properties)
        {
            var existing = collected.FindIndex(p => string.Equals(p.WireName, wireName, StringComparison.Ordinal));
            if (existing < 0)
            {
                collected.Add(new CollectedProperty(wireName, propertySchema, path + "/properties/" + wireName));
                continue;
            }
            var before = Shape(collected[existing].Schema);
            var after = Shape(propertySchema);
            if (!string.Equals(before, after, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Schema '{recordName}': property '{wireName}' has conflicting types in its allOf parts, '{before}' and '{after}'.");
        }
        required.UnionWith(schema.Required);
    }

    // What makes two declarations of a property the same type, without generating anything.
    private static string Shape(OpenApiSchema schema)
    {
        if (schema.Reference?.Id is { } id)
            return id;
        var shape = schema.Type ?? "any";
        if (schema.Format is not null)
            shape += ":" + schema.Format;
        if (schema.Items is not null)
            shape += "[" + Shape(schema.Items) + "]";
        return shape;
    }

    private EquatableList<PropertyModel> Properties(string recordName, List<CollectedProperty> collected, HashSet<string> required)
    {
        var used = new HashSet<string>(RecordMembers, StringComparer.Ordinal) { recordName };
        var properties = new List<PropertyModel>(collected.Count);
        foreach (var (wireName, propertySchema, propertyPath) in collected)
        {
            var identifier = CSharpNames.Unique(CSharpNames.Pascal(wireName, "Property"), used);
            var type = TypeMapper.Map(propertySchema, recordName + identifier, propertyPath, this);
            properties.Add(new PropertyModel(identifier, wireName, type, required.Contains(wireName), propertySchema.Nullable, propertySchema.Description));
        }
        return new EquatableList<PropertyModel>(properties);
    }
```

- [ ] **Step 5: Run the tests, then the full suite**

Run the Step 2 command. Expected: PASS, the Task 7 tests included. Then run the full-suite command.

- [ ] **Step 6: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: flatten allOf schemas into one record

The parts of an allOf merge into one record, parts first. A property is required if
any part requires it, and one declared twice with different types fails generation with
a message naming the schema and the property. An allOf that refers back to itself maps
to JsonElement with ZRT002.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Discriminated `oneOf`/`anyOf` as a polymorphic hierarchy

**Files:**
- Create: `src/ZeroAlloc.Rest.Tools.Shared/Models/PolymorphicModel.cs`, `Models/DerivedTypeModel.cs`
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/SchemaModelBuilder.cs`: constructor, `BuildModel`, `BuildRecord`; add `ClaimVariants`, `IsPolymorphic`, `Variants`, `BuildPolymorphic`
- Modify: `tests/ZeroAlloc.Rest.Tools.Tests/ModelFixture.cs`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/SchemaModelBuilderTests.cs`

**Interfaces:**
- Consumes: `Collect`, `Properties`, `CollectedProperty` from Task 8.
- Produces:
  - `internal sealed record PolymorphicModel(string Name, string? Description, string DiscriminatorWireName, EquatableList<PropertyModel> Properties, EquatableList<DerivedTypeModel> Variants) : ModelDefinition(Name, Description)`.
  - `internal sealed record DerivedTypeModel(string TypeName, string DiscriminatorValue)`.
  - The constructor becomes `SchemaModelBuilder(OpenApiDocument document, IEnumerable<string> reservedNames, List<OpenApiWarning> warnings)`. Task 14 calls it with this signature.

- [ ] **Step 1: Write the failing tests**

In `ModelFixture.Build`, change the builder line to `var builder = new SchemaModelBuilder(document, ReservedNames, warnings);`. Append to `SchemaModelBuilderTests`:

```csharp
    private const string Pets = """
                Pet:
                  type: object
                  required: [petType]
                  properties:
                    petType:
                      type: string
                    name:
                      type: string
                  discriminator:
                    propertyName: petType
                    mapping:
                      cat: '#/components/schemas/Cat'
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        lives:
                          type: integer
                Dog:
                  type: object
                  properties:
                    petType:
                      type: string
                    bark:
                      type: boolean
            """;

    [Fact]
    public void Discriminator_MakesAnAbstractBase_WithOneDerivedTypePerVariant()
    {
        var (models, _) = Build(Pets);

        Assert.Equal(
            new PolymorphicModel("Pet", null, "petType",
                List(Optional("Name", "name", TypeRef.String)),
                List(new DerivedTypeModel("Cat", "cat"), new DerivedTypeModel("Dog", "Dog"))),
            models[0]);
    }

    [Fact]
    public void Variants_DeriveFromTheBase_WithoutTheDiscriminatorOrInheritedProperties()
    {
        var (models, _) = Build(Pets);

        Assert.Equal(new RecordModel("Cat", null, List(Optional("Lives", "lives", TypeRef.Int)), BaseName: "Pet"), models[1]);
        Assert.Equal(new RecordModel("Dog", null, List(Optional("Bark", "bark", TypeRef.Bool)), BaseName: "Pet"), models[2]);
    }

    [Fact]
    public void Variant_ReferencedBeforeItsBase_StillDerivesFromIt()
    {
        var document = ModelFixture.Parse(Pets);
        var builder = new SchemaModelBuilder(document, ModelFixture.ReservedNames, []);

        TypeMapper.Map(document.Components.Schemas["Dog"], "Dog", "#/components/schemas/Dog", builder);
        var models = builder.Build();

        Assert.Equal("Pet", Assert.IsType<RecordModel>(models[0]).BaseName);
        Assert.Contains(models, m => m is PolymorphicModel { Name: "Pet" });
    }

    [Fact]
    public void VariantOfTwoBases_IsAGenerationError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build("""
                A:
                  discriminator:
                    propertyName: kind
                  oneOf:
                    - $ref: '#/components/schemas/V'
                B:
                  discriminator:
                    propertyName: kind
                  oneOf:
                    - $ref: '#/components/schemas/V'
                V:
                  type: object
                  properties:
                    x:
                      type: string
            """));

        Assert.Equal("Schema 'V' is a variant of both 'A' and 'B'; a C# record has one base type.", error.Message);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: FAIL with CS0246 for `PolymorphicModel`, `DerivedTypeModel`, and CS1729 for the three-argument constructor.

- [ ] **Step 3: Add the model types**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/PolymorphicModel.cs
namespace ZeroAlloc.Rest.Tools;

// Spec §5.5: an abstract record with [JsonPolymorphic] and one [JsonDerivedType] per variant.
internal sealed record PolymorphicModel(
    string Name,
    string? Description,
    string DiscriminatorWireName,
    EquatableList<PropertyModel> Properties,
    EquatableList<DerivedTypeModel> Variants)
    : ModelDefinition(Name, Description);
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/DerivedTypeModel.cs
namespace ZeroAlloc.Rest.Tools;

internal sealed record DerivedTypeModel(string TypeName, string DiscriminatorValue);
```

- [ ] **Step 4: Claim variants up front and build the hierarchy**

Add the field and the new constructor, replacing the Task 7 one:

```csharp
    // Each variant of a discriminated oneOf or anyOf, with its base.
    private readonly Dictionary<OpenApiSchema, OpenApiSchema> _baseOf = new(ReferenceEqualityComparer.Instance);

    // reservedNames are the names the generated file already uses: the interface, the client the
    // source generator derives from it, and the JSON context.
    internal SchemaModelBuilder(OpenApiDocument document, IEnumerable<string> reservedNames, List<OpenApiWarning> warnings)
    {
        _warnings = warnings;
        _typeNames = new HashSet<string>(reservedNames, StringComparer.Ordinal);
        ClaimVariants(document);
    }

    // A C# record has one base type. Claiming every variant before anything is built means a
    // variant the interface reaches before its base still derives from it.
    private void ClaimVariants(OpenApiDocument document)
    {
        if (document.Components?.Schemas is null)
            return;
        foreach (var (id, schema) in document.Components.Schemas)
        {
            if (!IsPolymorphic(schema))
                continue;
            foreach (var variant in Variants(schema))
            {
                if (variant.Reference?.Id is not { } variantId)
                    throw new InvalidOperationException($"Schema '{id}': each variant of a oneOf or anyOf with a discriminator must be a $ref.");
                if (_baseOf.TryGetValue(variant, out var other) && !ReferenceEquals(other, schema))
                    throw new InvalidOperationException(
                        $"Schema '{variantId}' is a variant of both '{other.Reference?.Id}' and '{id}'; a C# record has one base type.");
                _baseOf[variant] = schema;
            }
        }
    }

    private static bool IsPolymorphic(OpenApiSchema schema)
        => schema.Discriminator is not null && (schema.OneOf.Count > 0 || schema.AnyOf.Count > 0);

    private static IList<OpenApiSchema> Variants(OpenApiSchema schema) => schema.OneOf.Count > 0 ? schema.OneOf : schema.AnyOf;
```

Replace `BuildModel` and `BuildRecord`, and add `BuildPolymorphic`:

```csharp
    private ModelDefinition BuildModel(OpenApiSchema schema, string name, string path)
    {
        if (TypeMapper.IsEnum(schema))
            return BuildEnum(schema, name);
        if (IsPolymorphic(schema))
            return BuildPolymorphic(schema, name, path);
        return BuildRecord(schema, name, path);
    }

    // A variant derives from its base, and inherits the base's properties and the discriminator,
    // which STJ writes itself and rejects as a declared property.
    private RecordModel BuildRecord(OpenApiSchema schema, string name, string path)
    {
        string? baseName = null;
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        _baseOf.TryGetValue(schema, out var baseSchema);
        if (baseSchema is not null)
        {
            baseName = Named(baseSchema, name + "Base", path).Name;
            inherited.Add(baseSchema.Discriminator.PropertyName);
            inherited.UnionWith(baseSchema.Properties.Keys);
        }
        var collected = new List<CollectedProperty>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema, path, name, collected, required, skip: baseSchema);
        collected.RemoveAll(p => inherited.Contains(p.WireName));
        return new RecordModel(name, schema.Description, Properties(name, collected, required), baseName);
    }

    // Spec §5.5: values come from discriminator.mapping, and otherwise from the schema name. A
    // mapping target is a $ref or a bare schema name; either way its last segment is the name.
    private PolymorphicModel BuildPolymorphic(OpenApiSchema schema, string name, string path)
    {
        var discriminator = schema.Discriminator.PropertyName;
        var valueById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (value, target) in schema.Discriminator.Mapping)
            valueById.TryAdd(target[(target.LastIndexOf('/') + 1)..], value);

        var variants = new List<DerivedTypeModel>();
        foreach (var variant in Variants(schema))
        {
            var id = variant.Reference?.Id
                ?? throw new InvalidOperationException($"Schema '{name}': each variant of a oneOf or anyOf with a discriminator must be a $ref.");
            _baseOf.TryAdd(variant, schema);
            var type = Named(variant, id, path);
            variants.Add(new DerivedTypeModel(type.Name, valueById.GetValueOrDefault(id, id)));
        }

        var collected = new List<CollectedProperty>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema, path, name, collected, required, skip: null);
        collected.RemoveAll(p => string.Equals(p.WireName, discriminator, StringComparison.Ordinal));
        return new PolymorphicModel(name, schema.Description, discriminator, Properties(name, collected, required), new EquatableList<DerivedTypeModel>(variants));
    }
```

- [ ] **Step 5: Run the tests, then the full suite**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~SchemaModelBuilderTests"`
Expected: PASS. Then run the full-suite command.

- [ ] **Step 6: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: model discriminated oneOf and anyOf as a record hierarchy

A oneOf or anyOf with a discriminator becomes an abstract base with one derived record
per variant. Values come from the discriminator mapping, else the schema name. Variants
drop the discriminator and inherited properties, derive from their base however they
are reached first, and a variant of two bases fails generation.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Undiscriminated `oneOf`/`anyOf` as a union model

**Files:**
- Create: `src/ZeroAlloc.Rest.Tools.Shared/Models/UnionModel.cs`, `Models/UnionVariantModel.cs`, `Models/JsonKind.cs`
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/SchemaModelBuilder.cs`: `Named`, `BuildModel`; add `UnionName`, `BuildUnion`, `VariantName`, `ReadableName`, `KindOf`, `ModelKind`, `RequiredOf`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/SchemaModelBuilderTests.cs`

**Interfaces:**
- Consumes: Task 9's builder.
- Produces:
  - `internal enum JsonKind { Object, Array, String, Number, Boolean, Any }`.
  - `internal sealed record UnionModel(string Name, string? Description, bool IsOneOf, EquatableList<UnionVariantModel> Variants) : ModelDefinition(Name, Description)`.
  - `internal sealed record UnionVariantModel(string Name, TypeRef Type, JsonKind Kind, EquatableList<string> RequiredWireNames)`. `Name` is the suffix of the `As{Name}` property; `RequiredWireNames` is sorted ordinally and empty unless `Kind` is `Object`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void OneOfWithoutDiscriminator_IsAUnion_WithKindsAndRequiredProperties()
    {
        var (models, _) = Build("""
                Pet:
                  type: object
                  required: [name, id]
                  properties:
                    id:
                      type: integer
                    name:
                      type: string
                Error:
                  type: object
                  required: [code]
                  properties:
                    code:
                      type: string
                Result:
                  oneOf:
                    - $ref: '#/components/schemas/Pet'
                    - $ref: '#/components/schemas/Error'
                    - type: integer
                      format: int64
                    - type: array
                      items:
                        type: string
            """);

        Assert.Equal(
            new UnionModel("Result", null, IsOneOf: true, List(
                new UnionVariantModel("Pet", Model("Pet"), JsonKind.Object, List("id", "name")),
                new UnionVariantModel("Error", Model("Error"), JsonKind.Object, List("code")),
                new UnionVariantModel("Int64", TypeRef.Long, JsonKind.Number, EquatableList<string>.Empty),
                new UnionVariantModel("StringList", new TypeRef("global::System.Collections.Generic.List<string>", TypeRefKind.List, false), JsonKind.Array, EquatableList<string>.Empty))),
            models[2]);
    }

    [Fact]
    public void InlineUnionOfRefs_IsNamedAfterItsVariants()
    {
        var (models, _) = Build("""
                Pet:
                  type: object
                  properties:
                    id:
                      type: integer
                Error:
                  type: object
                  properties:
                    code:
                      type: string
                Holder:
                  type: object
                  properties:
                    outcome:
                      anyOf:
                        - $ref: '#/components/schemas/Pet'
                        - $ref: '#/components/schemas/Error'
            """);

        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal("PetOrError", union.Name);
        Assert.False(union.IsOneOf);
    }

    [Fact]
    public void UnionVariant_RequiredPropertiesIncludeThoseOfItsAllOfParts()
    {
        var (models, _) = Build("""
                Base:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: integer
                Derived:
                  allOf:
                    - $ref: '#/components/schemas/Base'
                    - type: object
                      required: [extra]
                      properties:
                        extra:
                          type: string
                Either:
                  oneOf:
                    - $ref: '#/components/schemas/Derived'
                    - type: string
            """);

        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal(List("extra", "id"), union.Variants[0].RequiredWireNames);
        Assert.Equal(new UnionVariantModel("String", TypeRef.String, JsonKind.String, EquatableList<string>.Empty), union.Variants[1]);
    }

    [Fact]
    public void EnumVariant_TakesItsJsonKindFromItsType()
    {
        var (models, _) = Build("""
                Code:
                  type: integer
                  enum: [1, 2]
                Either:
                  oneOf:
                    - $ref: '#/components/schemas/Code'
                    - type: boolean
            """);

        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal(new[] { JsonKind.Number, JsonKind.Boolean }, union.Variants.Select(v => v.Kind));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: FAIL with CS0246 for `UnionModel`, `UnionVariantModel`, `JsonKind`.

- [ ] **Step 3: Add the model types**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/JsonKind.cs
namespace ZeroAlloc.Rest.Tools;

// The JSON value kind a union variant accepts. Any is a variant the tool could not type, such as a
// nested union or a JsonElement, which accepts every kind.
internal enum JsonKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Any,
}
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/UnionModel.cs
namespace ZeroAlloc.Rest.Tools;

// Spec §5.6: a sealed record with one As{Variant} property per variant and a generated converter.
internal sealed record UnionModel(string Name, string? Description, bool IsOneOf, EquatableList<UnionVariantModel> Variants)
    : ModelDefinition(Name, Description);
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/Models/UnionVariantModel.cs
namespace ZeroAlloc.Rest.Tools;

// RequiredWireNames, sorted, decide whether a JSON object can be this variant; empty unless Kind
// is Object.
internal sealed record UnionVariantModel(string Name, TypeRef Type, JsonKind Kind, EquatableList<string> RequiredWireNames);
```

- [ ] **Step 4: Build unions**

In `Named`, change the name line to:

```csharp
        var name = Reserve(schema.Reference?.Id ?? UnionName(schema) ?? contextName);
```

In `BuildModel`, before the final `return BuildRecord(...)`:

```csharp
        if (schema.OneOf.Count > 0 || schema.AnyOf.Count > 0)
            return BuildUnion(schema, name, path);
```

Add:

```csharp
    // Design decision 8: an inline union whose variants are all $refs is named after them, PetOrError.
    private static string? UnionName(OpenApiSchema schema)
    {
        if (schema.Discriminator is not null)
            return null;
        var parts = schema.OneOf.Count > 0 ? schema.OneOf : schema.AnyOf;
        if (parts.Count < 2)
            return null;
        var names = new List<string>(parts.Count);
        foreach (var part in parts)
        {
            if (part.Reference?.Id is not { } id)
                return null;
            names.Add(CSharpNames.Pascal(id, "Variant"));
        }
        return string.Join("Or", names);
    }

    private UnionModel BuildUnion(OpenApiSchema schema, string name, string path)
    {
        var isOneOf = schema.OneOf.Count > 0;
        var parts = isOneOf ? schema.OneOf : schema.AnyOf;
        var keyword = isOneOf ? "/oneOf/" : "/anyOf/";
        var used = new HashSet<string>(StringComparer.Ordinal) { name };
        var variants = new List<UnionVariantModel>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var contextName = name + "Variant" + (i + 1).ToString(CultureInfo.InvariantCulture);
            var type = TypeMapper.Map(part, contextName, path + keyword + i.ToString(CultureInfo.InvariantCulture), this);
            var kind = KindOf(part, type);
            var required = kind == JsonKind.Object ? RequiredOf(part) : EquatableList<string>.Empty;
            variants.Add(new UnionVariantModel(CSharpNames.Unique(VariantName(part, type), used), type, kind, required));
        }
        return new UnionModel(name, schema.Description, isOneOf, new EquatableList<UnionVariantModel>(variants));
    }

    private static string VariantName(OpenApiSchema part, TypeRef type)
        => part.Reference?.Id is { } id ? CSharpNames.Pascal(id, "Variant") : ReadableName(type.Name);

    // A readable name for a type: its CLR name for a keyword, its last segment otherwise, and the
    // element's name first for a collection: List<string> is StringList.
    private static string ReadableName(string typeName)
    {
        const string List = "global::System.Collections.Generic.List<";
        const string Dictionary = "global::System.Collections.Generic.Dictionary<string, ";
        if (typeName.StartsWith(List, StringComparison.Ordinal))
            return ReadableName(typeName[List.Length..^1]) + "List";
        if (typeName.StartsWith(Dictionary, StringComparison.Ordinal))
            return ReadableName(typeName[Dictionary.Length..^1]) + "Map";
        var name = typeName.TrimEnd('?');
        return name switch
        {
            "string" => "String",
            "int" => "Int32",
            "long" => "Int64",
            "float" => "Single",
            "double" => "Double",
            "decimal" => "Decimal",
            "bool" => "Boolean",
            "byte[]" => "Bytes",
            _ => name[(name.LastIndexOf('.') + 1)..],
        };
    }

    private static JsonKind KindOf(OpenApiSchema part, TypeRef type) => type.Kind switch
    {
        TypeRefKind.List => JsonKind.Array,
        TypeRefKind.Dictionary => JsonKind.Object,
        TypeRefKind.JsonElement or TypeRefKind.Stream => JsonKind.Any,
        TypeRefKind.Model => ModelKind(TypeMapper.Unwrap(part)),
        _ => type.Name switch
        {
            "bool" => JsonKind.Boolean,
            "int" or "long" or "float" or "double" or "decimal" => JsonKind.Number,
            _ => JsonKind.String,
        },
    };

    private static JsonKind ModelKind(OpenApiSchema schema)
    {
        if (TypeMapper.IsEnum(schema))
            return string.Equals(schema.Type, "integer", StringComparison.Ordinal) ? JsonKind.Number : JsonKind.String;
        if (IsPolymorphic(schema) || (schema.OneOf.Count == 0 && schema.AnyOf.Count == 0))
            return JsonKind.Object;
        return JsonKind.Any;
    }

    // The required properties of an object variant, its allOf parts included, sorted so the model is
    // deterministic. A recursive allOf never gets here: Named mapped it to JsonElement, kind Any.
    private static EquatableList<string> RequiredOf(OpenApiSchema schema)
    {
        var required = new SortedSet<string>(StringComparer.Ordinal);
        AddRequired(TypeMapper.Unwrap(schema), required);
        return new EquatableList<string>(required);
    }

    private static void AddRequired(OpenApiSchema schema, SortedSet<string> required)
    {
        required.UnionWith(schema.Required);
        foreach (var part in schema.AllOf)
            AddRequired(part, required);
    }
```

- [ ] **Step 5: Run the tests, then the full suite**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~SchemaModelBuilderTests"`
Expected: PASS. Then run the full-suite command.

- [ ] **Step 6: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: model undiscriminated oneOf and anyOf as unions

A oneOf or anyOf without a discriminator becomes a union model: one variant per part,
with the JSON kind it accepts and, for an object, its required properties including
those of its allOf parts. An inline union of refs is named after them, PetOrError.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Compile harness, record and enum emission, and the JSON context

**Files:**
- Modify: `tests/ZeroAlloc.Rest.Tools.Tests/ZeroAlloc.Rest.Tools.Tests.csproj`
- Create: `tests/ZeroAlloc.Rest.Tools.Tests/GeneratedCode.cs`
- Modify: `tests/ZeroAlloc.Rest.Tools.Tests/OpenApiRouteBindingTests.cs`: `AssertCompilesClean` delegates to the harness
- Modify: `tests/ZeroAlloc.Rest.Tools.Tests/ModelFixture.cs`: add `Emit`
- Create: `src/ZeroAlloc.Rest.Tools.Shared/ModelEmitter.cs`, `JsonContextEmitter.cs`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/ModelEmitterTests.cs`

**Interfaces:**
- Consumes: `SchemaModelBuilder`, `RecordModel`, `EnumModel` and the rest of the model from Tasks 7 to 10.
- Produces:
  - `internal static class ModelEmitter` with `void Emit(StringBuilder sb, IEnumerable<ModelDefinition> models)` and `IEnumerable<string> SerializableTypes(IEnumerable<ModelDefinition> models)`.
  - `internal static class JsonContextEmitter` with `void Emit(StringBuilder sb, string contextName, IEnumerable<string> typeNames)`.
  - Test harness `GeneratedCode.Compile(string code, string? probe = null)` returning `GeneratedCode.Output` with `Problems`, `AssertClean()` and `string RunProbe()`.
  - `ModelFixture.Emit(string schemasYaml)` returning the source text of namespace `MyApp` with the models and `MyApiJsonContext`.

- [ ] **Step 1: Give the tests the STJ source generator and the runtime**

The STJ source generator ships in the targeting pack as an analyzer, so the SDK already hands it to every net10.0 build. The test project records its path in an assembly attribute. Add to `ZeroAlloc.Rest.Tools.Tests.csproj`:

```xml
  <ItemGroup>
    <!-- Probes built from generated clients serialize with it -->
    <ProjectReference Include="../../src/ZeroAlloc.Rest.SystemTextJson/ZeroAlloc.Rest.SystemTextJson.csproj" />
  </ItemGroup>

  <!--
    GeneratedCode compiles emitted models the way a consumer's build does, with the System.Text.Json
    source generator. The SDK passes that generator to this project as an Analyzer item from the
    targeting pack; its path is recorded here so the test uses the same one.
  -->
  <Target Name="RecordJsonSourceGeneratorPath" BeforeTargets="GetAssemblyAttributes" DependsOnTargets="ResolveTargetingPackAssets">
    <ItemGroup>
      <_JsonSourceGenerator Include="@(Analyzer)" Condition="'%(Filename)' == 'System.Text.Json.SourceGeneration'" />
      <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
        <_Parameter1>JsonSourceGenerator</_Parameter1>
        <_Parameter2>@(_JsonSourceGenerator->'%(FullPath)')</_Parameter2>
      </AssemblyAttribute>
    </ItemGroup>
  </Target>
```

- [ ] **Step 2: Write the harness**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/GeneratedCode.cs
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ZeroAlloc.Rest.Generator;

namespace ZeroAlloc.Rest.Tools.Tests;

// Compiles emitted code the way a consumer's build does: with the ZeroAlloc.Rest source generator,
// the System.Text.Json source generator and RouteTemplateAnalyzer, against real references.
// RouteTemplateAnalyzer skips generated code, so the <auto-generated/> marker is dropped and the file
// is compiled as ordinary source. A warning is a problem: consumers build with TreatWarningsAsErrors.
internal static class GeneratedCode
{
    private static readonly MetadataReference[] References =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Collections.HeapPooledList<>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<ISourceGenerator> JsonSourceGenerator = new(LoadJsonSourceGenerator);

    // probe, when given, is compiled alongside: a public static class Probe with a public static
    // string Run(), which RunProbe calls.
    internal static Output Compile(string code, string? probe = null)
    {
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(code.Replace("// <auto-generated/>", "", StringComparison.Ordinal), ParseOptions, path: "Generated.cs"),
        };
        if (probe is not null)
            trees.Add(CSharpSyntaxTree.ParseText(probe, ParseOptions, path: "Probe.cs"));
        var compilation = CSharpCompilation.Create(
            "OpenApiClient",
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create([new RestClientGenerator().AsSourceGenerator(), JsonSourceGenerator.Value], parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var analyzerDiagnostics = output
            .WithAnalyzers([new RouteTemplateAnalyzer()])
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        var problems = generatorDiagnostics
            .AddRange(analyzerDiagnostics)
            .AddRange(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        return new Output(code, output, problems);
    }

    private static ISourceGenerator LoadJsonSourceGenerator()
    {
        var path = typeof(GeneratedCode).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => string.Equals(a.Key, "JsonSourceGenerator", StringComparison.Ordinal)).Value;
        Assert.True(File.Exists(path), $"The System.Text.Json source generator was not found at '{path}'.");
        var reference = new AnalyzerFileReference(path, new AnalyzerLoader());
        return Assert.Single(reference.GetGenerators(LanguageNames.CSharp));
    }

    internal sealed class Output(string code, Compilation compilation, ImmutableArray<Diagnostic> problems)
    {
        internal ImmutableArray<Diagnostic> Problems => problems;

        internal void AssertClean()
            => Assert.True(problems.IsEmpty, code + "\n" + string.Join("\n", problems));

        // Emits the assembly into a collectible load context and returns what Probe.Run returns.
        internal string RunProbe()
        {
            AssertClean();
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            stream.Position = 0;
            var context = new AssemblyLoadContext("probe", isCollectible: true);
            try
            {
                var run = context.LoadFromStream(stream).GetType("Probe")?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("The probe has no public static Probe.Run().");
                return run.Invoke(null, null) as string
                    ?? throw new InvalidOperationException("Probe.Run() returned no string.");
            }
            finally
            {
                context.Unload();
            }
        }
    }

    private sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
```

In `OpenApiRouteBindingTests`, delete the `References` field and replace the body of `AssertCompilesClean` with `GeneratedCode.Compile(code).AssertClean();`. Remove usings it no longer needs.

- [ ] **Step 3: Add `ModelFixture.Emit`**

```csharp
    internal const string ContextName = "MyApiJsonContext";

    // The file the generator writes for these components, minus the interface: models, then context.
    internal static string Emit(string schemasYaml)
    {
        var (models, warnings) = Build(schemasYaml);
        Assert.Empty(warnings);
        var sb = new System.Text.StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine()
            .AppendLine("namespace MyApp;")
            .AppendLine();
        ModelEmitter.Emit(sb, models);
        JsonContextEmitter.Emit(sb, ContextName, ModelEmitter.SerializableTypes(models));
        return sb.ToString();
    }
```

- [ ] **Step 4: Write the failing emitter tests**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/ModelEmitterTests.cs
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §10.2 and §10.3: emitted models compile with no diagnostics and round-trip through the
// generated context.
public class ModelEmitterTests
{
    private const string PetSpec = """
                Pet:
                  type: object
                  description: A pet & its <owner>.
                  required: [id, owner]
                  properties:
                    id:
                      type: integer
                      format: int64
                    name:
                      type: string
                    owner:
                      type: string
                      nullable: true
                    status:
                      type: string
                      enum: [available, sold out]
                    priority:
                      type: integer
                      enum: [1, 2]
                    born:
                      type: string
                      format: date-time
            """;

    [Fact]
    public void Record_IsASealedRecord_WithWireNamesRequiredAndNullableMembers()
    {
        var code = ModelFixture.Emit(PetSpec);

        Assert.Contains("/// A pet &amp; its &lt;owner&gt;.", code);
        Assert.Contains("public sealed record Pet", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonPropertyName(\"id\")]\n    public required long Id { get; init; }", Normalize(code));
        Assert.Contains("public required string? Owner { get; init; }", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]\n    public string? Name { get; init; }", Normalize(code));
        Assert.Contains("public PetStatus? Status { get; init; }", code);
    }

    [Fact]
    public void Enums_HaveStrictConverters()
    {
        var code = ModelFixture.Emit(PetSpec);

        Assert.Contains("[global::System.Text.Json.Serialization.JsonConverter(typeof(PetStatusConverter))]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonStringEnumMemberName(\"sold out\")]\n    SoldOut,", Normalize(code));
        Assert.Contains("internal sealed class PetStatusConverter : global::System.Text.Json.Serialization.JsonStringEnumConverter<PetStatus>", code);
        Assert.Contains(": base(namingPolicy: null, allowIntegerValues: false)", code);
        Assert.Contains("Value1 = 1,", code);
        Assert.Contains("internal sealed class PetPriorityConverter : global::System.Text.Json.Serialization.JsonConverter<PetPriority>", code);
    }

    [Fact]
    public void Context_RegistersEveryModel()
    {
        var code = ModelFixture.Emit(PetSpec);

        Assert.Contains("[global::System.Text.Json.Serialization.JsonSourceGenerationOptions(global::System.Text.Json.JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true)]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(Pet))]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(PetStatus))]", code);
        Assert.Contains("public partial class MyApiJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext", code);
    }

    [Fact]
    public void EmittedModels_CompileWithNoDiagnostics()
        => GeneratedCode.Compile(ModelFixture.Emit(PetSpec)).AssertClean();

    [Fact]
    public void Record_RoundTrips_OmittingAbsentOptionalsAndKeepingRequiredNulls()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetSpec), Probe("""
            var pet = JsonSerializer.Deserialize("{\"id\":1,\"owner\":null,\"status\":\"sold out\",\"priority\":2,\"born\":\"2026-09-27T10:30:00+02:00\"}", MyApiJsonContext.Default.Pet)!;
            return pet.Id + "|" + (pet.Owner ?? "none") + "|" + pet.Status + "|" + pet.Priority + "|" + JsonSerializer.Serialize(pet, MyApiJsonContext.Default.Pet);
            """));

        Assert.Equal(
            "1|none|SoldOut|Value2|{\"id\":1,\"owner\":null,\"status\":\"sold out\",\"priority\":2,\"born\":\"2026-09-27T10:30:00+02:00\"}",
            output.RunProbe());
    }

    [Theory]
    [InlineData("{\"id\":1,\"owner\":null}", "ok")]
    [InlineData("{\"owner\":null}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":\"lost\"}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":0}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"priority\":3}", "JsonException")]
    public void Deserialization_IsStrict(string json, string expected)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetSpec), Probe($$"""
            try
            {
                JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Pet);
                return "ok";
            }
            catch (JsonException)
            {
                return "JsonException";
            }
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    // A probe whose Run() body is the given statements, in namespace MyApp.
    internal static string Probe(string body) => $$"""
        using System;
        using System.Text.Json;
        using MyApp;

        public static class Probe
        {
            public static string Run()
            {
        {{body}}
            }
        }
        """;

    internal static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Normalize(string code) => code.Replace("\r\n", "\n", StringComparison.Ordinal);
}
```

The probe body uses `!` once, on the probe's own `Deserialize` result. It is test input compiled in memory, not repository code; if the executor prefers, write `?? throw new InvalidOperationException()` instead.

- [ ] **Step 5: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: FAIL with CS0103 for `ModelEmitter` and `JsonContextEmitter`.

- [ ] **Step 6: Write the emitters**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/ModelEmitter.cs
using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Writes the models, spec §5.2 to §5.6, as pure functions of the intermediate model. Every framework
// type is global::-qualified, because a model named from the spec may shadow it.
internal static class ModelEmitter
{
    internal const string Serialization = "global::System.Text.Json.Serialization";
    internal const string Json = "global::System.Text.Json";

    internal static void Emit(StringBuilder sb, IEnumerable<ModelDefinition> models)
    {
        foreach (var model in models)
        {
            switch (model)
            {
                case RecordModel record:
                    EmitRecord(sb, record);
                    break;
                case EnumModel enumModel:
                    EmitEnum(sb, enumModel);
                    break;
                default:
                    throw new InvalidOperationException($"No emitter for {model.GetType().Name} '{model.Name}'.");
            }
            sb.AppendLine();
        }
    }

    // Every model, plus every union variant: a type behind a custom converter is not reachable by
    // the STJ source generator on its own, and the union converter resolves variants by type.
    internal static IEnumerable<string> SerializableTypes(IEnumerable<ModelDefinition> models)
    {
        foreach (var model in models)
            yield return model.Name;
    }

    private static void EmitRecord(StringBuilder sb, RecordModel record)
    {
        CSharpNames.AppendDocComment(sb, "", record.Description);
        sb.Append("public sealed record ").Append(record.Name);
        if (record.BaseName is not null)
            sb.Append(" : ").Append(record.BaseName);
        sb.AppendLine().AppendLine("{");
        EmitProperties(sb, record.Properties);
        sb.AppendLine("}");
    }

    internal static void EmitProperties(StringBuilder sb, EquatableList<PropertyModel> properties)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (i > 0)
                sb.AppendLine();
            CSharpNames.AppendDocComment(sb, "    ", property.Description);
            sb.Append("    [").Append(Serialization).Append(".JsonPropertyName(").Append(CSharpNames.Literal(property.WireName)).AppendLine(")]");
            // An optional property that is null was absent: it stays absent on the wire. A required
            // one is always written, null included.
            if (!property.Required)
                sb.Append("    [").Append(Serialization).Append(".JsonIgnore(Condition = ").Append(Serialization).AppendLine(".JsonIgnoreCondition.WhenWritingNull)]");
            sb.Append("    public ");
            if (property.Required)
                sb.Append("required ");
            sb.Append(TypeMapper.Declare(property.Type, property.Required, property.Nullable))
                .Append(' ').Append(property.Name).AppendLine(" { get; init; }");
        }
    }

    // Spec §5.3 and design decision 11: strict enums. A string enum reads its wire names only; an
    // integer enum reads its declared values only. Anything else throws JsonException.
    private static void EmitEnum(StringBuilder sb, EnumModel model)
    {
        var converter = model.Name + "Converter";
        CSharpNames.AppendDocComment(sb, "", model.Description);
        sb.Append('[').Append(Serialization).Append(".JsonConverter(typeof(").Append(converter).AppendLine("))]");
        sb.Append("public enum ").Append(model.Name);
        if (!model.IsString && string.Equals(model.UnderlyingType, "long", StringComparison.Ordinal))
            sb.Append(" : long");
        sb.AppendLine().AppendLine("{");
        foreach (var member in model.Members)
        {
            if (model.IsString)
            {
                sb.Append("    [").Append(Serialization).Append(".JsonStringEnumMemberName(").Append(CSharpNames.Literal(member.WireValue)).AppendLine(")]");
                sb.Append("    ").Append(member.Name).AppendLine(",");
            }
            else
            {
                sb.Append("    ").Append(member.Name).Append(" = ").Append(member.WireValue).AppendLine(",");
            }
        }
        sb.AppendLine("}").AppendLine();
        if (model.IsString)
            EmitStringEnumConverter(sb, model.Name, converter);
        else
            EmitIntegerEnumConverter(sb, model, converter);
    }

    private static void EmitStringEnumConverter(StringBuilder sb, string enumName, string converter)
    {
        sb.Append("internal sealed class ").Append(converter).Append(" : ").Append(Serialization)
            .Append(".JsonStringEnumConverter<").Append(enumName).AppendLine(">");
        sb.AppendLine("{");
        sb.Append("    public ").Append(converter).AppendLine("()");
        sb.AppendLine("        : base(namingPolicy: null, allowIntegerValues: false)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static void EmitIntegerEnumConverter(StringBuilder sb, EnumModel model, string converter)
    {
        sb.Append("internal sealed class ").Append(converter).Append(" : ").Append(Serialization)
            .Append(".JsonConverter<").Append(model.Name).AppendLine(">");
        sb.AppendLine("{");
        sb.Append("    public override ").Append(model.Name).Append(" Read(ref ").Append(Json)
            .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.Append("        if (reader.TokenType == ").Append(Json).AppendLine(".JsonTokenType.Number && reader.TryGetInt64(out var value))");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (value)");
        sb.AppendLine("            {");
        foreach (var member in model.Members)
            sb.Append("                case ").Append(member.WireValue).Append(": return ").Append(model.Name).Append('.').Append(member.Name).AppendLine(";");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.Append("        throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"The JSON value is not a defined {model.Name} value.")).AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    public override void Write(").Append(Json).Append(".Utf8JsonWriter writer, ").Append(model.Name)
            .Append(" value, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("        => writer.WriteNumberValue((long)value);");
        sb.AppendLine("}");
    }
}
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/JsonContextEmitter.cs
using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Writes the JsonSerializerContext covering every generated type and every request and response
// type, spec §4. AllowOutOfOrderMetadataProperties lets a discriminator appear anywhere in an object.
internal static class JsonContextEmitter
{
    internal static void Emit(StringBuilder sb, string contextName, IEnumerable<string> typeNames)
    {
        sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSourceGenerationOptions(").Append(ModelEmitter.Json)
            .AppendLine(".JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true)]");
        foreach (var typeName in typeNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSerializable(typeof(").Append(typeName).AppendLine("))]");
        sb.Append("public partial class ").Append(contextName).Append(" : ").Append(ModelEmitter.Serialization).AppendLine(".JsonSerializerContext");
        sb.AppendLine("{");
        sb.AppendLine("}");
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: PASS, the route binding tests included, now through the harness. If the STJ generator reports a diagnostic, such as SYSLIB1037 or SYSLIB1031, fix what the emitter writes; do not filter the diagnostic.

- [ ] **Step 8: Run the full suite, then commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: emit records, strict enums and a JSON context from the model

ModelEmitter writes sealed records with required and init members and wire names, and
enums whose generated converters reject unknown names and values. JsonContextEmitter
writes a source-generated JsonSerializerContext covering them. Tests now compile the
output with the System.Text.Json generator and run round trips through it.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: Polymorphic hierarchy emission

**Files:**
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/ModelEmitter.cs`: `Emit`; add `EmitPolymorphic`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/ModelEmitterTests.cs`

**Interfaces:**
- Consumes: `PolymorphicModel` from Task 9, `ModelEmitter` from Task 11.
- Produces: `public abstract record {Base}` with `[JsonPolymorphic]` and `[JsonDerivedType]`; variants are the Task 11 records with `: {Base}`.

- [ ] **Step 1: Write the failing tests**

Append to `ModelEmitterTests`:

```csharp
    private const string PetsSpec = """
                Pet:
                  type: object
                  required: [petType, name]
                  properties:
                    petType:
                      type: string
                    name:
                      type: string
                  discriminator:
                    propertyName: petType
                    mapping:
                      cat: '#/components/schemas/Cat'
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        lives:
                          type: integer
                Dog:
                  type: object
                  properties:
                    bark:
                      type: boolean
            """;

    [Fact]
    public void Polymorphic_IsAnAbstractBase_WithDerivedTypes()
    {
        var code = ModelFixture.Emit(PetsSpec);

        Assert.Contains("[global::System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = \"petType\")]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonDerivedType(typeof(Cat), \"cat\")]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonDerivedType(typeof(Dog), \"Dog\")]", code);
        Assert.Contains("public abstract record Pet", code);
        Assert.Contains("public sealed record Cat : Pet", code);
        GeneratedCode.Compile(code).AssertClean();
    }

    [Theory]
    [InlineData("{\"petType\":\"cat\",\"name\":\"Tom\",\"lives\":9}", "Cat:Tom:9")]
    [InlineData("{\"name\":\"Rex\",\"bark\":true,\"petType\":\"Dog\"}", "Dog:Rex:True")]
    public void Polymorphic_RoundTrips_WithTheDiscriminatorAnywhere(string json, string expected)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetsSpec), Probe($$"""
            var pet = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Pet);
            var text = pet switch
            {
                Cat cat => "Cat:" + cat.Name + ":" + cat.Lives,
                Dog dog => "Dog:" + dog.Name + ":" + dog.Bark,
                _ => "none",
            };
            var again = JsonSerializer.Deserialize(JsonSerializer.Serialize(pet, MyApiJsonContext.Default.Pet), MyApiJsonContext.Default.Pet);
            return Equals(pet, again) ? text : "round trip changed it";
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    [Fact]
    public void Polymorphic_WithAnUnknownDiscriminator_Throws()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetsSpec), Probe("""
            try
            {
                JsonSerializer.Deserialize("{\"petType\":\"bird\",\"name\":\"Tweety\"}", MyApiJsonContext.Default.Pet);
                return "read";
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return "rejected";
            }
            """));

        Assert.Equal("rejected", output.RunProbe());
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~ModelEmitterTests"`
Expected: FAIL with `InvalidOperationException: No emitter for PolymorphicModel 'Pet'.`

- [ ] **Step 3: Emit the base**

Add to the `switch` in `ModelEmitter.Emit`:

```csharp
                case PolymorphicModel polymorphic:
                    EmitPolymorphic(sb, polymorphic);
                    break;
```

and add:

```csharp
    // Spec §5.5. The variants are ordinary records deriving from this base; STJ writes and reads the
    // discriminator, which is why neither the base nor a variant declares it.
    private static void EmitPolymorphic(StringBuilder sb, PolymorphicModel model)
    {
        CSharpNames.AppendDocComment(sb, "", model.Description);
        sb.Append('[').Append(Serialization).Append(".JsonPolymorphic(TypeDiscriminatorPropertyName = ")
            .Append(CSharpNames.Literal(model.DiscriminatorWireName)).AppendLine(")]");
        foreach (var variant in model.Variants)
        {
            sb.Append('[').Append(Serialization).Append(".JsonDerivedType(typeof(").Append(variant.TypeName).Append("), ")
                .Append(CSharpNames.Literal(variant.DiscriminatorValue)).AppendLine(")]");
        }
        sb.Append("public abstract record ").AppendLine(model.Name);
        sb.AppendLine("{");
        EmitProperties(sb, model.Properties);
        sb.AppendLine("}");
    }
```

- [ ] **Step 4: Run the tests, then the full suite**

Run the Step 2 command. Expected: PASS. Then run the full-suite command.

- [ ] **Step 5: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: emit discriminated hierarchies with STJ polymorphism

A discriminated oneOf or anyOf is written as an abstract record with JsonPolymorphic and
one JsonDerivedType per variant. Round trips pass with the discriminator anywhere in the
object, and an unknown discriminator value is rejected.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: Union wrappers and their converters

**Files:**
- Create: `src/ZeroAlloc.Rest.Tools.Shared/UnionEmitter.cs`
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/ModelEmitter.cs`: `Emit`, `SerializableTypes`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/UnionEmitterTests.cs`

**Interfaces:**
- Consumes: `UnionModel`, `UnionVariantModel`, `JsonKind` from Task 10; `ModelEmitter.Serialization`, `ModelEmitter.Json`, `TypeMapper.Declare`.
- Produces: `internal static class UnionEmitter` with `void Emit(StringBuilder sb, UnionModel model)`. The union is `public sealed record {Name}` with `As{Variant}` properties, `Match` and `Switch`; the converter is `internal sealed class {Name}Converter`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/UnionEmitterTests.cs
using Xunit;
using static ZeroAlloc.Rest.Tools.Tests.ModelEmitterTests;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §5.6: the converter filters by JSON kind, then by required properties, prefers the variant
// matching the most required properties, and applies the oneOf or anyOf rule.
public class UnionEmitterTests
{
    private static string Spec(string keyword) => $"""
                Pet:
                  type: object
                  required: [id, name]
                  properties:
                    id:
                      type: integer
                    name:
                      type: string
                Named:
                  type: object
                  required: [name]
                  properties:
                    name:
                      type: string
                Result:
                  {keyword}:
                    - $ref: '#/components/schemas/Named'
                    - $ref: '#/components/schemas/Pet'
                    - type: integer
                      format: int64
                    - type: string
                    - type: boolean
                    - type: array
                      items:
                        type: integer
            """;

    private const string Describe = """
            var text = result.Match(
                named => "Named:" + named.Name,
                pet => "Pet:" + pet.Id,
                int64 => "Int64:" + int64,
                @string => "String:" + @string,
                boolean => "Boolean:" + boolean,
                int32List => "Int32List:" + int32List.Count);
        """;

    [Fact]
    public void Union_IsASealedRecord_WithAConverter()
    {
        var code = ModelFixture.Emit(Spec("oneOf"));

        Assert.Contains("[global::System.Text.Json.Serialization.JsonConverter(typeof(ResultConverter))]", code);
        Assert.Contains("public sealed record Result", code);
        Assert.Contains("public Pet? AsPet { get; init; }", code);
        Assert.Contains("public long? AsInt64 { get; init; }", code);
        Assert.Contains("internal sealed class ResultConverter : global::System.Text.Json.Serialization.JsonConverter<Result>", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(long))]", code);
        GeneratedCode.Compile(code).AssertClean();
    }

    [Theory]
    [InlineData("oneOf", "{\"name\":\"n\"}", "Named:n")]
    [InlineData("oneOf", "42", "Int64:42")]
    [InlineData("oneOf", "\"x\"", "String:x")]
    [InlineData("oneOf", "true", "Boolean:True")]
    [InlineData("oneOf", "[1,2,3]", "Int32List:3")]
    [InlineData("anyOf", "{\"id\":1,\"name\":\"n\"}", "Pet:1")]
    public void EachJsonKind_ReadsItsVariant_AndRoundTrips(string keyword, string json, string expected)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec(keyword)), Probe($$"""
            var result = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Result)!;
            {{Describe}}
            var written = JsonSerializer.Serialize(result, MyApiJsonContext.Default.Result);
            return written == {{Quote(json)}} ? text : "wrote " + written;
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    [Fact]
    public void OneOf_MatchingTwoVariants_IsAmbiguous()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe("""
            try
            {
                JsonSerializer.Deserialize("{\"id\":1,\"name\":\"n\"}", MyApiJsonContext.Default.Result);
                return "read";
            }
            catch (JsonException exception)
            {
                return exception.Message;
            }
            """));

        Assert.Equal("The JSON value matches more than one variant of oneOf Result.", output.RunProbe());
    }

    [Fact]
    public void AnyOf_PrefersTheVariantMatchingTheMostRequiredProperties()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("anyOf")), Probe($$"""
            var result = JsonSerializer.Deserialize("{\"id\":1,\"name\":\"n\"}", MyApiJsonContext.Default.Result)!;
            {{Describe}}
            return text;
            """));

        Assert.Equal("Pet:1", output.RunProbe());
    }

    [Theory]
    [InlineData("{\"other\":1}")]
    [InlineData("null")]
    [InlineData("1.5")]
    public void NoMatchingVariant_Throws(string json)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe($$"""
            try
            {
                var result = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Result);
                return result is null ? "null" : "read";
            }
            catch (JsonException)
            {
                return "JsonException";
            }
            """));

        Assert.Equal(string.Equals(json, "null", StringComparison.Ordinal) ? "null" : "JsonException", output.RunProbe());
    }

    [Fact]
    public void Writing_RequiresExactlyOneValue()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe("""
            string Try(Result value)
            {
                try
                {
                    return JsonSerializer.Serialize(value, MyApiJsonContext.Default.Result);
                }
                catch (JsonException)
                {
                    return "JsonException";
                }
            }
            return Try(new Result()) + "|" + Try(new Result { AsInt64 = 1, AsBoolean = true }) + "|" + Try(new Result { AsString = "s" });
            """));

        Assert.Equal("JsonException|JsonException|\"s\"", output.RunProbe());
    }
}
```

`1.5` fails because the `Int64` variant accepts the number kind and then cannot read `1.5` as a `long`: the deserialize step throws `JsonException`, as spec §5.6 step 6 implies. A JSON `null` never reaches the converter: STJ returns `null` for a reference type.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~UnionEmitterTests"`
Expected: FAIL with `InvalidOperationException: No emitter for UnionModel 'Result'.`

- [ ] **Step 3: Write `UnionEmitter`**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/UnionEmitter.cs
using System.Globalization;
using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Spec §5.6: a union wrapper and its converter. The converter is emitted as source, not by a source
// generator, so the STJ source generator sees its [JsonConverter] attribute. Variants are read and
// written through options.GetTypeInfo, which resolves from the generated context and is not
// annotated for trimming, so nothing here uses reflection.
internal static class UnionEmitter
{
    private const string Serialization = ModelEmitter.Serialization;
    private const string Json = ModelEmitter.Json;

    internal static void Emit(StringBuilder sb, UnionModel model)
    {
        EmitWrapper(sb, model);
        sb.AppendLine();
        EmitConverter(sb, model);
    }

    private static void EmitWrapper(StringBuilder sb, UnionModel model)
    {
        CSharpNames.AppendDocComment(sb, "", model.Description);
        sb.Append('[').Append(Serialization).Append(".JsonConverter(typeof(").Append(model.Name).AppendLine("Converter))]");
        sb.Append("public sealed record ").AppendLine(model.Name);
        sb.AppendLine("{");
        foreach (var variant in model.Variants)
        {
            sb.Append("    public ").Append(TypeMapper.Declare(variant.Type, required: false, nullable: true))
                .Append(" As").Append(variant.Name).AppendLine(" { get; init; }");
            sb.AppendLine();
        }
        EmitMatch(sb, model);
        sb.AppendLine();
        EmitSwitch(sb, model);
        sb.AppendLine("}");
    }

    private static void EmitMatch(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public TResult Match<TResult>(")
            .Append(string.Join(", ", model.Variants.Select(v => $"global::System.Func<{v.Type.Name}, TResult> {Parameter(v)}")))
            .AppendLine(")");
        sb.AppendLine("    {");
        foreach (var variant in model.Variants)
            sb.Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(Parameter(variant)).AppendLine(");");
        foreach (var variant in model.Variants)
        {
            sb.Append("        if (As").Append(variant.Name).Append(" is not null) return ").Append(Parameter(variant))
                .Append('(').Append(Value(variant)).AppendLine(");");
        }
        sb.Append("        throw new global::System.InvalidOperationException(").Append(CSharpNames.Literal($"{model.Name} holds no value.")).AppendLine(");");
        sb.AppendLine("    }");
    }

    private static void EmitSwitch(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public void Switch(")
            .Append(string.Join(", ", model.Variants.Select(v => $"global::System.Action<{v.Type.Name}> {Parameter(v)}")))
            .AppendLine(")");
        sb.AppendLine("    {");
        foreach (var variant in model.Variants)
            sb.Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(Parameter(variant)).AppendLine(");");
        foreach (var variant in model.Variants)
        {
            sb.Append("        if (As").Append(variant.Name).AppendLine(" is not null)");
            sb.AppendLine("        {");
            sb.Append("            ").Append(Parameter(variant)).Append('(').Append(Value(variant)).AppendLine(");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
        }
        sb.Append("        throw new global::System.InvalidOperationException(").Append(CSharpNames.Literal($"{model.Name} holds no value.")).AppendLine(");");
        sb.AppendLine("    }");
    }

    private static void EmitConverter(StringBuilder sb, UnionModel model)
    {
        sb.Append("internal sealed class ").Append(model.Name).Append("Converter : ").Append(Serialization)
            .Append(".JsonConverter<").Append(model.Name).AppendLine(">");
        sb.AppendLine("{");
        for (var i = 0; i < model.Variants.Count; i++)
        {
            var required = model.Variants[i].RequiredWireNames;
            if (required.Count == 0)
                continue;
            sb.Append("    private static readonly string[] Required").Append(Index(i)).Append(" = [")
                .Append(string.Join(", ", required.Select(CSharpNames.Literal))).AppendLine("];");
        }
        sb.AppendLine();
        EmitRead(sb, model);
        sb.AppendLine();
        EmitWrite(sb, model);
        sb.AppendLine();
        sb.Append("    private static bool HasAll(").Append(Json).AppendLine(".JsonElement element, string[] names)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (var name in names)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!element.TryGetProperty(name, out _)) return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    // Parse once, filter by JSON kind, filter objects by required properties, then pick the variant
    // matching the most required properties, earliest first on a tie. oneOf rejects more than one
    // candidate as ambiguous; anyOf takes the pick.
    private static void EmitRead(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public override ").Append(model.Name).Append(" Read(ref ").Append(Json)
            .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.Append("        using var document = ").Append(Json).AppendLine(".JsonDocument.ParseValue(ref reader);");
        sb.AppendLine("        var element = document.RootElement;");
        sb.AppendLine("        var kind = element.ValueKind;");
        sb.AppendLine("        var candidates = 0;");
        sb.AppendLine("        var best = -1;");
        sb.AppendLine("        var bestScore = -1;");
        for (var i = 0; i < model.Variants.Count; i++)
        {
            var variant = model.Variants[i];
            var score = variant.RequiredWireNames.Count.ToString(CultureInfo.InvariantCulture);
            sb.Append("        if (").Append(Accepts(variant, i)).AppendLine(")");
            sb.AppendLine("        {");
            sb.AppendLine("            candidates++;");
            sb.Append("            if (").Append(score).Append(" > bestScore) { best = ").Append(Index(i)).Append("; bestScore = ").Append(score).AppendLine("; }");
            sb.AppendLine("        }");
        }
        sb.AppendLine("        if (candidates == 0)");
        sb.Append("            throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"The JSON value matches no variant of {model.Name}.")).AppendLine(");");
        if (model.IsOneOf)
        {
            sb.AppendLine("        if (candidates > 1)");
            sb.Append("            throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"The JSON value matches more than one variant of oneOf {model.Name}.")).AppendLine(");");
        }
        sb.AppendLine("        return best switch");
        sb.AppendLine("        {");
        for (var i = 0; i < model.Variants.Count; i++)
        {
            var variant = model.Variants[i];
            sb.Append("            ").Append(Index(i)).Append(" => new ").Append(model.Name).Append(" { As").Append(variant.Name)
                .Append(" = ").Append(Json).Append(".JsonSerializer.Deserialize(element, ").Append(TypeInfo(variant)).AppendLine(") },");
        }
        sb.Append("            _ => throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"The JSON value matches no variant of {model.Name}.")).AppendLine("),");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
    }

    private static void EmitWrite(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public override void Write(").Append(Json).Append(".Utf8JsonWriter writer, ").Append(model.Name)
            .Append(" value, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.Append("        var set = ").Append(string.Join(" + ", model.Variants.Select(v => $"(value.As{v.Name} is not null ? 1 : 0)"))).AppendLine(";");
        sb.AppendLine("        if (set != 1)");
        sb.Append("            throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"{model.Name} must hold exactly one value to be written.")).AppendLine(");");
        foreach (var variant in model.Variants)
        {
            sb.Append("        if (value.As").Append(variant.Name).AppendLine(" is not null)");
            sb.AppendLine("        {");
            sb.Append("            ").Append(Json).Append(".JsonSerializer.Serialize(writer, ").Append(Value(variant, "value.")).Append(", ").Append(TypeInfo(variant)).AppendLine(");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
    }

    private static string Accepts(UnionVariantModel variant, int index) => variant.Kind switch
    {
        JsonKind.Object when variant.RequiredWireNames.Count > 0
            => $"kind == {Json}.JsonValueKind.Object && HasAll(element, Required{Index(index)})",
        JsonKind.Object => $"kind == {Json}.JsonValueKind.Object",
        JsonKind.Array => $"kind == {Json}.JsonValueKind.Array",
        JsonKind.String => $"kind == {Json}.JsonValueKind.String",
        JsonKind.Number => $"kind == {Json}.JsonValueKind.Number",
        JsonKind.Boolean => $"kind is {Json}.JsonValueKind.True or {Json}.JsonValueKind.False",
        _ => "true",
    };

    private static string TypeInfo(UnionVariantModel variant)
        => $"({Json}.Serialization.Metadata.JsonTypeInfo<{variant.Type.Name}>)options.GetTypeInfo(typeof({variant.Type.Name}))";

    // A value-type variant is stored as T?, so its value is read through .Value once it is known set.
    private static string Value(UnionVariantModel variant, string owner = "")
        => variant.Type.IsValueType ? $"{owner}As{variant.Name}.Value" : $"{owner}As{variant.Name}";

    private static string Parameter(UnionVariantModel variant)
        => CSharpNames.Escape(CSharpNames.ToIdentifier(variant.Name, upperFirst: false));

    private static string Index(int index) => index.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Route unions through `ModelEmitter`**

Add to the `switch` in `ModelEmitter.Emit`:

```csharp
                case UnionModel union:
                    UnionEmitter.Emit(sb, union);
                    break;
```

and make `SerializableTypes` register every variant type:

```csharp
    internal static IEnumerable<string> SerializableTypes(IEnumerable<ModelDefinition> models)
    {
        foreach (var model in models)
        {
            yield return model.Name;
            if (model is UnionModel union)
            {
                foreach (var variant in union.Variants)
                    yield return variant.Type.Name;
            }
        }
    }
```

- [ ] **Step 5: Run the tests, then the full suite**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~UnionEmitterTests|FullyQualifiedName~ModelEmitterTests"`
Expected: PASS. If `JsonSerializer.Deserialize(element, ...)` returns `T?` and the compiler warns converting it into a value-type `As` property, the declared `T?` property already accepts it; if a reference-type variant warns, keep the property nullable as declared. Then run the full-suite command.

- [ ] **Step 6: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: emit union wrappers with deterministic converters

An undiscriminated oneOf or anyOf is written as a record with one As property per variant,
Match and Switch, and a converter. The converter filters by JSON kind and required
properties and prefers the best match; oneOf rejects two matches as ambiguous, anyOf
takes the best. Variants resolve through the generated context, never by reflection.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 14: Typed interface, file assembly and `Result` returns: the `feat!` commit

This is the one commit that changes what users get when they regenerate, so it carries the whole `BREAKING CHANGE` footer.

**Files:**
- Create: `src/ZeroAlloc.Rest.Tools.Shared/GenerationOptions.cs`, `ReferenceOnlyNamer.cs`, `OperationEmitter.cs`
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/OpenApiInterfaceGenerator.cs`: rewrite `Generate`; move `EmitMethod`, `Declaration`, `Skipped`, `EffectiveParameters`, `RewriteRoute`, `GetReturnType` into `OperationEmitter`; delete `MapSchemaType` and `MapSchemaTypeForReturn`
- Modify: `tests/ZeroAlloc.Rest.Tools.Tests/OpenApiInterfaceGeneratorTests.cs`, `OpenApiRouteBindingTests.cs`
- Modify: `tests/ZeroAlloc.Rest.DuplicateGeneratorTests/MSBuildTaskPackageTests.cs:106-160`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/OpenApiClientGenerationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3 to 13. `SchemaModelBuilder(OpenApiDocument, IEnumerable<string>, List<OpenApiWarning>)`, `TypeMapper.Map`/`Declare`, `ModelEmitter.Emit`/`SerializableTypes`, `JsonContextEmitter.Emit`, the generator's `UnitResult` and `__FormatValue` support.
- Produces:
  - `internal sealed record GenerationOptions(bool GenerateModels)` with `static GenerationOptions Default`.
  - `OpenApiInterfaceGenerator.Generate(string yamlOrJson, string @namespace, string interfaceName, List<OpenApiWarning> warnings, GenerationOptions options)`. The existing overloads keep their signatures and pass `GenerationOptions.Default`.
  - The generated file: `// <auto-generated/>`, `#nullable enable`, the namespace, the interface, the models, then `{Name}JsonContext` when any type needs registering.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/OpenApiClientGenerationTests.cs
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §3, §6 and §10: one file per spec, typed members, Result returns, and a client that compiles
// with no diagnostics and round-trips through its generated context.
public class OpenApiClientGenerationTests
{
    private const string A = "global::ZeroAlloc.Rest.Attributes.";
    private const string Ct = "global::System.Threading.CancellationToken ct = default";
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string HttpError = "global::ZeroAlloc.Rest.HttpError";

    internal const string PetsSpec = """
        openapi: 3.0.0
        info:
          title: Pets
          version: "1"
        paths:
          /pets:
            get:
              operationId: listPets
              parameters:
                - name: status
                  in: query
                  schema:
                    type: string
                    enum: [available, sold]
                - name: limit
                  in: query
                  required: true
                  schema:
                    type: integer
                    format: int32
                - name: X-Trace
                  in: header
                  schema:
                    type: string
                    format: uuid
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          $ref: '#/components/schemas/Pet'
            post:
              operationId: addPet
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Pet'
              responses:
                '201':
                  description: Created
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
          /pets/{petId}:
            parameters:
              - name: petId
                in: path
                required: true
                schema:
                  type: integer
                  format: int64
            get:
              operationId: getPet
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
            delete:
              operationId: deletePet
              responses:
                '204':
                  description: Deleted
          /pets/{petId}/photo:
            parameters:
              - name: petId
                in: path
                required: true
                schema:
                  type: integer
                  format: int64
            get:
              operationId: getPhoto
              responses:
                '200':
                  description: The photo
                  content:
                    image/png:
                      schema:
                        type: string
                        format: binary
            put:
              operationId: putPhoto
              requestBody:
                content:
                  application/octet-stream:
                    schema:
                      type: string
                      format: binary
              responses:
                '204':
                  description: Stored
          /stats:
            get:
              operationId: getStats
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        type: object
                        properties:
                          count:
                            type: integer
        components:
          schemas:
            Pet:
              type: object
              required: [id, name]
              properties:
                id:
                  type: integer
                  format: int64
                name:
                  type: string
                status:
                  type: string
                  enum: [available, sold]
        """;

    private static (string Code, List<OpenApiWarning> Warnings) Generate(bool models = true)
    {
        var warnings = new List<OpenApiWarning>();
        var code = OpenApiInterfaceGenerator.Generate(PetsSpec, "MyApp", "IPetsApi", warnings, new GenerationOptions(models));
        return (code, warnings);
    }

    [Fact]
    public void File_HoldsTheInterfaceThenTheModelsThenTheContext()
    {
        var code = Generate().Code;

        Assert.StartsWith("// <auto-generated/>", code, StringComparison.Ordinal);
        Assert.Contains("#nullable enable", code);
        var iface = code.IndexOf("public interface IPetsApi", StringComparison.Ordinal);
        var model = code.IndexOf("public sealed record Pet", StringComparison.Ordinal);
        var context = code.IndexOf("public partial class PetsApiJsonContext", StringComparison.Ordinal);
        Assert.True(iface >= 0 && iface < model && model < context, code);
        Assert.DoesNotContain("using ", code);
    }

    [Fact]
    public void Methods_AreTyped_AndReturnResults()
    {
        var code = Generate().Code;

        Assert.Contains(
            $"{Task}<global::ZeroAlloc.Results.Result<global::System.Collections.Generic.List<Pet>, {HttpError}>> ListPetsAsync("
                + $"[{A}Query] ListPetsStatus? status, [{A}Query] int limit, [{A}Header(\"X-Trace\")] global::System.Guid? xTrace, {Ct});",
            code);
        Assert.Contains($"{Task}<global::ZeroAlloc.Results.Result<Pet, {HttpError}>> AddPetAsync([{A}Body] Pet body, {Ct});", code);
        Assert.Contains($"{Task}<global::ZeroAlloc.Results.Result<Pet, {HttpError}>> GetPetAsync(long petId, {Ct});", code);
        Assert.Contains($"{Task}<global::ZeroAlloc.Results.UnitResult<{HttpError}>> DeletePetAsync(long petId, {Ct});", code);
        Assert.Contains($"{Task}<global::ZeroAlloc.Results.Result<GetStatsResponse, {HttpError}>> GetStatsAsync({Ct});", code);
        Assert.Contains($"[{A}Get(\"/pets/{{petId}}\")]", code);
    }

    [Fact]
    public void NonJsonContent_KeepsAnUntypedBody_AndReportsAnUntypedResponse()
    {
        var (code, warnings) = Generate();

        Assert.Contains($"{Task}<global::ZeroAlloc.Results.Result<global::System.Text.Json.JsonElement, {HttpError}>> GetPhotoAsync(long petId, {Ct});", code);
        Assert.Contains($"{Task}<global::ZeroAlloc.Results.UnitResult<{HttpError}>> PutPhotoAsync(long petId, [{A}Body] object body, {Ct});", code);
        var warning = Assert.Single(warnings);
        Assert.Equal("ZRT002", warning.Code);
        Assert.Equal(
            "Schema 'getPhoto: response 200' is mapped to JsonElement, because its content 'image/png' is not JSON, which the generated client cannot read yet.",
            warning.Message);
    }

    [Fact]
    public void Context_RegistersEveryRequestAndResponseType()
    {
        var code = Generate().Code;

        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(global::System.Collections.Generic.List<Pet>))]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(GetStatsResponse))]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(global::System.Text.Json.JsonElement))]", code);
    }

    [Fact]
    public void GeneratedClient_CompilesWithNoDiagnostics()
        => GeneratedCode.Compile(Generate().Code).AssertClean();

    [Fact]
    public void GeneratedClient_RoundTrips_AndReturnsDeserializationFailuresAsResults()
    {
        var output = GeneratedCode.Compile(Generate().Code, """
            using System;
            using System.Net;
            using System.Net.Http;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.SystemTextJson;

            public sealed class Stub : HttpMessageHandler
            {
                private readonly string _json;

                public Stub(string json) => _json = json;

                public string? LastBody { get; private set; }

                public string? LastUri { get; private set; }

                protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    LastUri = request.RequestUri?.PathAndQuery;
                    LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
                }
            }

            public static class Probe
            {
                public static string Run() => RunAsync().GetAwaiter().GetResult();

                private static MyApp.IPetsApi Client(Stub stub)
                    => new MyApp.PetsApiClient(
                        new HttpClient(stub) { BaseAddress = new Uri("http://stub/") },
                        new SystemTextJsonSerializer(MyApp.PetsApiJsonContext.Default));

                private static async Task<string> RunAsync()
                {
                    var good = new Stub("{\"id\":1,\"name\":\"Rex\",\"status\":\"sold\"}");
                    var api = Client(good);
                    var pet = await api.GetPetAsync(1);
                    await api.AddPetAsync(new MyApp.Pet { Id = 2, Name = "Tom" });
                    var body = good.LastBody;
                    var deleted = await api.DeletePetAsync(1);
                    await api.ListPetsAsync(MyApp.ListPetsStatus.Available, 10, null);
                    var uri = good.LastUri;

                    var unknown = await Client(new Stub("{\"id\":1,\"name\":\"Rex\",\"status\":\"lost\"}")).GetPetAsync(1);

                    return (pet.IsSuccess ? pet.Value.Name + ":" + pet.Value.Status : "failed")
                        + "|" + body + "|" + deleted.IsSuccess + "|" + uri + "|" + (unknown.IsFailure ? unknown.Error.Kind.ToString() : "read");
                }
            }
            """);

        Assert.Equal(
            "Rex:Sold|{\"id\":2,\"name\":\"Tom\"}|True|/pets?status=available&limit=10|Deserialization",
            output.RunProbe());
    }

    [Fact]
    public void ModelsOff_UsesBareNames_AndCompilesAgainstHandWrittenDtos()
    {
        var (code, warnings) = Generate(models: false);

        Assert.DoesNotContain("public sealed record", code);
        Assert.Contains($"{Task}<global::ZeroAlloc.Results.Result<Pet, {HttpError}>> GetPetAsync(long petId, {Ct});", code);
        Assert.Contains("[global::ZeroAlloc.Rest.Attributes.Query] global::System.Text.Json.JsonElement? status", code);
        Assert.Equal(3, warnings.Count(w => string.Equals(w.Code, "ZRT002", StringComparison.Ordinal)));
        GeneratedCode.Compile(code, """
            namespace MyApp;

            public sealed record Pet
            {
                public required long Id { get; init; }

                public required string Name { get; init; }
            }
            """).AssertClean();
    }

    [Fact]
    public void SpecWithNoBodies_EmitsNoContext()
    {
        const string Spec = """
            openapi: 3.0.0
            info:
              title: Status
              version: "1"
            paths:
              /status:
                get:
                  operationId: getStatus
                  responses:
                    '204':
                      description: OK
            """;

        var code = OpenApiInterfaceGenerator.Generate(Spec, "MyApp", "IStatusApi");

        Assert.DoesNotContain("JsonSerializerContext", code);
        GeneratedCode.Compile(code).AssertClean();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: FAIL with CS0246 for `GenerationOptions`.

- [ ] **Step 3: Add the options and the models-off namer**

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/GenerationOptions.cs
namespace ZeroAlloc.Rest.Tools;

// What a run generates besides the interface. The CLI's --models and the MSBuild item's
// GenerateModels metadata set GenerateModels.
internal sealed record GenerationOptions(bool GenerateModels)
{
    internal static GenerationOptions Default { get; } = new(GenerateModels: true);
}
```

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/ReferenceOnlyNamer.cs
using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// The namer for --models false, design decision 16: a $ref is its bare PascalCase name, which a
// hand-written DTO in the same namespace satisfies, and an inline schema that would need a model
// has no type, so it maps to JsonElement with ZRT002.
internal sealed class ReferenceOnlyNamer(List<OpenApiWarning> warnings) : ISchemaTypeNamer
{
    public TypeRef Named(OpenApiSchema schema, string contextName, string path)
    {
        if (schema.Reference?.Id is { } id)
            return new TypeRef(CSharpNames.Pascal(id, "Model"), TypeRefKind.Model, IsValueType: TypeMapper.IsEnum(schema));
        Unsupported(path, "models are off, so an inline schema has no generated type");
        return TypeRef.JsonElement;
    }

    public void Unsupported(string path, string reason) => warnings.Add(OpenApiWarning.MappedToJsonElement(path, reason));
}
```

- [ ] **Step 4: Write `OperationEmitter`**

Move `EffectiveParameters`, `RewriteRoute` and `Skipped` from `OpenApiInterfaceGenerator` into this class unchanged, then write the rest:

```csharp
// src/ZeroAlloc.Rest.Tools.Shared/OperationEmitter.cs
using System.Text;
using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// Writes one interface method per operation, spec §6: parameters and body typed from their schemas,
// and a Result or UnitResult return. Every type is global::-qualified, design decision 10.
internal static class OperationEmitter
{
    private const string Attributes = "global::ZeroAlloc.Rest.Attributes.";
    private const string ResultOf = "global::System.Threading.Tasks.Task<global::ZeroAlloc.Results.Result<";
    private const string UnitResult = "global::System.Threading.Tasks.Task<global::ZeroAlloc.Results.UnitResult<global::ZeroAlloc.Rest.HttpError>>";
    private const string HttpError = "global::ZeroAlloc.Rest.HttpError";

    internal static void Emit(
        StringBuilder sb, string path, OpenApiPathItem pathItem, OperationType operationType, OpenApiOperation operation,
        ISchemaTypeNamer namer, List<string> serializable, List<OpenApiWarning> warnings)
    {
        var httpAttr = operationType switch
        {
            OperationType.Get => "Get",
            OperationType.Post => "Post",
            OperationType.Put => "Put",
            OperationType.Patch => "Patch",
            OperationType.Delete => "Delete",
            _ => null,
        };
        if (httpAttr is null) return;

        var baseName = CSharpNames.ToIdentifier(CSharpNames.ToPascalCase(operation.OperationId
            ?? $"{httpAttr}{path.Replace("/", "_").Replace("{", "").Replace("}", "")}"), upperFirst: true);
        var operationName = operation.OperationId ?? $"{httpAttr.ToUpperInvariant()} {path}";

        // The CancellationToken is always ct and the request body always body. A parameter whose
        // identifier would clash with either, or with an earlier parameter, gets a numeric suffix.
        var used = new HashSet<string>(StringComparer.Ordinal) { "ct" };
        if (operation.RequestBody != null)
            used.Add("body");

        var parameters = new List<string>();
        var comments = new List<string>();
        var routeIdentifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var param in EffectiveParameters(pathItem, operation))
        {
            if (param.In is not (ParameterLocation.Path or ParameterLocation.Query or ParameterLocation.Header))
            {
                var (comment, warning) = Skipped(param, operationName);
                comments.Add(comment);
                warnings.Add(warning);
                continue;
            }

            var identifier = CSharpNames.Unique(CSharpNames.ToIdentifier(param.Name, upperFirst: false), used);
            // The source generator binds a {token} to the parameter of exactly its name, so the
            // route's token is rewritten to the identifier. See RewriteRoute.
            if (param.In == ParameterLocation.Path)
                routeIdentifiers[param.Name] = identifier;
            parameters.Add(Declaration(param, identifier, ParameterType(param, baseName, operationName, namer)));
        }

        if (operation.RequestBody != null)
            parameters.Add(BodyParameter(operation.RequestBody, baseName, operationName, namer, serializable));
        parameters.Add("global::System.Threading.CancellationToken ct = default");

        foreach (var comment in comments)
            sb.Append("    ").AppendLine(comment);
        sb.Append("    [").Append(Attributes).Append(httpAttr).Append('(').Append(CSharpNames.Literal(RewriteRoute(path, routeIdentifiers))).AppendLine(")]");
        sb.Append("    ").Append(ReturnType(operation, baseName, operationName, namer, serializable))
            .Append(' ').Append(baseName).Append("Async(").Append(string.Join(", ", parameters)).AppendLine(");");
        sb.AppendLine();
    }

    // Design decision 14: a path parameter, or a required one, is T; an optional one is T?.
    private static string ParameterType(OpenApiParameter param, string baseName, string operationName, ISchemaTypeNamer namer)
    {
        var type = param.Schema is null
            ? TypeRef.String
            : TypeMapper.Map(param.Schema, baseName + CSharpNames.Pascal(param.Name, "Value"), $"{operationName}: parameter '{param.Name}'", namer);
        var required = param.Required || param.In == ParameterLocation.Path;
        return TypeMapper.Declare(type, required, param.Schema?.Nullable == true);
    }

    private static string Declaration(OpenApiParameter param, string identifier, string type)
    {
        var name = CSharpNames.Escape(identifier);
        return param.In switch
        {
            ParameterLocation.Query when string.Equals(identifier, param.Name, StringComparison.Ordinal)
                => $"[{Attributes}Query] {type} {name}",
            ParameterLocation.Query => $"[{Attributes}Query(Name = {CSharpNames.Literal(param.Name)})] {type} {name}",
            ParameterLocation.Header => $"[{Attributes}Header({CSharpNames.Literal(param.Name)})] {type} {name}",
            _ => $"{type} {name}",
        };
    }

    // Design decision 4: only JSON content is typed. A body without it, or one that maps to a
    // Stream, keeps today's [Body] object body.
    private static string BodyParameter(OpenApiRequestBody body, string baseName, string operationName, ISchemaTypeNamer namer, List<string> serializable)
    {
        const string Untyped = Attributes + "Body] object body";
        if (JsonSchema(body.Content) is not { } schema)
            return "[" + Untyped;
        var type = TypeMapper.Map(schema, baseName + "Request", $"{operationName}: request body", namer, isBody: true);
        if (type.Kind == TypeRefKind.Stream)
            return "[" + Untyped;
        serializable.Add(type.Name);
        return $"[{Attributes}Body] {TypeMapper.Declare(type, body.Required, schema.Nullable)} body";
    }

    // Spec §6: the success type comes from the first 2xx response with content; none gives UnitResult.
    private static string ReturnType(OpenApiOperation operation, string baseName, string operationName, ISchemaTypeNamer namer, List<string> serializable)
    {
        foreach (var (status, response) in operation.Responses)
        {
            if (!status.StartsWith('2') || response.Content is null || response.Content.Count == 0)
                continue;
            var where = $"{operationName}: response {status}";
            TypeRef type;
            if (JsonSchema(response.Content) is { } schema)
            {
                type = TypeMapper.Map(schema, baseName + "Response", where, namer, isBody: true);
                if (type.Kind == TypeRefKind.Stream)
                {
                    namer.Unsupported(where, "binary content needs a raw stream response, which ZeroAlloc.Rest does not support yet");
                    type = TypeRef.JsonElement;
                }
            }
            else
            {
                namer.Unsupported(where, $"its content '{response.Content.Keys.First()}' is not JSON, which the generated client cannot read yet");
                type = TypeRef.JsonElement;
            }
            serializable.Add(type.Name);
            return $"{ResultOf}{type.Name}, {HttpError}>>";
        }
        return UnitResult;
    }

    private static OpenApiSchema? JsonSchema(IDictionary<string, OpenApiMediaType> content)
    {
        foreach (var (mediaType, media) in content)
        {
            if (media.Schema is not null && IsJson(mediaType))
                return media.Schema;
        }
        return null;
    }

    // Design decision 5. */* counts: springdoc writes it for JSON responses.
    private static bool IsJson(string mediaType)
    {
        var type = mediaType.Split(';')[0].Trim();
        return string.Equals(type, "application/json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "text/json", StringComparison.OrdinalIgnoreCase)
            || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "*/*", StringComparison.Ordinal);
    }
}
```

`Skipped`, `EffectiveParameters` and `RewriteRoute` keep their bodies and comments; `RewriteRoute` and `Unique` callers use `CSharpNames` since Task 6. If the analyzer flags the moved `path.Replace` calls, add `StringComparison.Ordinal` to each.

- [ ] **Step 5: Rewrite `OpenApiInterfaceGenerator.Generate`**

Replace the three `Generate` overloads and delete `EmitMethod`, `Declaration`, `Skipped`, `EffectiveParameters`, `RewriteRoute`, `GetReturnType`, `MapSchemaTypeForReturn` and `MapSchemaType`. Keep `GenerateFromFile`, `GenerateFromFileAsync` and `GenerateFromUrlAsync` as they are.

```csharp
    internal static string Generate(string yamlOrJson, string @namespace, string interfaceName)
        => Generate(yamlOrJson, @namespace, interfaceName, new List<OpenApiWarning>(), GenerationOptions.Default);

    internal static string Generate(string yamlOrJson, string @namespace, string interfaceName, List<OpenApiWarning> warnings)
        => Generate(yamlOrJson, @namespace, interfaceName, warnings, GenerationOptions.Default);

    // Spec §3: one file per spec, in this order: the interface, the models it references, and the
    // JSON context covering both. Adds a warning for each part of the spec the file leaves out or
    // cannot type, so that the CLI and the MSBuild task can report it.
    internal static string Generate(string yamlOrJson, string @namespace, string interfaceName, List<OpenApiWarning> warnings, GenerationOptions options)
    {
        var document = Parse(yamlOrJson);
        var baseName = WithoutInterfacePrefix(interfaceName);
        var contextName = baseName + "JsonContext";
        var builder = options.GenerateModels
            ? new SchemaModelBuilder(document, [interfaceName, baseName + "Client", contextName], warnings)
            : null;
        ISchemaTypeNamer namer = builder is null ? new ReferenceOnlyNamer(warnings) : builder;

        var methods = new StringBuilder();
        var serializable = new List<string>();
        if (document.Paths != null)
        {
            foreach (var (path, pathItem) in document.Paths)
            {
                foreach (var (operationType, operation) in pathItem.Operations)
                    OperationEmitter.Emit(methods, path, pathItem, operationType, operation, namer, serializable, warnings);
            }
        }
        var models = builder?.Build() ?? EquatableList<ModelDefinition>.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.Append("namespace ").Append(@namespace).AppendLine(";");
        sb.AppendLine();
        sb.Append('[').Append("global::ZeroAlloc.Rest.Attributes.ZeroAllocRestClient").AppendLine("]");
        sb.Append("public interface ").AppendLine(interfaceName);
        sb.AppendLine("{");
        sb.Append(methods);
        sb.AppendLine("}");
        sb.AppendLine();
        ModelEmitter.Emit(sb, models);
        // A context with no [JsonSerializable] would leave JsonSerializerContext's abstract members
        // unimplemented, so a spec with no bodies gets none.
        var types = serializable.Concat(ModelEmitter.SerializableTypes(models)).ToList();
        if (types.Count > 0)
            JsonContextEmitter.Emit(sb, contextName, types);
        return sb.ToString();
    }

    private static OpenApiDocument Parse(string yamlOrJson)
    {
        var reader = new OpenApiStringReader();
        var document = reader.Read(yamlOrJson, out var diagnostic);
        if (document == null)
            throw new InvalidOperationException("Failed to parse OpenAPI document.");
        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI parse errors: {errors}");
        }
        return document;
    }

    // The Rest source generator's rule for the client name: IPetStoreClient gives PetStoreClientClient,
    // so the context is PetStoreClientJsonContext.
    private static string WithoutInterfacePrefix(string interfaceName)
        => interfaceName.Length > 1 && interfaceName[0] == 'I' ? interfaceName[1..] : interfaceName;
```

- [ ] **Step 6: Run the new tests**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~OpenApiClientGenerationTests"`
Expected: PASS. A diagnostic from `AssertClean` names the file and line; fix what the emitter writes.

- [ ] **Step 7: Update the existing tool tests to the new output**

In `OpenApiInterfaceGeneratorTests` and `OpenApiRouteBindingTests`, change the expected text only, never the specs:

| Old expected text | New expected text |
|---|---|
| `[ZeroAllocRestClient]` | `[global::ZeroAlloc.Rest.Attributes.ZeroAllocRestClient]` |
| `[Get(`, `[Post(`, `[Put(` | `[global::ZeroAlloc.Rest.Attributes.Get(`, and so on |
| `[Body]` | `[global::ZeroAlloc.Rest.Attributes.Body]` |
| `CancellationToken ct = default` | `global::System.Threading.CancellationToken ct = default` |
| `Task<UserDto>` | `global::System.Threading.Tasks.Task<global::ZeroAlloc.Results.Result<UserDto, global::ZeroAlloc.Rest.HttpError>>` |
| `Task<List<UserDto>>` | `global::System.Threading.Tasks.Task<global::ZeroAlloc.Results.Result<global::System.Collections.Generic.List<UserDto>, global::ZeroAlloc.Rest.HttpError>>` |
| `[Query(Name = "page-size")] int pageSize` | `[global::ZeroAlloc.Rest.Attributes.Query(Name = "page-size")] int? pageSize` |
| `[Query(Name = "PageToken")] string pageToken` | `[global::ZeroAlloc.Rest.Attributes.Query(Name = "PageToken")] string? pageToken` |
| `[Header("X-Request-Id")] string xRequestId` | `[global::ZeroAlloc.Rest.Attributes.Header("X-Request-Id")] string? xRequestId` |

The `PutItemAsync` assertion in `ParametersWhoseIdentifiersCollide_GetDistinctNames_AndThePathOneBindsItsToken` becomes:

```csharp
        Assert.Contains("[global::ZeroAlloc.Rest.Attributes.Put(\"/items/{id2}\")]", code);
        Assert.Contains(
            "PutItemAsync([global::ZeroAlloc.Rest.Attributes.Query] string? id, int id2, "
                + "[global::ZeroAlloc.Rest.Attributes.Query(Name = \"ct\")] string? ct2, "
                + "[global::ZeroAlloc.Rest.Attributes.Header(\"body\")] string? body2, "
                + "[global::ZeroAlloc.Rest.Attributes.Body] global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.JsonElement>? body, "
                + "global::System.Threading.CancellationToken ct = default)",
            code);
```

The query and header parameters there have no `required`, so they are optional and nullable; the body is `type: object` with no properties and no `required: true`.

- [ ] **Step 8: Exercise models and the context in the package consumer test**

In `MSBuildTaskPackageTests.WriteSpecs`, give `getPet` a JSON response and a component, replacing the `'200': description: OK` lines of `pets.yaml`:

```yaml
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Pet'
            components:
              schemas:
                Pet:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: integer
                      format: int64
```

In `ScaffoldConsumer`, make `Program.cs` print the model and the context too:

```csharp
            System.Console.WriteLine(typeof(Consumer.PetsApiClient).FullName);
            System.Console.WriteLine(typeof(Consumer.Status.ApiClientClient).FullName);
            System.Console.WriteLine(typeof(Consumer.Pet).FullName);
            System.Console.WriteLine(typeof(Consumer.PetsApiJsonContext).FullName);
```

and assert them in the test after the existing `Consumer.PetsApiClient` assertion:

```csharp
            Assert.Contains("Consumer.Pet", runOut, StringComparison.Ordinal);
            Assert.Contains("Consumer.PetsApiJsonContext", runOut, StringComparison.Ordinal);
```

This build runs the real STJ source generator in a consumer with `TreatWarningsAsErrors` off, which the in-memory harness cannot prove.

- [ ] **Step 9: Run the full suite**

Run the full-suite command. Expected: every project passes, `MSBuildTaskPackageTests` included.

- [ ] **Step 10: Commit, the one `feat!`**

```bash
git add -A src/ZeroAlloc.Rest.Tools.Shared tests
git commit -m "$(cat <<'EOF'
feat!: generate models, typed members and Result returns from OpenAPI specs

zeroalloc generate and ZeroAlloc.Rest.Tools.MSBuild now write one file per spec: the
interface, a record, enum, hierarchy or union for every schema it references, and a
source-generated JsonSerializerContext named after the interface. Parameters and bodies
are typed from their schemas, and every method returns Result or UnitResult of HttpError.

BREAKING CHANGE: regenerating a client changes its shape, and the runtime changes with it.
- Generated methods return Task of Result<T, HttpError>, or Task of UnitResult<HttpError>
  when no 2xx response has a schema, instead of Task of T or Task. A non-success status, a
  transport failure, a timeout and an unreadable body come back as an HttpError. Cancellation
  the caller asked for and an unserializable request body still throw.
- Request bodies are typed from their schema instead of object. Parameters follow type and
  format: int64 is long, date-time is DateTimeOffset, uuid is Guid, and enums are enums.
  Optional query and header parameters are nullable; headers are typed from their schema.
- Models are generated by default, so they can clash with hand-written DTOs. Pass
  --models false, or GenerateModels="false" on the item, to keep your own.
- The generated file uses global::-qualified names and enables nullable annotations.
- IRestSerializer and the in-repo serializers no longer carry RequiresDynamicCode or
  RequiresUnreferencedCode, and generated clients no longer carry UnconditionalSuppressMessage.
  A custom serializer must drop those attributes. The parameterless and options
  SystemTextJsonSerializer constructors now carry them; pass a JsonSerializerContext instead.
- MemoryPackRestSerializer serves only registered types plus MemoryPack built-ins: pass a delegate
  that registers every MemoryPackable type it serializes, such as types.Add<User>. A client using
  the Serializer attribute for it registers that configured instance in DI before Add{Interface}.
- Route, query and header values are written invariantly: ISO 8601 dates and times, true and
  false, and enum wire names. A null header value sends no header.
- docs/migrating-to-v3.md walks through each change.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

Check the body before committing: every line at most 100 characters, and no parenthesis inside another.

---

### Task 15: `--models` and `GenerateModels`

**Files:**
- Modify: `src/ZeroAlloc.Rest.Tools.Shared/OpenApiInterfaceGenerator.cs`: `GenerateFromFileAsync`, `GenerateFromUrlAsync`
- Modify: `src/ZeroAlloc.Rest.Tools/Program.cs`
- Modify: `src/ZeroAlloc.Rest.Tools.MSBuild/GenerateRestClientTask.cs`, `build/ZeroAlloc.Rest.Tools.MSBuild.targets`, `ZeroAlloc.Rest.Tools.MSBuild.csproj`
- Create: `src/ZeroAlloc.Rest.Tools.MSBuild/PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/CommandLineTests.cs`, `GenerateRestClientTaskTests.cs`

**Interfaces:**
- Consumes: `GenerationOptions` from Task 14.
- Produces: `GenerateFromFileAsync(string filePath, string @namespace, string interfaceName, List<OpenApiWarning> warnings, GenerationOptions options, CancellationToken ct)` and the same for `GenerateFromUrlAsync`; CLI `--models true|false`, default `true`; task property `public bool GenerateModels { get; set; } = true`; item metadata `GenerateModels`.

- [ ] **Step 1: Write the failing tests**

In `CommandLineTests`, generalise `Run` to take the spec and return the output file too, then add:

```csharp
    internal const string ModelSpec = """
        openapi: 3.0.0
        info:
          title: Test
          version: "1"
        paths:
          /pets/{id}:
            get:
              operationId: getPet
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    type: integer
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              properties:
                name:
                  type: string
        """;

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "--models", "true" }, true)]
    [InlineData(new[] { "--models", "false" }, false)]
    public void Models_AreGeneratedUnlessTurnedOff(string[] extraArgs, bool expected)
    {
        var (exitCode, _, output) = Run(ModelSpec, extraArgs);

        Assert.Equal(0, exitCode);
        Assert.Equal(expected, output.Contains("public sealed record Pet", StringComparison.Ordinal));
    }
```

xUnit passes `new string[0]` as one argument only when the parameter list has a second parameter, as here. The reworked `Run(string spec, params string[] extraArgs)` returns `(int ExitCode, string Stderr, string Output)`, reading the output file before the temp directory is deleted; update the two existing tests to call `Run(CookieSpec, ...)`.

In `GenerateRestClientTaskTests` add:

```csharp
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GenerateModels_ControlsTheModels(bool generateModels)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var spec = Path.Combine(dir, "openapi.yaml");
            File.WriteAllText(spec, CommandLineTests.ModelSpec);
            var output = Path.Combine(dir, "IMyApi.g.cs");
            var task = new GenerateRestClientTask
            {
                BuildEngine = new RecordingBuildEngine(),
                Spec = spec,
                OutputPath = output,
                Namespace = "MyApp",
                InterfaceName = "IMyApi",
                GenerateModels = generateModels,
            };

            Assert.True(task.Execute());
            Assert.Equal(generateModels, File.ReadAllText(output).Contains("public sealed record Pet", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
```

`ModelSpec` is `internal` so the task test shares it.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: FAIL with CS0117: `GenerateRestClientTask` has no `GenerateModels`. After adding only that property, the CLI theory fails with "Unrecognized command or argument '--models'".

- [ ] **Step 3: Thread the options through the file and URL entry points**

```csharp
    internal static Task<string> GenerateFromFileAsync(
        string filePath, string @namespace, string interfaceName,
        CancellationToken ct = default)
        => GenerateFromFileAsync(filePath, @namespace, interfaceName, new List<OpenApiWarning>(), GenerationOptions.Default, ct);

    internal static async Task<string> GenerateFromFileAsync(
        string filePath, string @namespace, string interfaceName, List<OpenApiWarning> warnings,
        GenerationOptions options, CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return Generate(content, @namespace, interfaceName, warnings, options);
    }
```

and the same shape for `GenerateFromUrlAsync`, whose short overload passes `GenerationOptions.Default`.

- [ ] **Step 4: Add the CLI option**

In `Program.cs`:

```csharp
var modelsOption = new Option<bool>("--models")
{
    Description = "Generate a type for each schema the interface references; false keeps your own DTOs",
    DefaultValueFactory = _ => true,
};
```

Add it with `generateCommand.Options.Add(modelsOption);`, read it with `var options = new GenerationOptions(parseResult.GetValue(modelsOption));`, and pass `options` to both generate calls before `ct`.

- [ ] **Step 5: Add the task property and the item metadata**

In `GenerateRestClientTask`:

```csharp
    // Metadata GenerateModels="false" keeps hand-written DTOs. MSBuild does not set a parameter
    // whose metadata is empty, so an item without it keeps the default.
    public bool GenerateModels { get; set; } = true;
```

Pass `new GenerationOptions(GenerateModels)` to both generate calls. In the targets, add to `<GenerateRestClientTask ...>`:

```xml
        GenerateModels="%(ZeroAllocApiSpec.GenerateModels)"
```

The package consumer test from Task 14 has items without the metadata, so it proves the default.

- [ ] **Step 6: Track the task's public API**

Add to `ZeroAlloc.Rest.Tools.MSBuild.csproj`:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
    <AdditionalFiles Include="PublicAPI.Shipped.txt" />
    <AdditionalFiles Include="PublicAPI.Unshipped.txt" />
  </ItemGroup>
```

`PublicAPI.Shipped.txt`:

```text
#nullable enable
override ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.Execute() -> bool
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.GenerateRestClientTask() -> void
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.InterfaceName.get -> string!
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.InterfaceName.set -> void
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.Namespace.get -> string!
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.Namespace.set -> void
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.OutputPath.get -> string!
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.OutputPath.set -> void
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.Spec.get -> string!
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.Spec.set -> void
```

`PublicAPI.Unshipped.txt`:

```text
#nullable enable
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.GenerateModels.get -> bool
ZeroAlloc.Rest.Tools.MSBuild.GenerateRestClientTask.GenerateModels.set -> void
```

Take the analyzer's spelling if RS0016 or RS0017 disagrees with a line.

- [ ] **Step 7: Run the tests, then the full suite**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release`
Expected: PASS. Then run the full-suite command.

- [ ] **Step 8: Commit**

```bash
git add -A src/ZeroAlloc.Rest.Tools src/ZeroAlloc.Rest.Tools.MSBuild src/ZeroAlloc.Rest.Tools.Shared tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
feat: add --models and GenerateModels to keep hand-written DTOs

zeroalloc generate takes --models false, and a ZeroAllocApiSpec item takes
GenerateModels="false", to generate the interface and JSON context without models, so
references resolve to your own DTOs. Both default to true. The MSBuild task package now
tracks its public API.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 16: Docs and the migration guide

**Files:**
- Modify: `docs/openapi-codegen.md`, `docs/advanced.md`, `docs/serialization.md`, `docs/native-aot.md`, `docs/cookbook/02-openapi-import.md`, `docs/cookbook/04-native-aot-publish.md`, `docs/README.md`, `README.md`
- Create: `docs/migrating-to-v3.md`
- Separate PR in another repo: `C:\Projects\Prive\ZeroAlloc\ZeroAlloc.Serialisation\README.md:119-126`

**Interfaces:**
- Consumes: the behaviour of Tasks 2 to 15, exactly as built. Where this task's text and the code disagree, the code wins: re-run the generator on the Task 14 `PetsSpec` and copy from its output.
- Produces: nothing code depends on.

Docs have no unit tests. The check for each step is the verification in Step 9.

- [ ] **Step 1: `docs/openapi-codegen.md`**

Update the front-matter `description` to "Generate ZeroAllocRestClient interfaces, models and a JSON context from OpenAPI 3.0 specs with the CLI or the MSBuild task." Then:

- In "## CLI", after the `--interface` sentence, add: "`--models false` generates the interface and the JSON context without models, so `$ref`s resolve to your own DTOs; the default is `true`."
- Replace "## Generation rules" with this section:

````markdown
## What is generated

One `.cs` file per spec, in one namespace, in this order:

1. the `[ZeroAllocRestClient]` interface;
2. a type for every schema the interface references, directly or through another schema;
3. `{Name}JsonContext`, a source-generated `JsonSerializerContext` covering every model and every
   request and response type, where `{Name}` is the interface name without its leading `I`. A spec
   with no request or response bodies gets no context.

The file starts with `// <auto-generated/>` and `#nullable enable`, and writes every framework and
ZeroAlloc type as `global::`, so a schema named `Task` or `Header` cannot shadow one.

### Methods

- `operationId` → method name, PascalCase, suffixed with `Async`.
- Every method returns `Task<Result<T, HttpError>>`, where `T` comes from the first 2xx response with
  content. An operation with no such response returns `Task<UnitResult<HttpError>>`. A non-success
  status, a transport failure, a timeout and an unreadable body come back as an `HttpError`; see
  [What still throws](advanced.md#what-still-throws).
- A JSON request body is `[Body] T body`, `T?` when the body is not `required`. A body with no JSON
  media type, such as XML, multipart, form or `application/octet-stream`, stays `[Body] object body`.
- A success response with no JSON media type is typed `JsonElement` and reported as
  [ZRT002](advanced.md#zrt002-schema-mapped-to-jsonelement). JSON media types are `application/json`,
  `text/json`, any `+json` type and `*/*`.

### Parameters

- `in: path` → `T name`; the route's `{token}` is rewritten to the same C# name, so `/users/{User-Id}`
  becomes `/users/{userId}`.
- `in: query` → `[Query] T name`, or `[Query(Name = "page-size")] T pageSize` when the C# name differs
  from the wire name.
- `in: header` → `[Header("Name")] T name`.
- `in: cookie` → not emitted, with warning [ZRT001](advanced.md#zrt001-cookie-parameter-not-emitted).
- A path parameter, or a `required` one, is `T`; an optional one is `T?`.
- A name that is not a valid C# identifier is made into one: `user-id` becomes `userId`, a keyword
  such as `class` is escaped as `@class`, and a clash with another parameter, `body` or `ct` gets a
  numeric suffix. Parameters declared on the path item apply to each operation unless overridden.

### Types

| Schema | C# |
|---|---|
| `integer`, `format: int32` | `int` |
| `integer`, `format: int64` | `long` |
| `number`; `format: float`; `format: decimal` | `double`; `float`; `decimal` |
| `string`; `format: date-time`, `date`, `time` | `string`; `DateTimeOffset`, `DateOnly`, `TimeOnly` |
| `string`, `format: uuid`, `uri`, `byte` | `Guid`, `Uri`, `byte[]` |
| `boolean` | `bool` |
| `array` | `List<T>` |
| `object` with only `additionalProperties` | `Dictionary<string, T>` |
| `$ref`, `enum`, an object with properties, `allOf`, `oneOf`, `anyOf` | a generated type |
| `not`, a recursive `allOf`, a schema with no type | `JsonElement`, with [ZRT002](advanced.md#zrt002-schema-mapped-to-jsonelement) |

### Models

- **Records:** `public sealed record`. A required property is `required T Name { get; init; }`, an
  optional one `T? Name { get; init; }` that is left out of the JSON when null, and a required
  `nullable: true` one `required T? Name`. Every property has `[JsonPropertyName]` with its wire name,
  and each `description` becomes a `<summary>`.
- **Names:** components keep their name, PascalCased. An inline schema is named from its path:
  `User` + `address` gives `UserAddress`, an inline request body `{Operation}Request`, an inline
  response `{Operation}Response`, a parameter enum `{Operation}{Parameter}`. A clash gets a numeric
  suffix.
- **Enums** are strict. A string enum is a C# `enum` whose members carry
  `[JsonStringEnumMemberName("wire")]`; an integer enum names its members `Value1` or takes
  `x-enum-varnames`. Both get a generated converter, and an unknown value throws `JsonException`,
  which the client returns as `HttpErrorKind.Deserialization`.
- **`allOf`** is flattened into one record. A property is required if any part requires it; the same
  property with two types stops generation with an error naming the schema and the property.
- **`oneOf`/`anyOf` with a `discriminator`** is an abstract record with `[JsonPolymorphic]` and one
  `[JsonDerivedType]` per variant; each variant derives from it. The discriminator may appear
  anywhere in the JSON object.
- **`oneOf`/`anyOf` without one** is a union wrapper, such as `PetOrError`, with one `As{Variant}`
  property per variant plus `Match` and `Switch`. Its converter keeps the variants whose JSON kind
  matches and, for objects, whose required properties are all present, then picks the one matching
  the most required properties, the first declared on a tie. For `oneOf`, more than one match is
  ambiguous and throws `JsonException`; for `anyOf` the pick wins.

### Using the generated client

```csharp
services.AddIPetStoreClient(o =>
{
    o.BaseAddress = new Uri("https://petstore.example.com/");
    o.UseSerializer(new SystemTextJsonSerializer(PetStoreClientJsonContext.Default));
});
```

The context makes serialization AOT-safe and reflection-free; see [Native AOT](native-aot.md).
````

- In "## MSBuild task", add a row to the metadata table: `| \`GenerateModels\` | No | \`false\` generates no models, so references resolve to your own DTOs (default: \`true\`) |`.
- In "## Warnings", replace "`--nowarn ZRT001`" with "`--nowarn ZRT001`, or `--nowarn ZRT001,ZRT002`".
- In "## Workflow recommendation", replace step 4 with: "4. Register the client in DI with `UseSerializer(new SystemTextJsonSerializer({Name}JsonContext.Default))`".

- [ ] **Step 2: `docs/advanced.md`: ZRT002**

After the ZRT001 subsection, before "## CancellationToken":

````markdown
#### ZRT002: Schema mapped to JsonElement

Severity: Warning.

The spec has a schema the generator cannot give a C# type, so the property, parameter or response
is typed `System.Text.Json.JsonElement` and you read it yourself. The reasons are:

- the schema uses `not`;
- its `allOf` refers back to itself;
- it has neither a `type` nor a composition;
- a success response has no JSON media type, or is binary: ZeroAlloc.Rest has no raw stream binding yet;
- `--models false` is set and the schema is an inline object, enum or composition.

Message: `Schema '#/components/schemas/Holder/properties/anything' is mapped to JsonElement, because it has neither a type nor a composition.`

To suppress it, add the code to `<NoWarn>` in the project that runs the MSBuild task, or pass
`--nowarn ZRT002` to `zeroalloc generate`.
````

- [ ] **Step 3: `docs/serialization.md`**

In "## IRestSerializer", after the interface block, add: "The interface carries no trim or AOT annotations, so generated clients call it without a suppression. An implementation that needs reflection marks its constructor with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` instead, as the reflection-based `SystemTextJsonSerializer` constructors do." If the page's interface block still shows the attributes, remove them.

Replace the body of "### System.Text.Json" after the install command with:

````markdown
Pass the `JsonSerializerContext` generated from your spec, or your own:

```csharp
services.AddIUserApi(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.UseSerializer(new SystemTextJsonSerializer(AppJsonContext.Default));
});
```

| Constructor | Metadata | AOT |
|---|---|---|
| `SystemTextJsonSerializer(JsonSerializerContext context)` | The context's, with its options | Safe |
| `SystemTextJsonSerializer(IJsonTypeInfoResolver resolver, JsonSerializerOptions? options = null)` | The resolver's, with a copy of `options` or `JsonSerializerDefaults.Web` | Safe |
| `SystemTextJsonSerializer()` | Reflection, `JsonSerializerDefaults.Web` | Warns: `[RequiresUnreferencedCode]` |
| `SystemTextJsonSerializer(JsonSerializerOptions options)` | Reflection, unless the options already have a resolver | Warns: `[RequiresUnreferencedCode]` |

With a context or resolver, a type it does not cover throws `InvalidOperationException` naming the
type; nothing falls back to reflection. `UseSerializer<SystemTextJsonSerializer>()` builds the
serializer from DI with its parameterless constructor, so it uses reflection. Content-Type:
`application/json`.
````

- [ ] **Step 4: `docs/native-aot.md`**

Replace "## Serialization and AOT" with:

````markdown
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

- MemoryPack or MessagePack with their source-generated formatters.

The parameterless and options constructors of `SystemTextJsonSerializer` use reflection and are
marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so the build tells you where.
````

Replace the checklist's second item with "- [ ] Construct serializers without reflection: no IL2026 or IL3050 warning should remain; never suppress one", and its first with "- [ ] Use a source-generated `JsonSerializerContext`, such as the one generated from your OpenAPI spec, or MemoryPack or MessagePack source generators".

- [ ] **Step 5: The cookbook recipes**

`docs/cookbook/04-native-aot-publish.md`:
- In "## 3. Use a source-generated JSON context", keep the context and add after it: "Pass it to the serializer in the next step. A client generated from an OpenAPI spec already has one; see [Recipe 02](02-openapi-import.md)."
- In "## 4. Wire everything up", change the interface method to `Task<Result<UserDto, HttpError>> GetUserAsync(int id, CancellationToken ct = default);`, add `using ZeroAlloc.Results;` and `using ZeroAlloc.Rest;`, replace `options.UseSerializer<SystemTextJsonSerializer>();` with `options.UseSerializer(new SystemTextJsonSerializer(AotJsonContext.Default));`, and the output lines with:

```csharp
var result = await api.GetUserAsync(1);
Console.WriteLine(result.IsSuccess ? $"User: {result.Value.Id} — {result.Value.Name}" : $"Error: {result.Error.Kind}");
```

- Delete "## 5. Suppress AOT warnings" entirely and renumber "## 6. Publish" and "## 7. Verify" to 5 and 6. Add at the end of Publish: "The publish should report no IL2026 or IL3050 warning. If one appears, a serializer is using reflection: construct it with the context."

`docs/cookbook/02-openapi-import.md`:
- Replace the block in "## 4. Inspect the generated interface" with the actual output for a two-operation spec. Generate it: put the recipe's spec in a temp file and run `dotnet run --project src/ZeroAlloc.Rest.Tools -- generate --spec <file> --namespace MyApp --interface IMyApi --output <out>`, then paste the interface and one record, trimming the rest with a `// ...` line.
- Add after that block: "The file also holds a record for each schema and `MyApiJsonContext`, a JSON context for all of them. Pass `--models false`, or `GenerateModels=\"false\"`, to use your own DTOs instead."
- In "## 5. Register in DI", replace `options.UseSerializer<SystemTextJsonSerializer>();` with `options.UseSerializer(new SystemTextJsonSerializer(MyApiJsonContext.Default));`.

- [ ] **Step 6: `docs/advanced.md` "What still throws" and the index pages**

In "### What still throws", add after the list: "A method generated from an OpenAPI spec is a `Result` or `UnitResult` method, so these rules apply to every generated operation."

In `docs/README.md`, add `- [Migrating to 3.0](migrating-to-v3.md)` after the 2.0 line. In the root `README.md`, change the `UseSerializer<SystemTextJsonSerializer>()` sample at line 68 to `UseSerializer(new SystemTextJsonSerializer(AppJsonContext.Default))` with a one-line `[JsonSerializable]` context above it, and extend the OpenAPI bullet at line 109 to "... generates the interface, its models and a JSON context".

- [ ] **Step 7: `docs/migrating-to-v3.md`**

````markdown
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

## `SystemTextJsonSerializer` reflection constructors are annotated

`new SystemTextJsonSerializer()` and `new SystemTextJsonSerializer(options)` now carry
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so a trimmed or AOT build warns where they
are called. Use `new SystemTextJsonSerializer(context)` or `new SystemTextJsonSerializer(resolver, options)`.

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
| `null` header | empty header | no header |

If a server relied on the old form, pass a `string` parameter formatted the way it expects.
````

- [ ] **Step 8: The ZeroAlloc.Serialisation README, a separate PR**

```bash
cd C:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
git fetch origin
git worktree add C:/wt/serialisation-rest-snippet -b docs/rest-adapter-snippet origin/main
cd C:/wt/serialisation-rest-snippet
```

In `README.md`'s "## REST Integration", `RestSerializerAdapter<T>` has no one-argument constructor; it takes the content type too. Replace the snippet with:

```csharp
services.AddSingleton<IRestSerializer>(sp =>
    new RestSerializerAdapter<OrderCreated>(sp.GetRequiredService<ISerializer<OrderCreated>>(), "application/json"));
```

and add below it: "For JSON clients generated by ZeroAlloc.Rest 3.0, prefer `new SystemTextJsonSerializer(MyApiJsonContext.Default)`: it covers every request and response type in one serializer."

```bash
git add README.md
git commit -m "$(cat <<'EOF'
docs: pass the content type to RestSerializerAdapter in the REST snippet

RestSerializerAdapter takes the serializer and a content type; the snippet passed only the
serializer and did not compile. It also points JSON clients generated by ZeroAlloc.Rest 3.0
to their generated JSON context.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
git push -u origin docs/rest-adapter-snippet
gh pr create --title "docs: pass the content type to RestSerializerAdapter in the REST snippet" --body "$(cat <<'EOF'
The REST Integration snippet called a RestSerializerAdapter constructor that does not exist: it
takes the serializer and a content type. Found while designing ZeroAlloc-Net/ZeroAlloc.Rest#351.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 9: Verify, then commit**

Check every changed Markdown file: each code sample compiles in your head against Tasks 2 to 15, each link target exists, and `grep -rn "UseSerializer<SystemTextJsonSerializer>\|IL2026;IL3050\|Suppress AOT" docs README.md` finds nothing a reader would copy into an AOT app. Run the full-suite command: docs changes cannot break it, but the commit must be green.

```bash
git add docs README.md
git commit -m "$(cat <<'EOF'
docs: document generated models, the JSON context and the 3.0 migration

The OpenAPI page describes the generated file, type rules, models, unions and the
--models option. Serialization and Native AOT pages use the context constructors, the AOT
recipe drops its warning suppression, ZRT002 is documented, and migrating-to-v3.md walks
through each breaking change.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 17: Real-world specs compile with no diagnostics

Spec §10.4: Petstore, plus a composition-heavy public subset.

**Files:**
- Create: `tests/ZeroAlloc.Rest.Tools.Tests/Specs/petstore.yaml`, `Specs/petstore-expanded.yaml`, `Specs/github-issues.yaml`
- Modify: `tests/ZeroAlloc.Rest.Tools.Tests/ZeroAlloc.Rest.Tools.Tests.csproj`
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/RealWorldSpecTests.cs`

**Interfaces:**
- Consumes: `OpenApiInterfaceGenerator.Generate` from Task 14, `GeneratedCode` from Task 11.
- Produces: fixtures Task 18 does not need; the smoke has its own spec.

- [ ] **Step 1: Fetch the Petstore specs**

The OpenAPI Initiative's examples, pinned to the 3.0.3 tag, Apache 2.0:

```bash
mkdir -p tests/ZeroAlloc.Rest.Tools.Tests/Specs
curl -fsSL https://raw.githubusercontent.com/OAI/OpenAPI-Specification/3.0.3/examples/v3.0/petstore.yaml -o tests/ZeroAlloc.Rest.Tools.Tests/Specs/petstore.yaml
curl -fsSL https://raw.githubusercontent.com/OAI/OpenAPI-Specification/3.0.3/examples/v3.0/petstore-expanded.yaml -o tests/ZeroAlloc.Rest.Tools.Tests/Specs/petstore-expanded.yaml
```

Prepend to each file one comment line naming its source URL and license: `# From <url>, Apache License 2.0, unmodified below this line.`

- [ ] **Step 2: Extract the GitHub subset**

GitHub's REST description is MIT-licensed. Its issue timeline is an `anyOf` of about twenty object schemas with overlapping required properties, which exercises union matching hard. Save this script to the scratchpad, not the repo:

```python
# extract_github_subset.py: the issue and timeline operations with every component they reach.
import json, sys, urllib.request, yaml

URL = "https://raw.githubusercontent.com/github/rest-api-description/main/descriptions/api.github.com/api.github.com.json"
OPERATIONS = [
    ("/repos/{owner}/{repo}/issues/{issue_number}", "get"),
    ("/repos/{owner}/{repo}/issues/{issue_number}/timeline", "get"),
]

spec = json.load(urllib.request.urlopen(URL))

def strip(node):
    if isinstance(node, dict):
        return {k: strip(v) for k, v in node.items() if k not in ("example", "examples")}
    if isinstance(node, list):
        return [strip(v) for v in node]
    return node

needed = {}
def collect(node):
    if isinstance(node, dict):
        ref = node.get("$ref")
        if isinstance(ref, str) and ref.startswith("#/components/"):
            _, _, kind, name = ref.split("/", 3)
            if name not in needed.setdefault(kind, set()):
                needed[kind].add(name)
                collect(strip(spec["components"][kind][name]))
        for value in node.values():
            collect(value)
    elif isinstance(node, list):
        for value in node:
            collect(value)

paths = {}
for path, method in OPERATIONS:
    item = spec["paths"][path]
    kept = {k: v for k, v in item.items() if k == "parameters"}
    kept[method] = strip(item[method])
    paths[path] = kept
    collect(kept)

components = {kind: {n: strip(spec["components"][kind][n]) for n in sorted(names)} for kind, names in needed.items()}
subset = {"openapi": spec["openapi"], "info": {"title": "GitHub issues subset", "version": spec["info"]["version"]},
          "paths": paths, "components": components}
sys.stdout.write("# Subset of github/rest-api-description, MIT License. Extracted for ZeroAlloc.Rest tests.\n")
sys.stdout.write(yaml.safe_dump(subset, sort_keys=False, allow_unicode=True))
```

```bash
pip install pyyaml
python "$SCRATCHPAD/extract_github_subset.py" > tests/ZeroAlloc.Rest.Tools.Tests/Specs/github-issues.yaml
```

Record the GitHub description's commit SHA in the header line: `git ls-remote https://github.com/github/rest-api-description refs/heads/main`.

- [ ] **Step 3: Copy the specs to the test output**

In the test csproj:

```xml
  <ItemGroup>
    <None Include="Specs/*.yaml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 4: Write the test**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/RealWorldSpecTests.cs
using Xunit;
using Xunit.Abstractions;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §10.4: public specs generate a client that compiles with no diagnostics. ZRT002 warnings are
// allowed: they are the tool reporting what it could not type, not a broken build.
public class RealWorldSpecTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("petstore.yaml", "IPetstoreApi")]
    [InlineData("petstore-expanded.yaml", "IPetstoreExpandedApi")]
    [InlineData("github-issues.yaml", "IGitHubIssuesApi")]
    public void Spec_GeneratesAClientThatCompilesClean(string file, string interfaceName)
    {
        var warnings = new List<OpenApiWarning>();
        var code = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Specs", file)), "RealWorld", interfaceName, warnings, GenerationOptions.Default);

        foreach (var warning in warnings)
            output.WriteLine($"{warning.Code}: {warning.Message}");
        Assert.All(warnings, w => Assert.Contains(w.Code, new[] { "ZRT001", "ZRT002" }));
        GeneratedCode.Compile(code).AssertClean();
    }

    [Fact]
    public void GitHubTimeline_IsAUnionOfItsEventTypes()
    {
        var code = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Specs", "github-issues.yaml")), "RealWorld", "IGitHubIssuesApi");

        Assert.Contains("public sealed record TimelineIssueEvents", code);
        Assert.Contains("internal sealed class TimelineIssueEventsConverter", code);
    }
}
```

- [ ] **Step 5: Run it and fix what it finds**

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~RealWorldSpecTests" --logger "console;verbosity=detailed"`

Expected: PASS. Real specs find cases the unit tests did not. Fix each in the unit that owns it, with a unit test reproducing it first, in its own `fix:` commit before this task's commit. Likely ones, and where they belong:
- SYSLIB1031, two context registrations with the same generated property name, for example `List<Label>` and a model named `ListLabel`: `JsonContextEmitter` gives generic registrations a `TypeInfoPropertyName` built from `ReadableName`, `LabelList`.
- CS0102 or CS0542 from a property name: `SchemaModelBuilder.Properties` reserves it.
- An enum value that sanitises to an existing member, such as `+1` and `-1`: already suffixed by `Unique`; if not, `BuildEnum`.
- A `nullable: true` schema with `allOf` of one `$ref`: already unwrapped by `TypeMapper.Unwrap`.

Never filter a diagnostic out of `GeneratedCode`.

- [ ] **Step 6: Run the full suite, then commit**

```bash
git add -A tests/ZeroAlloc.Rest.Tools.Tests
git commit -m "$(cat <<'EOF'
test: generate and compile clients for Petstore and a GitHub subset

Petstore, Petstore expanded and GitHub's issue and timeline operations generate clients
that compile with the Rest and System.Text.Json generators and report no diagnostics. The
GitHub timeline is an anyOf of about twenty events, which exercises union matching.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 18: AOT smoke with a generated client

Spec §10.5: a generated client and its context, published with `PublishAot`, 0 trim or AOT warnings, and a real request against a stub server.

**Files:**
- Create: `samples/ZeroAlloc.Rest.AotSmoke/petstore.yaml`
- Create: `samples/ZeroAlloc.Rest.AotSmoke/Generated/IPetStoreClient.g.cs`, generated
- Create: `samples/ZeroAlloc.Rest.AotSmoke/StubServer.cs`
- Modify: `samples/ZeroAlloc.Rest.AotSmoke/Program.cs`, `ZeroAlloc.Rest.AotSmoke.csproj`
- Modify: `.github/workflows/ci.yml`, the `aot-smoke` job
- Test: `tests/ZeroAlloc.Rest.Tools.Tests/AotSmokeClientTests.cs`

**Interfaces:**
- Consumes: the tool from Task 15, `SystemTextJsonSerializer(JsonSerializerContext)` from Task 3.
- Produces: `ZeroAlloc.Rest.AotSmoke.PetStore.IPetStoreClient`, `PetStoreClientClient`, `PetStoreClientJsonContext`.

- [ ] **Step 1: Write the spec**

```yaml
# samples/ZeroAlloc.Rest.AotSmoke/petstore.yaml
openapi: 3.0.0
info:
  title: Pet store for the AOT smoke
  version: "1"
paths:
  /pets/{petId}:
    parameters:
      - name: petId
        in: path
        required: true
        schema:
          type: integer
          format: int64
    get:
      operationId: getPet
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Pet'
    delete:
      operationId: deletePet
      responses:
        '204':
          description: Deleted
  /pets:
    post:
      operationId: addPet
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/Pet'
      responses:
        '201':
          description: Created
          content:
            application/json:
              schema:
                oneOf:
                  - $ref: '#/components/schemas/Pet'
                  - $ref: '#/components/schemas/Error'
components:
  schemas:
    Pet:
      type: object
      required: [id, name]
      properties:
        id:
          type: integer
          format: int64
        name:
          type: string
        status:
          type: string
          enum: [available, sold]
    Error:
      type: object
      required: [code]
      properties:
        code:
          type: string
```

- [ ] **Step 2: Write the failing freshness test**

```csharp
// tests/ZeroAlloc.Rest.Tools.Tests/AotSmokeClientTests.cs
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// The AOT smoke compiles a client generated from its petstore.yaml, checked in so the CI job needs
// no tool run. This fails when the checked-in file is not what the tool generates now.
public class AotSmokeClientTests
{
    [Fact]
    public void CheckedInClient_MatchesTheGenerator()
    {
        var sample = Path.Combine(RepositoryRoot(), "samples", "ZeroAlloc.Rest.AotSmoke");
        var expected = OpenApiInterfaceGenerator.Generate(
            File.ReadAllText(Path.Combine(sample, "petstore.yaml")), "ZeroAlloc.Rest.AotSmoke.PetStore", "IPetStoreClient");
        var generatedPath = Path.Combine(sample, "Generated", "IPetStoreClient.g.cs");

        Assert.True(File.Exists(generatedPath), "Generate it: " + Command);
        Assert.True(
            string.Equals(Normalize(expected), Normalize(File.ReadAllText(generatedPath)), StringComparison.Ordinal),
            "The AOT smoke's generated client is stale. Regenerate it: " + Command);
    }

    private const string Command =
        "dotnet run --project src/ZeroAlloc.Rest.Tools -- generate --spec samples/ZeroAlloc.Rest.AotSmoke/petstore.yaml "
        + "--namespace ZeroAlloc.Rest.AotSmoke.PetStore --interface IPetStoreClient "
        + "--output samples/ZeroAlloc.Rest.AotSmoke/Generated/IPetStoreClient.g.cs";

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Rest.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("ZeroAlloc.Rest.slnx not found above " + AppContext.BaseDirectory);
    }
}
```

Run: `dotnet test tests/ZeroAlloc.Rest.Tools.Tests -c Release --filter "FullyQualifiedName~AotSmokeClientTests"`
Expected: FAIL, "Generate it: ...".

- [ ] **Step 3: Generate the client**

Run the command from the test's `Command` constant from the repository root. Run the Step 2 test again. Expected: PASS.

- [ ] **Step 4: Write the stub server**

A real loopback socket, so the request goes through `HttpClient`'s socket handler under ILC:

```csharp
// samples/ZeroAlloc.Rest.AotSmoke/StubServer.cs
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Rest.AotSmoke;

// A minimal HTTP/1.1 server on loopback: it answers each connection with the next canned response
// and records the request line and body it received.
internal sealed class StubServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public StubServer() => _listener.Start();

    public Uri BaseAddress => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    public string LastRequest { get; private set; } = "";

    public async Task ServeAsync(int status, string json, CancellationToken ct)
    {
        using var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        var stream = client.GetStream();
        LastRequest = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
        var body = Encoding.UTF8.GetBytes(json);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Stub\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    // Reads the head, then Content-Length bytes of body.
    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var text = new StringBuilder();
        int headEnd;
        while ((headEnd = text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        var head = headEnd < 0 ? text.ToString() : text.ToString(0, headEnd);
        var length = 0;
        foreach (var line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line.AsSpan(15).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        while (headEnd >= 0 && text.Length - (headEnd + 4) < length)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        return text.ToString();
    }

    public void Dispose() => _listener.Stop();
}
```

- [ ] **Step 5: Call the generated client from the smoke**

Add to the csproj: `<ProjectReference Include="..\..\src\ZeroAlloc.Rest.SystemTextJson\ZeroAlloc.Rest.SystemTextJson.csproj" />`. Make the smoke's own warnings strict too: add `IL2104;IL3053;IL2046;IL3051` to its `WarningsAsErrors` list. In `Program.cs`, before `Console.WriteLine("AOT smoke: PASS");`:

```csharp
// Spec §10.5: a client generated from petstore.yaml, serializing through its generated context
// over a real loopback connection, with no reflection anywhere under ILC.
{
    using var server = new StubServer();
    using var http = new System.Net.Http.HttpClient { BaseAddress = server.BaseAddress };
    ZeroAlloc.Rest.AotSmoke.PetStore.IPetStoreClient pets = new ZeroAlloc.Rest.AotSmoke.PetStore.PetStoreClientClient(
        http, new ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer(ZeroAlloc.Rest.AotSmoke.PetStore.PetStoreClientJsonContext.Default));
    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

    var serving = server.ServeAsync(200, "{\"id\":7,\"name\":\"Rex\",\"status\":\"sold\"}", timeout.Token);
    var pet = await pets.GetPetAsync(7).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!pet.IsSuccess || !string.Equals(pet.Value.Name, "Rex", StringComparison.Ordinal) || pet.Value.Status != ZeroAlloc.Rest.AotSmoke.PetStore.PetStatus.Sold
        || !server.LastRequest.StartsWith("GET /pets/7 ", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("AOT smoke: FAIL — the generated client should read a Pet over a real connection");
        return 1;
    }

    serving = server.ServeAsync(201, "{\"code\":\"duplicate\"}", timeout.Token);
    var added = await pets.AddPetAsync(new ZeroAlloc.Rest.AotSmoke.PetStore.Pet { Id = 8, Name = "Tom" }).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!added.IsSuccess || !string.Equals(added.Value.AsError?.Code, "duplicate", StringComparison.Ordinal)
        || !server.LastRequest.EndsWith("{\"id\":8,\"name\":\"Tom\"}", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("AOT smoke: FAIL — the body and the oneOf response should round-trip through the context");
        return 1;
    }

    serving = server.ServeAsync(200, "{\"id\":7,\"name\":\"Rex\",\"status\":\"lost\"}", timeout.Token);
    var unknown = await pets.GetPetAsync(7).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!unknown.IsFailure || unknown.Error.Kind != HttpErrorKind.Deserialization)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — an unknown enum value should be a Deserialization error");
        return 1;
    }

    serving = server.ServeAsync(204, "", timeout.Token);
    var deleted = await pets.DeletePetAsync(7).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!deleted.IsSuccess)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a 204 should be a UnitResult success");
        return 1;
    }
}
```

Update the comment block at the top of `Program.cs`: the smoke now fires real requests.

- [ ] **Step 6: Publish and run**

Run the AOT smoke command. Expected: `0`, then `AOT smoke: PASS`. Any IL warning is a real finding: fix the code that causes it, never suppress it.

- [ ] **Step 7: Make CI fail on any IL warning**

In `.github/workflows/ci.yml`, replace the `aot-smoke` job's "Publish AOT" step with:

```yaml
      - name: Publish AOT
        # The smoke must publish with no trim or AOT warning at all; see #351.
        run: |
          set -o pipefail
          dotnet publish samples/ZeroAlloc.Rest.AotSmoke/ZeroAlloc.Rest.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out 2>&1 | tee publish.log
          if grep -E "warning IL[0-9]+" publish.log; then
            echo "::error::The AOT smoke has trim or AOT warnings."
            exit 1
          fi
```

Update the job's leading comment: it now also sends real requests through a generated client and its context.

- [ ] **Step 8: Run the full suite, then commit**

```bash
git add -A samples tests/ZeroAlloc.Rest.Tools.Tests .github/workflows/ci.yml
git commit -m "$(cat <<'EOF'
test: publish a generated client in the AOT smoke with no IL warnings

The smoke compiles a client generated from its petstore.yaml and calls it over a real
loopback connection through the generated JSON context: a typed read, a body and a oneOf
response, an unknown enum as a Deserialization error, and a UnitResult delete. CI now
fails on any IL warning, and a Tools test fails when the checked-in client is stale.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 19: Ship

**Files:** none in the repo.

**Interfaces:**
- Consumes: the branch with Tasks 1 to 18.
- Produces: the PR, the follow-up issue, and a release that is ready to cut.

- [ ] **Step 1: File the follow-up issue for binary and non-JSON content**

Design decision 4 is a documented gap, so it is tracked. Check for an existing issue first: `gh issue list --search "stream body" --state open`. If none:

```bash
gh issue create --title "Generated clients: raw Stream bodies and non-JSON responses" --body "$(cat <<'EOF'
Follow-up to #351.

What is wrong: ZeroAlloc.Rest has no raw Stream binding for request or response bodies. The
OpenAPI generator therefore keeps `[Body] object body` for binary and other non-JSON request
bodies, and types a non-JSON or binary success response as JsonElement with ZRT002. At run time
the JSON serializer then fails to read such a response, which comes back as a Deserialization
error.

Where: src/ZeroAlloc.Rest.Tools.Shared/OperationEmitter.cs, BodyParameter and ReturnType;
the Rest source generator's request and response emission in ClientEmitter.cs.

Why it matters: file uploads and downloads, `format: binary` under application/octet-stream or
image/*, cannot be called through a generated client.

Fix: teach the source generator a `Stream` body, sent as StreamContent with the media type, and a
`Stream` success type, copied out of the response before it is disposed. Then have the tool emit
`Stream` for binary content, which TypeMapper already maps. This needs a design decision on the
response side: buffering versus handing ownership of the response to the caller.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 2: Check the release order**

```bash
gh pr view 328 --json state,mergedAt
gh release view v2.2.0 --json tagName,publishedAt
curl -fsSL https://api.nuget.org/v3-flatcontainer/zeroalloc.rest/index.json | grep -c '"2.2.0"'
```

Expected: #328 merged, the v2.2.0 release published, and `1` from NuGet. A green release workflow is not proof; the NuGet index is. If 2.2.0 has not shipped, do not merge this PR; tell the maintainer.

- [ ] **Step 3: Sweep for other 3.0 breaking candidates**

Spec §9: one 3.0, everything in.

```bash
grep -rn "\[Obsolete" src --include=*.cs
gh issue view 336 --json state,title
gh issue list --label breaking --state open
```

List each `[Obsolete]` member and open breaking issue, #336 included, in the PR description under "Also for 3.0", with its status. Each is its own PR, merged before the 3.0 release PR; this plan does not implement them.

- [ ] **Step 4: Open the PR**

The PR is squash-merged, which keeps only the title's changelog entry, so the body carries `BEGIN_COMMIT_OVERRIDE` with every entry, and the one `feat!` carries the footer, copied from the Task 14 commit.

```bash
git push -u origin feat/351-openapi-models
gh pr create --title "feat!: generate OpenAPI models and AOT-safe serialization for Rest 3.0" --body "$(cat <<'EOF'
Closes #351. Design: docs/plans/2026-09-27-openapi-models-design.md; plan:
docs/plans/2026-09-27-openapi-models.md, whose AOT decision record holds the prototype numbers.

## Summary
- The OpenAPI tool generates models, typed parameters and bodies, Result returns and a JSON context.
- IRestSerializer is AOT-neutral; SystemTextJsonSerializer takes a context or a resolver.
- The Rest generator supports UnitResult and writes route, query and header values invariantly.
- The AOT smoke calls a generated client over a real connection with 0 IL warnings.

## Also for 3.0
<the Step 3 list>

BEGIN_COMMIT_OVERRIDE
feat!: generate models, typed members and Result returns from OpenAPI specs

<the Task 14 commit body and BREAKING CHANGE footer, verbatim>

feat: make IRestSerializer AOT-neutral and drop the generated trim suppression
feat: add context and resolver constructors to SystemTextJsonSerializer
feat: support UnitResult returns in generated clients
fix: write route, query and header values in their wire format
feat: add --models and GenerateModels to keep hand-written DTOs
docs: document generated models, the JSON context and the 3.0 migration
END_COMMIT_OVERRIDE

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Replace both `<...>` markers with the real text before running it. Keep every body line at most 100 characters and free of nested parentheses.

- [ ] **Step 5: After the merge, confirm the release PR counted it**

Open the release-please PR and check its changelog lists the `feat!` under a breaking section, the proposed version is 3.0.0, and each override entry appears. If one is missing, edit the merged PR's body and re-run the release-please workflow. Comment on #351 with what shipped and in which version once the release is on NuGet.

---

## Spec coverage

| Spec | Requirement | Task |
|---|---|---|
| §1, §2 | Models for every referenced schema | 7 to 10, 14 |
| §1, §2 | Typed request bodies and parameters, `format`, enums, nullability | 6, 14 |
| §2, §4 | STJ source-gen context covering every generated type | 11, 13, 14 |
| §2, §7 | AOT-safe context and resolver constructors | 3 |
| §2, §6 | `Result<T, HttpError>` returns; failures as results | 4, 14 |
| §2 non-goals | OpenAPI 3.1, XML, multipart and form bodies, a Serialisation multi-type adapter | Not implemented; non-JSON bodies keep `object`, design decision 4 |
| §3 | Sealed records with `required` and `init` | 11 |
| §3 | One file, one namespace, interface then models then context, `<auto-generated/>` | 14 |
| §3, §5.3 | Strict enums, unknown value is a `Deserialization` error | 11, 14, 18 |
| §3, §8 | Models on by default, `--models false`, `GenerateModels="false"` | 14, 15 |
| §3, §9 | Breaking, `feat!`, Rest 3.0.0 | 14, 19 |
| §4 | `SchemaModelBuilder`, `TypeMapper`, `ModelEmitter`, `JsonContextEmitter`, `OpenApiInterfaceGenerator`; value-equal intermediate model, pure emitters | 6 to 14 |
| §5.1 | Every row of the type table, parameters through the same mapper | 6, 14 |
| §5.2 | Required, optional, required-nullable, wire names, sanitised names, inline naming, `readOnly`/`writeOnly` ignored, descriptions | 7, 11 |
| §5.4 | `allOf` flattened, required if any part requires it, conflicts are generation errors | 8 |
| §5.5 | Discriminated hierarchies, mapping values or schema names | 9, 12 |
| §5.6 | Union wrappers, converter algorithm, oneOf ambiguity, anyOf best match, writing exactly one, primitives by JSON kind, converter emitted as source | 10, 13 |
| §5.7 | ZRT002 for `not`, recursive `allOf`, no type; suppressible | 6, 7, 8, 16 |
| §6 | `UnitResult<HttpError>` and mapped `UnitResult<TError>` in the source generator, with tests and `docs/advanced.md` | 4 |
| §6 | "What still throws" unchanged and stated in the migration guide | 4, 16 |
| §7 | Resolve `JsonTypeInfo<T>` only; unregistered type throws naming `T` and the context | 2, 3 |
| §7 | AOT annotations: prototype, measure, record the choice | 1, 2 |
| §7 | Docs: AOT recipe, native-aot, serialization, Serialisation README PR | 16 |
| §9 | Migration guide | 16 |
| §9 | Release 2.2.0 first; sweep for `[Obsolete]` and other breaks, #336 included | 19 |
| §10.1 | `TypeMapper` per row plus nullability; `SchemaModelBuilder` naming, inline, collisions, allOf, discriminators, unions | 6 to 10 |
| §10.2 | Compile tests with the Rest generator and analyzers, 0 diagnostics | 11 to 14 |
| §10.3 | Round trips: records, enums, end-to-end `Deserialization`, polymorphism, unions with every JSON kind and oneOf versus anyOf | 11 to 14 |
| §10.4 | Petstore and a composition-heavy GitHub subset | 17 |
| §10.5 | AOT smoke with the generated context, 0 warnings, a real request | 18 |
| §10.6 | Serializer constructor tests and the not-registered message | 3 |
| §11 | Deterministic, documented union matching; large files written only on change | 13, 16; unchanged task behaviour |

`readOnly` and `writeOnly` need no code: the builder never reads them, which is what §5.2 asks.
