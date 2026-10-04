# Changelog

## [3.2.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v3.2.0...v3.2.1) (2026-10-04)


### Performance Improvements

* stop generated calls allocating when nothing listens ([#407](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/407)) ([d8090d9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/d8090d998b32bd05dc4d8f209fe74f3b01306118)), closes [#406](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/406)

## [3.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v3.1.0...v3.2.0) (2026-09-30)


### Features

* generate Stream bodies for binary content in OpenAPI clients ([168c9bb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/168c9bb040feb5249b6b393d178e7e034eec5d40))
* opt-in StreamResponses reads response bodies without buffering them in HttpClient ([281db7c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/281db7c9e16e325217b1518be20cab240082d06f))
* send and return raw Stream bodies from generated clients, with [Body] ContentType ([168c9bb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/168c9bb040feb5249b6b393d178e7e034eec5d40))


### Bug Fixes

* generate clients for nested interfaces and report ZRA006 for unsupported ones ([#396](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/396)) ([4216be8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4216be8d6d7b8011c03bdf0c80ba1a2833d22fd4)), closes [#394](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/394)
* qualify generated file names so same-named clients do not crash the generator ([#393](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/393)) ([9fdef4a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/9fdef4abae491fcd21517cb6af1eff752494efb4)), closes [#392](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/392)
* qualify the named HttpClient when two clients would share its name ([#398](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/398)) ([c420415](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c420415ff46eeed7d5eeabe2148047ec712bde34)), closes [#395](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/395)
* run the route template checks on generated code ([#402](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/402)) ([59c4267](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/59c426725446aabab99d014fee7fb08becb4ef68)), closes [#346](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/346)


### Performance Improvements

* MemoryPackRestSerializer reads and writes bodies through cleared pooled buffers ([281db7c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/281db7c9e16e325217b1518be20cab240082d06f))


### Tests

* measure the streamed body saving by growth, so it holds in Debug ([#400](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/400)) ([9b6a10a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/9b6a10a1f7f90c8bd13acf143d9c2e9dcbf534f3)), closes [#397](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/397)

## [3.1.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v3.0.0...v3.1.0) (2026-09-28)


### Features

* add an AOT-safe resolver constructor to MessagePackRestSerializer ([#377](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/377)) ([1146e16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/1146e1616824017228c2ecc0e4fd8466614ed525)), closes [#361](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/361)


### Bug Fixes

* count the required properties a wrapper adds around a union variant ([eb54677](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/eb5467730077e39ddecef89cad2d6907fcf2621a))
* generate structurally identical inline unions once ([eb54677](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/eb5467730077e39ddecef89cad2d6907fcf2621a))
* leave the span status Unset when the caller cancels a request ([#379](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/379)) ([931a98e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/931a98e69a28553263fb703cdfa0bda5dcb289c7)), closes [#376](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/376)
* record rest.request_duration_ms once when a call fails after the response ([#375](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/375)) ([8f7f954](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/8f7f9541e9a6a18a260b3152bf67359f8f075024))
* report ZRA diagnostics at source locations that #pragma can suppress ([#384](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/384)) ([2cf6443](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/2cf6443419c5efcde631b252a131ae8e77d85e45))
* send one header value per element for a collection-typed [Header] parameter ([#373](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/373)) ([5a995d8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/5a995d8131b472d019ef27dcead09c7c0ec4af24)), closes [#356](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/356)
* tell apart union variants by single-value enums and report ZRT003 ([#380](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/380)) ([e4fb684](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/e4fb68402dc9d7d645d742dd0842b16660369eb5))
* trace and time request body serialization failures ([#382](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/382)) ([a73e81d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/a73e81ddedf9bb145a905356b4b8e1ec50e10596)), closes [#378](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/378)


### Performance Improvements

* serialize request bodies into a pooled buffer ([#368](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/368)) ([3ac43c5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/3ac43c546a7b5fb7895ef37ad8af0f8e6cdcd2ab))


### Tests

* cover value types in the AOT smoke ([#383](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/383)) ([814c2bf](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/814c2bf4b6879a730e60fcf5a99bfeb0aceab348))

## [3.0.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v2.2.0...v3.0.0) (2026-09-28)


### ⚠ BREAKING CHANGES

* ZeroAlloc.Rest no longer depends on Microsoft.Extensions.Http, ZeroAlloc.Serialisation or ZeroAlloc.ValueObjects, and the generated Add{I} registrations, AddZeroAllocClient, AddRestSerializer, ZeroAllocClientOptions with UseSerializer, IGeneratedRestClient and the per-client serializers now live in the new ZeroAlloc.Rest.DependencyInjection package under the same ZeroAlloc.Rest namespace, so apps that register clients through dependency injection add that package and change nothing else, apps on ZeroAlloc.Rest.Resilience get it transitively once Resilience is on 3.0, and RestSerializerAdapter is removed; see docs/migrating-to-v3.md.
* regenerating a client changes its shape, and the runtime changes with it. Generated methods return Task of Result<T, HttpError>, or Task of UnitResult<HttpError> when no 2xx response has a schema, instead of Task of T or Task; a non-success status, transport failure, timeout or unreadable body comes back as an HttpError, while caller cancellation and an unserializable request body still throw. An empty or null success body that T cannot hold is now an error, a Deserialization HttpError for a Result method and an exception for a Task of T method; declare T? to accept one. Request bodies are typed from their schema instead of object, parameters follow type and format so int64 is long, date-time is DateTimeOffset, uuid is Guid and enums are enums, and optional query and header parameters are nullable. Models are generated by default and can clash with hand-written DTOs; pass --models false or GenerateModels="false" to keep your own. The generated file uses global::-qualified names and enables nullable annotations. IRestSerializer and the in-repo serializers no longer carry RequiresDynamicCode or RequiresUnreferencedCode and generated clients no longer carry UnconditionalSuppressMessage, so a custom serializer must drop those attributes; the parameterless and options SystemTextJsonSerializer constructors now carry them, so pass a JsonSerializerContext instead, and the MessagePackRestSerializer constructors carry them too. MemoryPackRestSerializer serves only registered types plus MemoryPack built-ins and unmanaged types, so register every MemoryPackable type, such as types.Add<User>. Route, query and header values are written invariantly: ISO 8601 dates and times, true and false, and enum wire names. See docs/migrating-to-v3.md.

### Features

* add --models and GenerateModels to keep hand-written DTOs ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))
* add context and resolver constructors to SystemTextJsonSerializer ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))
* generate models, typed members and Result returns from OpenAPI specs ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))
* make IRestSerializer AOT-neutral and drop the generated trim suppression ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))
* make MemoryPackRestSerializer AOT-safe through explicit type registration ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))
* move DI and IHttpClientFactory integration to ZeroAlloc.Rest.DependencyInjection ([596dd97](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/596dd9725a02487ec5e1f6fcea1b3d8980715d24))
* support UnitResult returns in generated clients ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))


### Bug Fixes

* annotate the MessagePackRestSerializer reflection constructors ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))
* reject an empty or null success body that T does not accept ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))
* write route, query and header values in their wire format ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))


### Documentation

* document generated models, the JSON context and the 3.0 migration ([f8ae939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f8ae93999b904ddc1b737868f0aa736e7b2b883b))


### Dependencies

* drop the unused ZeroAlloc.ValueObjects and ZeroAlloc.Serialisation dependencies from ZeroAlloc.Rest ([596dd97](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/596dd9725a02487ec5e1f6fcea1b3d8980715d24))

## [2.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v2.1.0...v2.2.0) (2026-09-27)


### Features

* allow HTTP method attributes without a path ([#332](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/332)) ([7561bbd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/7561bbdf00afa0b21021f4d39fa14e3572043155)), closes [#318](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/318)
* ship the MSBuild task as ZeroAlloc.Rest.Tools.MSBuild ([c3a0b68](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c3a0b68182ace898dda3d1330d4f4a5c985618f6))
* warn with ZRA005 when a route template and its route parameters do not match ([02d3d66](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/02d3d664865cbe517f1ee4ad6a2a06aa009087ed))


### Bug Fixes

* a non-nullable value-type [Header] parameter such as int no longer fails to compile with CS0023 ([4009100](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4009100b7e12115f029542cf904e7abdfec55899))
* bind OpenAPI path parameters to their route tokens and skip cookie parameters ([#344](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/344)) ([9de8ec5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/9de8ec5c07c7c712aad3e07442311b5c92a85ae3)), closes [#338](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/338)
* escape route text and keyword parameter names, and stop generated locals colliding with parameters ([02d3d66](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/02d3d664865cbe517f1ee4ad6a2a06aa009087ed))
* generate the same OpenAPI method names under every culture ([c3a0b68](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c3a0b68182ace898dda3d1330d4f4a5c985618f6))
* keep filling a route token that compiled before ZRA005, so no working URL changes ([#340](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/340)) ([a651450](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/a65145050ba4257d23d606019155e41ac628274c)), closes [#333](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/333)
* omit a [Header] parameter whose value is null ([4009100](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4009100b7e12115f029542cf904e7abdfec55899))
* report skipped OpenAPI cookie parameters as warning ZRT001 ([#345](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/345)) ([861a68e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/861a68ea1cbe8a5ba7307764d99cc5d7c28ceefa))
* stop __CreateHttpError from boxing its headers enumerator ([#337](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/337)) ([ae0124f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/ae0124f83f161b13b04ec71e1bf7590f5f786930)), closes [#335](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/335)


### Performance Improvements

* make generator models value-equatable so incremental caching hits ([#334](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/334)) ([e0429bc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/e0429bc44b327f5cafedc286776f3e1c5fa35f07)), closes [#319](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/319)


### Code Refactoring

* make OpenApiInterfaceGenerator internal ([17e4254](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/17e4254f7e4e439b8d1095f259273aaafb601031))


### Documentation

* design OpenAPI model generation and AOT-safe serialization (Rest 3.0) ([#353](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/353)) ([05d2f4d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/05d2f4d1586e081f037f27594718432ca746e981))
* document retrying a failed Result with RetryWhen and DelayHint ([#327](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/327)) ([a6b1ff9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/a6b1ff9509104c817dc02549336160697258e4f1)), closes [#325](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/325)
* drop the C# API section from openapi-codegen.md ([17e4254](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/17e4254f7e4e439b8d1095f259273aaafb601031))

## [2.1.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v2.0.1...v2.1.0) (2026-09-26)


### Features

* expose the error response body on HttpError ([b998124](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/b998124508ba60387e00259f3b745fb6f46b90ab))
* map Result failures to a user-defined error type ([4cca929](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4cca929e2af7661fdf461d8fb49ecaec91c033fd))


### Bug Fixes

* generate valid, distinct field names for injected serializers ([4cca929](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4cca929e2af7661fdf461d8fb49ecaec91c033fd))
* include content headers such as Content-Type in HttpError.Headers ([b998124](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/b998124508ba60387e00259f3b745fb6f46b90ab))
* report ZRA001 at the offending method ([4cca929](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4cca929e2af7661fdf461d8fb49ecaec91c033fd))

## [2.0.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v2.0.0...v2.0.1) (2026-09-25)


### Bug Fixes

* mark released analyzer rules and public api as shipped and automate the move ([#311](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/311)) ([5e8a18e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/5e8a18ebdd658df4d82586d0763ac0a10e8cee88))

## [2.0.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.3.5...v2.0.0) (2026-09-25)


### ⚠ BREAKING CHANGES

* Result-returning methods now return failures for transport, timeout and deserialization errors instead of throwing them. Caller cancellation still throws. Because these failures are no longer thrown, ZeroAlloc.Resilience [Retry] no longer retries them on such methods. See docs/migrating-to-v2.md.
* UseSerializer<T>() on a generated Add{I} or on AddRestResilience no longer registers the app-wide IRestSerializer. A client that relied on another client's UseSerializer for its serializer now throws InvalidOperationException when resolved. Register an app-wide default with services.AddRestSerializer<T>(), or call UseSerializer on that client. AddRestResilience now requires TRestClient to implement IGeneratedRestClient<TRestClient>, so a hand-written client no longer compiles with it; implement that interface or register the resilience proxy manually. Every generated client now implements IGeneratedRestClient<TSelf> explicitly, with static Create and AddSerializers members. An interface-level [Serializer] combined with UseSerializer now throws at registration; in 1.x the attribute was ignored. Per-client serializers are keyed services, so a client that calls UseSerializer needs a service provider implementing IKeyedServiceProvider. See docs/migrating-to-v2.md.

### Features

* return transport, timeout and deserialization failures from result methods ([#310](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/310)) ([296a012](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/296a0120706effc104e526c82fc9811d5da21b75)), closes [#299](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/299)
* scope serializers per client and honour interface-level serializer attribute ([#306](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/306)) ([97d2a26](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/97d2a26ff3da775ea54136b5636d513ed4a25ca0))

## [1.3.5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.3.4...v1.3.5) (2026-09-24)


### Bug Fixes

* generated client and DI extensions follow the interface's accessibility ([#296](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/296)) ([f3f1567](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f3f1567f137ab14c4c8d26cd108f7df62ef6fac5)), closes [#295](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/295)

## [1.3.4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.3.3...v1.3.4) (2026-09-20)


### Bug Fixes

* **ci:** pin the SDK floor at the .NET 10 GA band, not the newest patch ([#273](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/273)) ([891816d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/891816d1d337c52686ea0ec2e0ea214d7ad0f4bd))

## [1.3.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.3.2...v1.3.3) (2026-09-20)


### Bug Fixes

* declare current sibling package versions ([#269](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/269)) ([5dcdbc8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/5dcdbc81be25804cd077b585b680df328a6e920b))

## [1.3.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.3.1...v1.3.2) (2026-09-19)


### Bug Fixes

* **ci:** stamp the assembly version when publishing from a manifest ([#264](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/264)) ([ef40adf](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/ef40adf59345a0e041df8c6c1e579fd1edf41d96))

## [1.3.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.3.0...v1.3.1) (2026-08-07)


### Bug Fixes

* **ci:** pack the local feed before testing in the publish job ([#180](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/180)) ([5fdd1d8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/5fdd1d86703fd9e56da6db975c16bf5948557534))
* **ci:** pack the local feed in the rescue workflow too ([#182](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/182)) ([76ec18c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/76ec18c2690e1336bd558ebc4da15bf3f2e891b1))

## [1.3.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.2.2...v1.3.0) (2026-08-07)


### Bug Fixes

* **deps:** release accumulated dependency updates ([#177](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/177)) ([1ee11db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/1ee11db5a02b799f9dedc9f8bc64af3b65472bb0))

## [1.2.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.2.1...v1.2.2) (2026-06-15)


### Bug Fixes

* **deps:** bump Refit 10.2.0 (revoked cert) + MessagePack 3.1.7 (CVE) ([#125](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/125)) ([56a0c8d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/56a0c8db8e9977e84638f67d6132080d91520947))

## [1.2.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.2.0...v1.2.1) (2026-05-19)


### Documentation

* **backlog:** mark v1 deferred items as shipped ([#103](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/103)) ([aca4e3a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/aca4e3a23e65c16410834cfac575ce1977fc5956))

## [1.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.1.3...v1.2.0) (2026-05-19)


### Features

* **build:** emit ZR9001 when consumers reference both ZeroAlloc.Rest and ZeroAlloc.Rest.Generator ([#101](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/101)) ([1805260](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/1805260d3901a03c68b058dbd901f9cdeb45df4c))

## [1.1.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.1.2...v1.1.3) (2026-05-13)


### Documentation

* **benchmarks:** refresh vs-Refit numbers and add BENCH sentinels ([#95](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/95)) ([a35e459](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/a35e45940b125227b6636fa86b65812944a17932))

## [1.1.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.1.1...v1.1.2) (2026-05-12)


### Bug Fixes

* **readme:** absolute GitHub URLs so nuget.org links resolve ([#90](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/90)) ([0fa7594](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/0fa7594e42211c231c2cf8e4bdd1cf786e5fd7de))

## [1.1.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.1.0...v1.1.1) (2026-05-03)


### Bug Fixes

* **release-please:** drop pre-major flags (package is post-1.0) ([#76](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/76)) ([03f734d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/03f734dec2a5de6bbb687300a9c44ae1ecadaec6))

## [1.1.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.0.2...v1.1.0) (2026-05-01)


### Features

* bundle source generator into ZeroAlloc.Rest package ([58c5cec](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/58c5cec1cc15165c3ce69680a812b347da523550))
* bundle source generator into ZeroAlloc.Rest package ([c061093](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c061093c6d5f65687fa59779ed8c5d20f42605d1)), closes [#67](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/67)
* lock public API surface (PublicApiAnalyzers + api-compat gate) ([#72](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/72)) ([357be70](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/357be703ae5bd043f1b1fc5c1c94d995ed5ac032))


### Documentation

* update install instructions for bundled generator ([de73231](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/de73231bbd878d4ba607390feb00283839836320))

## [1.0.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.0.1...v1.0.2) (2026-04-30)


### Bug Fixes

* pack generator DLL under analyzers/dotnet/cs ([#67](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/67)) ([c822910](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c8229105341d1941f29d47d9f81ca5fed55e6c1c))
* pack generator DLL under analyzers/dotnet/cs ([#67](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/67)) ([8852b8f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/8852b8f8befda5397bd247de855c94f8380a4837))

## [1.0.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v1.0.0...v1.0.1) (2026-04-29)


### Documentation

* **readme:** standardize 5-badge set ([7682939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/7682939de747e967cef37728125b524f9bf1c06c))
* **readme:** standardize 5-badge set (NuGet/Build/License/AOT/Sponsors) ([2130781](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/21307810572cd9af7445e160283d289f2a2950c7))

## [1.0.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v0.1.4...v1.0.0) (2026-04-28)


### Features

* **rest:** emit rest.requests_total + rest.request_duration_ms ([#47](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/47)) ([#58](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/58)) ([6d1e67a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/6d1e67a08f4514e1388346a84f17e5739c804dfd))


### Miscellaneous Chores

* **release:** promote to 1.0.0 stability milestone ([0623145](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/0623145f0628f5d9225ac4644f4a36735ada8dff))

## 1.0.0

Stability milestone — public API of `ZeroAlloc.Rest` is now considered stable. Bundles in PR #58 (added `rest.requests_total` counter + `rest.request_duration_ms` histogram + `rest.method` tag for OpenTelemetry instrumentation). This release marks the transition out of pre-1.0 SemVer.

## [0.1.4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v0.1.3...v0.1.4) (2026-04-25)


### Features

* telemetry spans, TypedId route params, Rest.Resilience bridge + NuGet isolation ([#51](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/51)) ([ad5b7ff](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/ad5b7ff238edf60175f06ab3e0dc7d843980772c))


### Bug Fixes

* resolve CPM violations, cross-repo test references, and transitive NuGet audit warnings ([103cad3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/103cad312162a7d0564fd5716925a984bca80230))
* resolve CPM violations, cross-repo test refs, and transitive NuGet audit warnings ([494ce81](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/494ce8119ad037610eec7148e04cf4963e5bca3c))

## [0.1.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v0.1.2...v0.1.3) (2026-04-23)


### Bug Fixes

* **generator:** emit [UnconditionalSuppressMessage] instead of [RequiresDynamicCode] on client methods ([#43](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/43)) ([879335e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/879335e8ab6aba2a1c9933652da78be32e8ab684))

## [0.1.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v0.1.1...v0.1.2) (2026-04-14)


### Features

* **generator:** add [FormBody] attribute for form-encoded requests ([01889e5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/01889e55c5867346a768011a51270fd529960c58))
* **generator:** emit static [Header] values on methods ([e236bc4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/e236bc4da30071c614c1c9dd79f82c4a81fc85f2))
* **generator:** static headers, collection query params, and [FormBody] ([ee96cf2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/ee96cf2eca06600205829ed9863529f926ae7489))
* **generator:** support IEnumerable&lt;T&gt; [Query] params as repeated keys ([1c4eedc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/1c4eedc9e53b98fe9bf36ea0d601de1f9b43dbce))


### Bug Fixes

* **generator:** diagnose conflicting [Body]+[FormBody]; assert Content-Type in form body test ([c7bb02f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c7bb02f9b9c4633c2797fec0c6a78d0eb301f465))
* **generator:** document additive Accept header behavior; strengthen static header test assertion ([17cbab7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/17cbab7b20cb1560af05d29940ab954706112c25))
* **generator:** skip null collection items; align NRT annotation on collection query params ([94ba264](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/94ba2643f9395c19a650754ad854bc9c8b78f588))

## [0.1.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/compare/v0.1.0...v0.1.1) (2026-04-01)


### Features

* add BenchmarkDotNet benchmarks comparing ZeroAlloc.Rest vs Refit vs HttpClient ([c7c4e32](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c7c4e3264ba01eb336bd9ad78d9e57e7e099d910))
* add core HTTP method and parameter attributes ([80be98f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/80be98fe5162ddd4115dfcaa601af691e6cb5b36))
* add DI registration infrastructure ([8f0cc8d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/8f0cc8df1b33f9be8ec99c57b97915b524cfd921))
* add HttpError record and HeapPooledListExtensions helper ([f5b134b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f5b134b1b6a70e00bf434d7bc4b8ee362b5504ea))
* add HttpError record and HeapPooledListExtensions helper ([8309da8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/8309da83669bf74fc46cdab2d1ec61899b1bdbd6))
* add IRestSerializer abstraction and ApiResponse&lt;T&gt; ([924b722](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/924b722c026c57b4eb9a002ee3f626587f15b840))
* add MemoryPack serializer adapter ([4451c49](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4451c496b7386584e0a43a11be8dece32b0d7576))
* add MessagePack serializer adapter ([af7f35d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/af7f35d4c267373abeff028f6f8d2de52da3fdc9))
* add MSBuild task for OpenAPI code generation ([712aee5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/712aee5eacd97ecca2790f868c4edff9780cee1c))
* add OpenAPI interface generator and dotnet CLI tool ([27a7bde](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/27a7bde6f0809cd5ce9024941d4ef192842be7d1))
* add System.Text.Json serializer adapter ([48c1fb0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/48c1fb0b2a782089722d754cc0ded64ca7cf605b))
* **core:** add RestSerializerAdapter&lt;T&gt; bridging ISerializer&lt;T&gt; to IRestSerializer ([6aa08f8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/6aa08f83549eac88e86fb7559540189d6c023045))
* emit DI extension method from source generator ([f40ef26](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f40ef2608b49bdf18a2ff6ddd588722e3895a0bf))
* implement model extraction from interface symbols ([04890ad](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/04890ad5629c4f040c3b21fd26f963d83553a2d3))
* implement per-method [Serializer] override ([bdc4a7e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/bdc4a7e20ff0ead330ab3abef821df40bcd102d0))
* implement source generator code emission ([2aeb31d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/2aeb31d811e93905eed835d4bd29c962d28f5307))
* integrate ZeroAlloc.Results, ZeroAlloc.Collections, and ZeroAlloc.Analyzers ([ceac64d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/ceac64d46de9fbacac1ecab67936790770298fc2))
* replace ApiResponse&lt;T&gt; with Result&lt;T,HttpError&gt; in generator models ([4b00869](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4b00869788448cd894b6c98b13a64e582f888ba0))
* replace ApiResponse&lt;T&gt; with Result&lt;T,HttpError&gt; in generator models ([e013791](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/e013791fe6070170afe4e92a3f20a8ad3c063a6e))
* scaffold solution structure with all projects ([3f2300a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/3f2300aa8b2a0632007fd732e021eb4414a8ee88))
* scaffold source generator with incremental provider ([04932db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/04932dbe90915d02128b25313b187b6ea427bbdf))
* update ClientEmitter to emit Result&lt;T,HttpError&gt; and HeapPooledList URL builder ([f749009](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/f749009e61a9260a60f11642b9a6c1c8421d7ff2))
* update ClientEmitter to emit Result&lt;T,HttpError&gt; and HeapPooledList URL builder ([a8f9462](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/a8f946265beddd52076260dc336bc4780d7c05e5))


### Bug Fixes

* add AOT annotations to IRestSerializer, strengthen header type ([1703f16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/1703f164bbfe5f8de6dfaab8691f7bdbba5e42c4))
* add AOT annotations, fix MemoryStream leak, refactor response handling ([a2e891b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/a2e891bdf5a0cf09a725db78ccf28bb538963a56))
* add null-forgiving operator to non-nullable query param ToString call ([ba26b4d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/ba26b4d130b3bc7731936fae07e0c187f707f98a))
* address code review findings ([dd5d629](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/dd5d629811ca8de7b111cd51c53f28fa9ee5248e))
* address code review findings ([2683afb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/2683afbf2c821d1f16a8e7dd5c0ce680ad917893))
* **core:** replace local ZeroAlloc.Serialisation project ref with NuGet package 1.0.0 ([024b279](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/024b279c3efaa1447ae081218fecb568f31f68a6))
* correct package versions and add MSBuild intent comment ([598ca8a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/598ca8a0ea55d1ec3154c4afdc4368e0e56ba16b))
* dispose ServiceProvider in integration tests and fix trailing ? in query URL ([e8a491f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/e8a491f68622c77e763956b0375e6337f7df26f1))
* guard empty serializer name, extract shared dedup, dispose benchmark handler, test OpenAPI error path ([90aeb8d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/90aeb8d879e0ab96915e2a42a5a13342e6454530))
* map OpenAPI response types, expose parse errors, fix PascalCase for snake_case operation IDs ([49a4619](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/49a461990da4b10929e6b848952bced7ebe25cde))
* prevent serializer field name collision, dispose all benchmark HttpClients ([8460233](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/8460233c88873aea2f597e1bd8d17abd0482d790))
* replace out-of-docs relative link in benchmarks.md with inline code ([284c886](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/284c8860d43176074690ee6b4aa1e9f2500fcbb9))
* resolve MSBuild task deadlock risk, missing StringComparison, and validation gaps ([c402cac](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/c402cac6447adb84caf14e12815d99f48372466b))
* safe empty-stream check and remove incorrect IsAotCompatible from STJ adapter ([380cf37](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/380cf37bb14d32ae1200ad319126b62b2bbdb690))
* send Accept header on all requests, remove GetAsync fast path ([4a5eadc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/4a5eadcf1c5f4ab208ee2955e62d5f79524045c1))
* set slug: / on getting-started for root URL ([98c8db9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/98c8db9f1be618c53aae449265b9c0b72a74f913))
* skip null guard for non-nullable value type query parameters ([50570d9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/50570d94b835527e41c421aa95b56f44bbc59ae4))
* strengthen ApiResponse detection and harden IReadOnlyList contracts ([5361fa3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/5361fa3afec82bb3902d36bb3965b50360b1992b))
* strengthen DI builder encapsulation and add null guard ([aff4b68](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/aff4b687cf25eb07f75f2043eed1d592c5e5e489))
* **tools:** port Program.cs to System.CommandLine 3.x API ([482aa39](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/482aa39894aa675e3125ee2578f3f6fe33582557))
* use AddHttpClient&lt;IInterface, Concrete&gt; and TryAddSingleton for serializer ([148628b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/148628bbe0c50038aed08aec7891ca59a91faf28))


### Performance Improvements

* add serializer benchmarks (STJ vs MemoryPack vs MessagePack) ([bcc6dad](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/bcc6daded58453541046ea1d60930bee2a18f8c1))
* add serializer benchmarks (STJ vs MemoryPack vs MessagePack) ([1011127](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/1011127da29717ab6eb7156e078a94fc12837ff5))
* extend benchmarks with query-param, delete, and Result&lt;T,HttpError&gt; scenarios ([b983501](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/b9835012636a972b434dc57a89720ff0e603282f))
* extend benchmarks with query-param, delete, and Result&lt;T,HttpError&gt; scenarios ([7287b76](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/7287b769cdee1b4eadcd7fbbc865caf982bbd817))
* **memorypack:** eliminate intermediate byte[] in SerializeAsync ([d659ab5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/commit/d659ab5c5708361f8d2891bdbd0dc85677f29160))

## Changelog
