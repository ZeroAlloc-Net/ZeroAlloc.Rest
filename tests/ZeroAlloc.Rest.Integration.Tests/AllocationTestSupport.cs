using Xunit;

namespace ZeroAlloc.Rest.Integration.Tests;

// Allocation budgets hold for optimized builds. A Debug build compiles every async method's state
// machine as a class, so the generated client allocates it on each call there.
[AttributeUsage(AttributeTargets.Method)]
public sealed class OptimizedBuildFactAttribute : FactAttribute
{
#if DEBUG
    public OptimizedBuildFactAttribute() =>
        Skip = "Allocation budgets apply to Release builds; Debug compiles async state machines as classes.";
#endif
}

/// <inheritdoc cref="OptimizedBuildFactAttribute"/>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OptimizedBuildTheoryAttribute : TheoryAttribute
{
#if DEBUG
    public OptimizedBuildTheoryAttribute() =>
        Skip = "Allocation budgets apply to Release builds; Debug compiles async state machines as classes.";
#endif
}

internal static class SynchronousCall
{
    // AllocationGate's synchronous gates count this thread only, so every step of a measured call
    // must complete synchronously. A plain check rather than an assertion: an assertion object
    // would allocate in the loop. A faulted call rethrows its exception.
    public static void Require(Task call)
    {
        if (call.IsCompletedSuccessfully)
            return;
        if (call.IsFaulted)
            call.GetAwaiter().GetResult();
        throw new InvalidOperationException("The call did not complete synchronously, so not all its allocations were on this thread.");
    }
}

internal static class RuntimeAllocation
{
    // net8.0's HttpClient.SendAsync allocates 8 B a call more than net10.0's. Measured with a bare
    // HttpClient over a synchronous stub handler and no ZeroAlloc code at all: 1,072 B on .NET 8
    // against 1,064 B on .NET 10, x64, Release. The budgets measured on .NET 10 add it on net8.0;
    // the gates against a hand-written call hold the generated code to the same cost on both.
#if NET10_0_OR_GREATER
    internal const int HttpClientSendExtraBytes = 0;
#else
    internal const int HttpClientSendExtraBytes = 8;
#endif
}
