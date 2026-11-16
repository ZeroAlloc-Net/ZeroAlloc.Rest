# AGENTS.md: ZeroAlloc.Rest

A compile-time REST client generator for .NET: the consumer writes an HTTP API as a C# interface marked
`[ZeroAllocRestClient]`, and a Roslyn source generator emits the client. It ships as NuGet packages: the core
`ZeroAlloc.Rest` (which bundles the generator), `.DependencyInjection`, the serializers `.SystemTextJson`,
`.MemoryPack` and `.MessagePack`, `.Resilience`, the provider clients `.Anthropic` and `.Ollama`, the
OpenAPI importer `.Tools` (a dotnet tool) and `.Tools.MSBuild`, plus the standalone `.Generator` package.

## Layout
- `src/ZeroAlloc.Rest.Generator` targets `netstandard2.0` and is not a runtime library. Its output is the
  hot path, so judge a change by the emitted code in `ClientEmitter.cs` and `DiEmitter.cs`, not by the generator
  itself. `ZeroAlloc.Rest.csproj` packs the generator DLL under `analyzers/dotnet/cs`.
- Runtime packages target `net8.0;net10.0;net11.0`. `ZeroAlloc.Rest.Tools` targets `net10.0;net11.0`,
  `ZeroAlloc.Rest.Tools.MSBuild` targets `net10.0` only.
- `tests/ZeroAlloc.Rest.Integration.Tests` holds the allocation gates, compiled against generated clients.
  `tests/ZeroAlloc.Rest.Generator.Tests` compiles generator output against the BCL reference set of the
  target framework in `TargetFrameworkReferences.cs`.
- `samples/*.AotSmoke` are the NativeAOT smoke apps that CI publishes. They are not packaged.
- `tests/ZeroAlloc.Rest.Benchmarks` targets `net10.0` only, not the three LTS frameworks of the org rule.
- `apicompat-suppressions.xml` is empty on purpose: it holds only annotated, intentional API breaks.

## Build and test
Prerequisites: the .NET 11 SDK from `global.json` (rollForward `latestMinor`) builds every target. Tests run on
the runtime of their target framework: .NET 8 for `net8.0`, .NET 10 for `net10.0`, .NET 11 for `net11.0`.
`ZeroAlloc.Rest.Tools.Tests` and `ZeroAlloc.Rest.DuplicateGeneratorTests` target only `net10.0` and `net11.0`.
The benchmarks need the .NET 10 runtime.

CI job `build` in `ci.yml`:
```bash
dotnet tool restore
VERSION=$(dotnet tool run dotnet-gitversion /output json /showvariable SemVer)
dotnet restore ZeroAlloc.Rest.slnx
dotnet build ZeroAlloc.Rest.slnx --no-restore -c Release -p:Version=$VERSION
dotnet test ZeroAlloc.Rest.slnx --no-build -c Release --logger "trx" --results-directory ./test-results --collect "XPlat Code Coverage" --settings coverage.runsettings
dotnet run --no-build -c Release --project samples/ZeroAlloc.Rest.CoreOnly.AotSmoke
dotnet pack ZeroAlloc.Rest.slnx --no-build -c Release -p:Version=$VERSION -o ./nupkg
```
The same job runs a coverage gate between the test and the smoke run: the `coverage-gate` action of
`MarcelRoozekrans/ci-baseline` merges `test-results/**/coverage.cobertura.xml` and fails below its line-coverage
floor. The commands above produce those reports but do not reproduce the gate. `pack` fails with `ZAVERSION001`
or `ZAVERSION002` when the version was not passed to the build that produced the assemblies, so `-p:Version`
goes on both `build` and `pack`.

