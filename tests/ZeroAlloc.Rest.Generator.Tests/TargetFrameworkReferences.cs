using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator.Tests;

// The BCL reference set of the framework this test run targets. ZeroAlloc.Rest ships net10.0 and
// net11.0, and the generator's output compiles inside consumers on either, so the net10.0 run
// compiles every generated client against net10.0's BCL and the net10.0 ZeroAlloc.Rest assembly.
// Generated code that used an API newer than net10.0 fails these tests there.
internal static class TargetFrameworkReferences
{
#if NET11_0_OR_GREATER
    internal static ImmutableArray<PortableExecutableReference> All => Basic.Reference.Assemblies.Net110.References.All;
#else
    internal static ImmutableArray<PortableExecutableReference> All => Basic.Reference.Assemblies.Net100.References.All;
#endif
}
