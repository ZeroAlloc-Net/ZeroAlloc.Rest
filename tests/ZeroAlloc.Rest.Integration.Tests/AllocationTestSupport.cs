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
    // net11.0 costs 8 B a call more than net10.0, and every gate sees it: the GET, POST, path, query,
    // catch-all and stream gates all read exactly their .NET 10 budget plus 8. On net11.0 the gates
    // that compare a generated call with the same call written by hand against HttpClient pass at
    // that budget, so the hand-written call costs the 8 B as well and the generated code adds none.
#if NET11_0_OR_GREATER
    internal const int Net11ExtraBytes = 8;
#else
    internal const int Net11ExtraBytes = 0;
#endif
}
