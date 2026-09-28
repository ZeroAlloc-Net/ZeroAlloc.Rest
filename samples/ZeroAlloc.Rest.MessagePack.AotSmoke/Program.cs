using System;
using ZeroAlloc.Rest.MessagePackAotSmoke;

// MessagePack through a source-generated resolver under Native AOT: built-in types, a
// [MessagePackObject] type read from a fixed payload and round-tripped, and a type the resolver does
// not cover reported as an error, all without MessagePack's reflection-based resolvers.
if (await MessagePackSmoke.RunAsync().ConfigureAwait(false) is { } failure)
{
    Console.Error.WriteLine("MessagePack AOT smoke: FAIL — " + failure);
    return 1;
}

Console.WriteLine("MessagePack AOT smoke: PASS");
return 0;
