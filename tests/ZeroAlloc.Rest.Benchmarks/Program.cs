using BenchmarkDotNet.Running;
using ZeroAlloc.Rest.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RestClientBenchmarks).Assembly).RunAll();
