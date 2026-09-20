using BenchmarkDotNet.Running;
using ZeroAlloc.Rest.Benchmarks;

// Run(args) rather than RunAll() so command-line arguments reach BenchmarkDotNet: CI runs this
// with --filter and reduced iteration counts as a smoke check. RunAll ignored them entirely and
// always executed a full measurement run.
BenchmarkSwitcher.FromAssembly(typeof(RestClientBenchmarks).Assembly).Run(args);
