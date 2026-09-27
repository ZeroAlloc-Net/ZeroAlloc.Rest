using MemoryPack;

namespace ZeroAlloc.Rest.MemoryPackAotSmoke;

// Registered with the MemoryPack serializer through types.Add<SmokeParcel>().
[MemoryPackable]
public sealed partial record SmokeParcel(int Id, string Name, double Weight);
