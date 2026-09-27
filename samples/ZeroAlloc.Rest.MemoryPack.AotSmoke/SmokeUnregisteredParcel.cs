using MemoryPack;

namespace ZeroAlloc.Rest.MemoryPackAotSmoke;

// Never registered with the MemoryPack serializer: it must refuse the type.
[MemoryPackable]
public sealed partial record SmokeUnregisteredParcel(int Id);
