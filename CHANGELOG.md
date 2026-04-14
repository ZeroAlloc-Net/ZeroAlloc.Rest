# Changelog

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