CI jobs `aot-smoke`, `aot-smoke-clients` and `aot-smoke-core` publish `samples/ZeroAlloc.Rest.AotSmoke`,
`.Clients.AotSmoke` and `.CoreOnly.AotSmoke` with NativeAOT (clang and `zlib1g-dev` needed) and run the binary:
```bash
set -o pipefail
dotnet publish samples/ZeroAlloc.Rest.AotSmoke/ZeroAlloc.Rest.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out 2>&1 | tee publish.log
if grep -E "warning IL[0-9]+" publish.log; then echo "trim or AOT warnings"; exit 1; fi
timeout --kill-after=10s 120s ./aot-out/ZeroAlloc.Rest.AotSmoke
```
For the other two, swap the project, the output folder and the executable name
(`ZeroAlloc.Rest.Clients.AotSmoke`, `ZeroAlloc.Rest.CoreOnly.AotSmoke`). Jobs `aot-smoke-memorypack` and
`aot-smoke-messagepack` publish the same way but accept IL warnings whose member starts with `MemoryPack.` or
`MessagePack.` (upstream warnings, issues #357 and #374) and fail on any other IL warning, including one from
`ZeroAlloc.Rest.MemoryPack` or `.MessagePack` themselves. Do not widen that filter.

Job `release-tracking` (release PRs only) and `api-compat` are shared workflows from `ZeroAlloc-Net/.github`.
Workflow `benchmarks.yml` runs `smoke` on PRs through the shared `benchmark-smoke.yml`; `full` is manual:
```bash
dotnet build tests/ZeroAlloc.Rest.Benchmarks -c Release
dotnet run --project tests/ZeroAlloc.Rest.Benchmarks -c Release --no-build -- --filter "${FILTER:-*}"
```

Fast inner loop, no CI job. Run on one target framework, in Release because the allocation gates skip in Debug:
```bash
dotnet test tests/ZeroAlloc.Rest.Generator.Tests -c Release -f net10.0 --filter "FullyQualifiedName~GeneratorHintNameTests"
dotnet test tests/ZeroAlloc.Rest.Integration.Tests -c Release -f net8.0 --filter "FullyQualifiedName~GetCallAllocationTests"
```

## Conventions and gotchas
- Allocation budgets are exact byte counts from `AllocationGate`, measured on .NET 10 x64 in Release. The
  `net8.0` runs add `RuntimeAllocation.HttpClientSendExtraBytes`. The README quotes the 1,688 B figure of a
  generated GET: a change to a budget updates the README and `docs/benchmarks.md` in the same PR.
- A generated call is not 0 B: the `HttpClient` request and response objects are inherent allocations,
  which the org rule allows when documented. The promise tested by
  `GeneratedGet_SmallDto_AllocatesNoMoreThanHandWritten` is that a generated call allocates no more than
  the equivalent hand-written `HttpClient` call.
  Measured calls must complete synchronously (`SynchronousCall.Require`), and the tests sit in the
  `rest-telemetry-non-parallel` collection so no `MeterListener` runs beside them.
- Every public API change in a runtime project edits that project's `PublicAPI.Unshipped.txt`. RS0016 and
  RS0017 are build errors. Never edit `PublicAPI.Shipped.txt` or `AnalyzerReleases.Shipped.md`: the release PR
  moves entries there. A new generator diagnostic goes in `AnalyzerReleases.Unshipped.md`.
- `TreatWarningsAsErrors` is on, with Meziantou and Roslynator analyzers. Fix the warning. A project-level
  `NoWarn` such as `MA0048` exists only where the file layout is intentional.
- Generated code compiles in consumers on `net8.0`, so it must not use an API newer than net8.0. The net8.0
  run of `Generator.Tests` catches that.
- On `net8.0`, `ZeroAlloc.Rest.SystemTextJson` pulls the System.Text.Json 10 package so both targets serialize
  alike. Test projects that reference WireMock.Net lift `Microsoft.AspNetCore.WebUtilities` to a patched
  version in `Directory.Packages.props`; keep that pin until WireMock drops the 2.x ASP.NET Core packages.
- `Microsoft.Build.Framework` and `Microsoft.Build.Utilities.Core` stay below 18.10 (`renovate.json` holds
  them), because `Tools.MSBuild` targets `net10.0` and the .NET 10 SDK ships MSBuild 18.9.
- Package versions live in `Directory.Packages.props`; a `PackageReference` in a csproj carries no version.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
