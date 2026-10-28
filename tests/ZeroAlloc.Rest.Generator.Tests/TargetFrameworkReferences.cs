using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator.Tests;

// The BCL reference set of the framework this test run targets. ZeroAlloc.Rest ships net8.0 and
// net10.0, and the generator's output compiles inside consumers on either, so the net8.0 run
// compiles every generated client against net8.0's BCL and the net8.0 ZeroAlloc.Rest assembly.
// Generated code that used an API newer than net8.0 fails these tests there.
internal static class TargetFrameworkReferences
{
#if NET10_0_OR_GREATER
    internal static ImmutableArray<PortableExecutableReference> All => Basic.Reference.Assemblies.Net100.References.All;
#else
    // A net8.0 consumer of ZeroAlloc.Rest.SystemTextJson gets System.Text.Json 10 from its package
    // in place of the framework's System.Text.Json 8, as the SDK's conflict resolution keeps the
    // higher version. This test project references the same package, so the loaded assembly is it.
    internal static ImmutableArray<PortableExecutableReference> All { get; } =
    [
        .. Basic.Reference.Assemblies.Net80.References.All.Where(r =>
            !string.Equals(System.IO.Path.GetFileName(r.FilePath), "System.Text.Json.dll", System.StringComparison.OrdinalIgnoreCase)),
        MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location),
    ];
#endif
}
