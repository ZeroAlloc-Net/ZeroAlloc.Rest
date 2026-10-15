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
