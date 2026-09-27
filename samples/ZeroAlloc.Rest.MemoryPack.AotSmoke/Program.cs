using System;
using ZeroAlloc.Rest.MemoryPackAotSmoke;

// MemoryPack through explicit type registration under Native AOT: built-in types without registration,
// a registered type read from a fixed payload and round-tripped, and a missing registration reported
// as InvalidOperationException, all without MemoryPack's reflection-based formatter lookup.
if (await MemoryPackSmoke.RunAsync().ConfigureAwait(false) is { } failure)
{
    Console.Error.WriteLine("MemoryPack AOT smoke: FAIL — " + failure);
    return 1;
}

Console.WriteLine("MemoryPack AOT smoke: PASS");
return 0;
